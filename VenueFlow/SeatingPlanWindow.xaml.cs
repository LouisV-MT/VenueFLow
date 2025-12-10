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

// Helper record for table position calculation
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
        }

        private async void SeatingPlanWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure Tables Exist FIRST
            await EnsureTablesExistAsync();

            // Run Algorithm (Now that tables definitely exist)
            await _seatingService.AutoSeatGuests(_weddingId);

            // Draw
            await DrawRoomLayoutIsolated();
            await PopulateUnassignedGuestsIsolated();
        }

        // --- DATA & LOGIC ---

        private async Task EnsureTablesExistAsync()
        {
            using (var isolatedContext = new VenueFlowDbContext())
            {
                // Determine Target Table Count based on Wedding Room Capacity
                var wedding = await isolatedContext.Weddings.FindAsync(_weddingId);
                if (wedding == null) return;

                int targetGuestTables;
                if (wedding.RoomCapacity <= 22) targetGuestTables = 2;
                else if (wedding.RoomCapacity <= 42) targetGuestTables = 4;
                else targetGuestTables = 6; // Default/Large

                // Check what we currently have
                // Count guest tables (TableNumber > 0)
                int currentGuestTables = await isolatedContext.Tables
                    .CountAsync(t => t.WeddingId == _weddingId && t.TableNumber > 0);

                // Check for Sweetheart table
                bool sweetheartExists = await isolatedContext.Tables
                    .AnyAsync(t => t.WeddingId == _weddingId && t.TableNumber == 0);

                bool changesMade = false;

                // Add Sweetheart if missing
                if (!sweetheartExists)
                {
                    isolatedContext.Tables.Add(new Table { WeddingId = _weddingId, TableNumber = 0, SeatingCapacity = 2 });
                    changesMade = true;
                }

                // Add missing Guest Tables (if we upgraded the room size)
                if (currentGuestTables < targetGuestTables)
                {
                    for (int i = currentGuestTables + 1; i <= targetGuestTables; i++)
                    {
                        isolatedContext.Tables.Add(new Table
                        {
                            WeddingId = _weddingId,
                            TableNumber = i,
                            SeatingCapacity = 10
                        });
                    }
                    changesMade = true;
                }

                if (changesMade)
                {
                    await isolatedContext.SaveChangesAsync();
                }
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

        // --- DRAWING LOGIC ---

        private List<TableCoordinates> GetTableCoordinates(int weddingId, List<Table> tables)
        {
            if (!tables.Any()) return new List<TableCoordinates>();

            _currentTableCoordinates.Clear();

            int guestTableCount = tables.Count(t => t.TableNumber > 0);

            // --- Layout Constants ---
            // Large fixed sizes to ensure no overlapping
            double cellWidth = 350;
            double cellHeight = 350;
            double sweetheartAreaHeight = 250;

            // Calculate how many columns we want based on total tables
            int tablesPerRow = 2; // Default for small
            if (guestTableCount >= 5) tablesPerRow = 3; // Wider for large weddings

            int rowCount = (int)Math.Ceiling((double)guestTableCount / tablesPerRow);

            // CALCULATE CANVAS SIZE
            // We set the canvas size explicitly. The ScrollViewer will see this and enable scrolling.
            double totalCanvasWidth = Math.Max(1000, (tablesPerRow * cellWidth) + 100);
            double totalCanvasHeight = sweetheartAreaHeight + (rowCount * cellHeight) + 100;

            SeatingCanvas.Width = totalCanvasWidth;
            SeatingCanvas.Height = totalCanvasHeight;

            // Sweetheart (Top Center)
            var sweetheart = tables.FirstOrDefault(t => t.TableNumber == 0);
            if (sweetheart != null)
            {
                _currentTableCoordinates.Add(new TableCoordinates(
                    sweetheart.TableId, 0,
                    totalCanvasWidth / 2,
                    100, // Fixed top offset 
                    160, 80, // Size
                    sweetheart.SeatingCapacity));
            }

            // Guest Tables
            var guestTablesList = tables.Where(t => t.TableNumber > 0).OrderBy(t => t.TableNumber).ToList();

            // Calculate starting X to center the grid horizontally
            double gridStartX = (totalCanvasWidth - (tablesPerRow * cellWidth)) / 2;

            for (int i = 0; i < guestTablesList.Count; i++)
            {
                var table = guestTablesList[i];
                int row = i / tablesPerRow;
                int col = i % tablesPerRow;

                // Center of the cell
                double centerX = gridStartX + (col * cellWidth) + (cellWidth / 2);
                double centerY = sweetheartAreaHeight + (row * cellHeight) + (cellHeight / 2);

                _currentTableCoordinates.Add(new TableCoordinates(
                    table.TableId, table.TableNumber,
                    centerX, centerY,
                    180, 180, // 180 diameter = 90 radius (Matches visual logic)
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

                // Draw Placemats
                int seatedCount = table.Guests.Count;
                if (seatedCount > 0)
                {
                    for (int j = 0; j < seatedCount; j++)
                    {
                        var guest = table.Guests.Skip(j).First();

                        double seatX, seatY;
                        double rotationAngle;

                        // --- CUSTOM LOGIC FOR SWEETHEART TABLE (Table 0) ---
                        if (table.TableNumber == 0)
                        {
                            // Place seats side-by-side ABOVE the table (linear layout)
                            double spacing = 70; // Distance between seats
                            double totalWidth = spacing * (seatedCount - 1);
                            double startX = coord.CenterX - (totalWidth / 2);

                            seatX = startX + (j * spacing);
                            seatY = coord.CenterY - (coord.Height / 2) - 25; // 25px buffer above top edge

                            rotationAngle = 0; // No rotation (Horizontal)
                        }
                        else
                        {
                            // --- STANDARD CIRCULAR LOGIC FOR GUEST TABLES ---
                            double layoutRadius = (coord.Width / 2) + 35;
                            double angleStep = 360.0 / table.SeatingCapacity;
                            double startAngle = -90; // Start at top

                            double angle = startAngle + (j * angleStep);
                            double radians = angle * (Math.PI / 180.0);

                            seatX = coord.CenterX + layoutRadius * Math.Cos(radians);
                            seatY = coord.CenterY + layoutRadius * Math.Sin(radians);

                            rotationAngle = angle + 90; // Rotate to face center
                        }

                        // Placemat
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

                        RotateTransform rotate = new RotateTransform(rotationAngle);
                        placemat.RenderTransformOrigin = new Point(0.5, 0.5);
                        placemat.RenderTransform = rotate;

                        Canvas.SetLeft(placemat, seatX - 30);
                        Canvas.SetTop(placemat, seatY - 15);
                        SeatingCanvas.Children.Add(placemat);

                        // Text
                        TextBlock nameText = new TextBlock
                        {
                            Text = $"{guest.GuestName.Split(' ')[0]}\n({(string.IsNullOrEmpty(guest.DietaryRestrictions) ? "" : "Diet")})",
                            FontSize = 9,
                            Foreground = Brushes.Black,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            Width = 60,
                            Height = 30,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        };
                        nameText.RenderTransformOrigin = new Point(0.5, 0.5);
                        nameText.RenderTransform = rotate;

                        Canvas.SetLeft(nameText, seatX - 30);
                        Canvas.SetTop(nameText, seatY - 15);
                        SeatingCanvas.Children.Add(nameText);
                    }
                }
            }
        }
    }
}