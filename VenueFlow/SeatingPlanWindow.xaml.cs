using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VenueFlow.Data;
using VenueFlow.Data.Models;
using VenueFlow.Services;

// Updated Record: Includes Width/Height specifically for drawing logic
public record TableCoordinates(int TableId, int TableNumber, double CenterX, double CenterY, double Width, double Height, int SeatingCapacity);

namespace VenueFlow
{
    public partial class SeatingPlanWindow : Window
    {
        private readonly VenueFlowDbContext _context;
        private readonly SeatingPlannerService _seatingService;
        private readonly int _weddingId;

        private Point _startPoint;
        private List<TableCoordinates> _currentTableCoordinates = new List<TableCoordinates>();

        public SeatingPlanWindow(VenueFlowDbContext context, SeatingPlannerService seatingService, int weddingId)
        {
            InitializeComponent();
            _context = context;
            _seatingService = seatingService;
            _weddingId = weddingId;

            Loaded += SeatingPlanWindow_Loaded;
            SizeChanged += SizeChangedIsolationHandler;
        }

        private async void SeatingPlanWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Ensure Tables Exist FIRST
            await EnsureTablesExistAsync();

            // 2. Run Algorithm (Now that tables definitely exist)
            await _seatingService.AutoSeatGuests(_weddingId);

            // 3. Draw
            await DrawRoomLayoutIsolated();
            await PopulateUnassignedGuestsIsolated();
        }

        private async void SizeChangedIsolationHandler(object sender, SizeChangedEventArgs e)
        {
            if (SeatingCanvas.ActualWidth > 0 && SeatingCanvas.ActualHeight > 0)
            {
                await DrawRoomLayoutIsolated();
            }
        }

        // --- DATA & LOGIC ---

        private async Task EnsureTablesExistAsync()
        {
            using (var isolatedContext = new VenueFlowDbContext())
            {
                bool tablesExist = await isolatedContext.Tables.AnyAsync(t => t.WeddingId == _weddingId);
                if (tablesExist) return;

                var wedding = await isolatedContext.Weddings.FindAsync(_weddingId);
                if (wedding == null) return;

                int guestTableCount = 6; // Default
                if (wedding.RoomCapacity <= 22) guestTableCount = 2;
                else if (wedding.RoomCapacity <= 42) guestTableCount = 4;

                // Create Sweetheart (Capacity 2)
                isolatedContext.Tables.Add(new Table { WeddingId = _weddingId, TableNumber = 0, SeatingCapacity = 2 });

                // Create Guest Tables (Capacity 10)
                for (int i = 1; i <= guestTableCount; i++)
                {
                    isolatedContext.Tables.Add(new Table { WeddingId = _weddingId, TableNumber = i, SeatingCapacity = 10 });
                }
                await isolatedContext.SaveChangesAsync();
            }
        }

        private async Task DrawRoomLayoutIsolated()
        {
            using (var isolatedContext = new VenueFlowDbContext())
            {
                var tables = await isolatedContext.Tables
                    .Include(t => t.Guests.Where(g => g.WeddingId == _weddingId))
                    .Where(t => t.WeddingId == _weddingId)
                    .OrderBy(t => t.TableNumber)
                    .ToListAsync();

                DrawRoomLayoutInternal(_weddingId, tables);
            }
        }

        private async Task PopulateUnassignedGuestsIsolated()
        {
            using (var isolatedContext = new VenueFlowDbContext())
            {
                var unassignedGuests = await isolatedContext.Guests
                    .Where(g => g.WeddingId == _weddingId && g.TableId == null)
                    .Select(g => new { g.GuestId, g.GuestName, g.FamilyGroup })
                    .ToListAsync();

                UnassignedGuestsListBox.ItemsSource = unassignedGuests.Select(g =>
                    $"{g.GuestName} ({g.FamilyGroup}) [ID:{g.GuestId}]"
                ).ToList();
            }
        }

        // --- DRAG AND DROP ---

        private void UnassignedGuestsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        private void UnassignedGuestsListBox_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition(null);
            Vector diff = _startPoint - mousePos;

            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                ListBox listBox = sender as ListBox;
                if (listBox.SelectedItem == null) return;
                string dragData = (string)listBox.SelectedItem;
                DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);
            }
        }

        private async void SeatingCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string data = (string)e.Data.GetData(DataFormats.StringFormat);
                var idString = data.Split(new[] { "[ID:", "]" }, System.StringSplitOptions.None)
                                   .Skip(1).FirstOrDefault();
                if (!int.TryParse(idString, out int guestId)) return;

                Point dropPosition = e.GetPosition(SeatingCanvas);

                // Use simple radius check based on the drawn Width/2
                var droppedOnTable = _currentTableCoordinates.FirstOrDefault(tc =>
                    (dropPosition.X - tc.CenterX) * (dropPosition.X - tc.CenterX) +
                    (dropPosition.Y - tc.CenterY) * (dropPosition.Y - tc.CenterY) <= ((tc.Width / 2) * (tc.Width / 2))
                );

                if (droppedOnTable != null)
                {
                    int targetTableId = droppedOnTable.TableId;
                    var guest = await _context.Guests.FindAsync(guestId);

                    if (guest != null)
                    {
                        int currentSeated = await _context.Guests.CountAsync(g => g.TableId == targetTableId);
                        if (currentSeated >= droppedOnTable.SeatingCapacity)
                        {
                            MessageBox.Show("Table is full.", "Capacity", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        var allPreferences = await _context.SeatingPreferences.ToListAsync();
                        var allGuestsCurrentState = await _context.Guests.Where(g => g.WeddingId == _weddingId).ToListAsync();
                        var groupOfOne = new List<Guest> { guest };

                        if (_seatingService.CheckMustNotSitWithConflict(groupOfOne, targetTableId, allPreferences, allGuestsCurrentState))
                        {
                            guest.TableId = targetTableId;
                            await _context.SaveChangesAsync();
                            await DrawRoomLayoutIsolated();
                            await PopulateUnassignedGuestsIsolated();
                        }
                        else
                        {
                            MessageBox.Show("Constraint Conflict!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
        }

        // --- DRAWING LOGIC (VISUAL FIXES) ---

        private List<TableCoordinates> GetTableCoordinates(int weddingId, List<Table> tables)
        {
            // Fallback size if canvas isn't ready
            double canvasWidth = SeatingCanvas.ActualWidth > 0 ? SeatingCanvas.ActualWidth : 1200;
            double canvasHeight = SeatingCanvas.ActualHeight > 0 ? SeatingCanvas.ActualHeight : 800;

            // Larger radius for guest tables to fit 10 people
            double guestTableRadius = 90;
            // Smaller size for Sweetheart table
            double sweetheartWidth = 140;
            double sweetheartHeight = 70;

            double padding = 60;

            if (!tables.Any()) return new List<TableCoordinates>();

            _currentTableCoordinates.Clear();

            int guestTableCount = tables.Count(t => t.TableNumber > 0);

            // Sweetheart (Top Center)
            var sweetheart = tables.FirstOrDefault(t => t.TableNumber == 0);
            if (sweetheart != null)
            {
                _currentTableCoordinates.Add(new TableCoordinates(
                    sweetheart.TableId, 0,
                    canvasWidth / 2, padding + (sweetheartHeight / 2),
                    sweetheartWidth, sweetheartHeight,
                    sweetheart.SeatingCapacity));
            }

            // Guest Table Layout
            int tablesPerRow = 0;
            int rowCount = 0;

            // Logic for rows based on count
            if (guestTableCount <= 2) { tablesPerRow = 2; rowCount = 1; }
            else if (guestTableCount <= 4) { tablesPerRow = 2; rowCount = 2; }
            else { tablesPerRow = 3; rowCount = (int)Math.Ceiling((double)guestTableCount / 3); }

            // Define grid area below sweetheart table
            double gridAreaTop = padding + sweetheartHeight + padding;
            double gridHeight = canvasHeight - gridAreaTop - padding;

            // Calculate spacing cells
            double cellWidth = (canvasWidth - (2 * padding)) / Math.Max(1, tablesPerRow);
            double cellHeight = gridHeight / Math.Max(1, rowCount);

            var guestTablesList = tables.Where(t => t.TableNumber > 0).OrderBy(t => t.TableNumber).ToList();

            for (int i = 0; i < guestTablesList.Count; i++)
            {
                var table = guestTablesList[i];
                int row = i / tablesPerRow;
                int col = i % tablesPerRow;

                // Center within the grid cell
                double centerX = padding + (col * cellWidth) + (cellWidth / 2);
                double centerY = gridAreaTop + (row * cellHeight) + (cellHeight / 2);

                // Use Diameter (Radius * 2) for width/height storage
                _currentTableCoordinates.Add(new TableCoordinates(
                    table.TableId, table.TableNumber,
                    centerX, centerY,
                    guestTableRadius * 2, guestTableRadius * 2,
                    table.SeatingCapacity));
            }

            return _currentTableCoordinates;
        }

        private void DrawRoomLayoutInternal(int weddingId, List<Table> tables)
        {
            SeatingCanvas.Children.Clear();
            var tableCoords = GetTableCoordinates(weddingId, tables);

            if (tableCoords.Count == 0)
            {
                SeatingCanvas.Children.Add(new TextBlock { Text = "No tables found.", FontSize = 16, Foreground = Brushes.Red, Margin = new Thickness(20) });
                return;
            }

            foreach (var coord in tableCoords)
            {
                var table = tables.FirstOrDefault(t => t.TableId == coord.TableId);
                if (table == null) continue;

                // Draw Table
                Shape tableShape;
                if (table.TableNumber == 0)
                {
                    // Sweetheart Rectangle
                    tableShape = new Rectangle
                    {
                        Width = coord.Width,
                        Height = coord.Height,
                        Fill = Brushes.Lavender,
                        Stroke = Brushes.Indigo,
                        StrokeThickness = 2,
                        RadiusX = 10,
                        RadiusY = 10 // Rounded corners
                    };
                    Canvas.SetLeft(tableShape, coord.CenterX - (coord.Width / 2));
                    Canvas.SetTop(tableShape, coord.CenterY - (coord.Height / 2));
                }
                else
                {
                    // Guest Circle
                    tableShape = new Ellipse
                    {
                        Width = coord.Width,
                        Height = coord.Height,
                        Fill = Brushes.WhiteSmoke,
                        Stroke = Brushes.Gray,
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(tableShape, coord.CenterX - (coord.Width / 2));
                    Canvas.SetTop(tableShape, coord.CenterY - (coord.Height / 2));
                }
                SeatingCanvas.Children.Add(tableShape);

                // Draw Label
                TextBlock tableLabel = new TextBlock
                {
                    Text = (table.TableNumber == 0 ? "Sweetheart" : $"Table {table.TableNumber}") + $"\n({table.Guests.Count}/{table.SeatingCapacity})",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14
                };
                tableLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tableLabel, coord.CenterX - (tableLabel.DesiredSize.Width / 2));
                Canvas.SetTop(tableLabel, coord.CenterY - (tableLabel.DesiredSize.Height / 2));
                SeatingCanvas.Children.Add(tableLabel);

                // Draw Seats (Placemats)
                int seatedCount = table.Guests.Count;
                if (seatedCount > 0)
                {
                    // Distance from center to seat center. 
                    // Use half width (Radius) + extra buffer for placemat size
                    double layoutRadius = (coord.Width / 2) + 35;

                    // Angle setup: Sweetheart (2 seats) needs different angles than Round (10 seats)
                    double startAngle = (table.TableNumber == 0) ? 180 : -90;
                    double angleStep = (table.TableNumber == 0) ? 180 : (360.0 / table.SeatingCapacity);

                    // We iterate through CAPACITY (drawing empty seats if needed?) 
                    // For now, let's just draw the seated guests.
                    for (int j = 0; j < seatedCount; j++)
                    {
                        var guest = table.Guests.Skip(j).First();

                        double angle = startAngle + (j * angleStep);
                        double radians = angle * (Math.PI / 180.0);

                        // Position calculation
                        double seatX = coord.CenterX + layoutRadius * Math.Cos(radians);
                        double seatY = coord.CenterY + layoutRadius * Math.Sin(radians);

                        // Placemat Rectangle
                        Rectangle placemat = new Rectangle
                        {
                            Width = 60,
                            Height = 30,
                            Fill = (!string.IsNullOrEmpty(guest.Allergies) && guest.Allergies != "None") ? Brushes.LightPink : Brushes.LightBlue,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2
                        };

                        // Rotate placemat to face center (Optional visual flair)
                        RotateTransform rotate = new RotateTransform(angle + 90);
                        placemat.RenderTransformOrigin = new Point(0.5, 0.5);
                        placemat.RenderTransform = rotate;

                        Canvas.SetLeft(placemat, seatX - 30);
                        Canvas.SetTop(placemat, seatY - 15);
                        SeatingCanvas.Children.Add(placemat);

                        // Guest Name
                        TextBlock nameText = new TextBlock
                        {
                            Text = guest.GuestName.Split(' ')[0], // First name only for space
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            Width = 60,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            ToolTip = $"{guest.GuestName}\n{guest.DietaryRestrictions ?? "None"}\n{guest.Allergies ?? "None"}"
                        };

                        // Rotate text to match placemat? Maybe keep text horizontal for readability.
                        // Let's keep text horizontal but centered on placemat.
                        Canvas.SetLeft(nameText, seatX - 30);
                        Canvas.SetTop(nameText, seatY - 7); // Vertically center in 30px height
                        SeatingCanvas.Children.Add(nameText);
                    }
                }
            }
        }
    }
}