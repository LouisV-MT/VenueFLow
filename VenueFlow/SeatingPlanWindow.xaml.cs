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
using VenueFlow.Data.Models;
using VenueFlow.Services;

namespace VenueFlow
{
    // Helper record for table position calculation
    public record TableCoordinates(int TableId, int TableNumber, double CenterX, double CenterY, double Radius, int SeatingCapacity);

    public partial class SeatingPlanWindow : Window
    {
        // Assuming your DbContext and models are in these namespaces
        private readonly VenueFlowDbContext _context;
        private readonly SeatingPlannerService _seatingService;
        private const int WeddingId = 1; // Assuming a single active wedding for MVP

        private Point _startPoint;
        private List<TableCoordinates> _currentTableCoordinates = new List<TableCoordinates>();


        public SeatingPlanWindow(VenueFlowDbContext context, SeatingPlannerService seatingService)
        {
            InitializeComponent();
            _context = context;
            _seatingService = seatingService;

            Loaded += SeatingPlanWindow_Loaded;

            // Re-draw when the window is resized to ensure tables fit the canvas
            SizeChanged += async (s, e) => await DrawRoomLayout(WeddingId);
        }

        private async void SeatingPlanWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await DrawRoomLayout(WeddingId);
            await PopulateUnassignedGuests();
        }

        private async Task PopulateUnassignedGuests()
        {
            var unassignedGuests = await _context.Guests
                .Where(g => g.WeddingId == WeddingId && g.TableId == null)
                .Select(g => new { g.GuestId, g.GuestName, g.FamilyGroup })
                .ToListAsync();

            UnassignedGuestsListBox.ItemsSource = unassignedGuests.Select(g =>
                $"{g.GuestName} ({g.FamilyGroup}) [ID:{g.GuestId}]"
            ).ToList();
        }

        // --- DRAG SOURCE LOGIC ---
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

        // --- DROP TARGET LOGIC ---
        private async void SeatingCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string data = (string)e.Data.GetData(DataFormats.StringFormat);

                var idString = data.Split(new[] { "[ID:", "]" }, System.StringSplitOptions.None)
                                   .Skip(1).FirstOrDefault();
                if (!int.TryParse(idString, out int guestId)) return;

                Point dropPosition = e.GetPosition(SeatingCanvas);

                // Find the nearest table circle to the drop position
                var droppedOnTable = _currentTableCoordinates.FirstOrDefault(tc =>
                    // Check if drop is within the table circle/shape's area (uses standard circle formula for simplicity)
                    (dropPosition.X - tc.CenterX) * (dropPosition.X - tc.CenterX) +
                    (dropPosition.Y - tc.CenterY) * (dropPosition.Y - tc.CenterY) <= (tc.Radius * tc.Radius) * 2 // Use 2x radius for easier targeting
                );

                if (droppedOnTable != null)
                {
                    int targetTableId = droppedOnTable.TableId;

                    var guest = await _context.Guests.FindAsync(guestId);
                    if (guest != null)
                    {
                        // 1. Check Capacity
                        int currentSeated = await _context.Guests.CountAsync(g => g.TableId == targetTableId);
                        if (currentSeated >= droppedOnTable.SeatingCapacity)
                        {
                            MessageBox.Show($"Table {droppedOnTable.TableNumber} is full ({droppedOnTable.SeatingCapacity} seats).", "Capacity Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // 2. Check MUST NOT SIT WITH constraints
                        var allPreferences = await _context.SeatingPreferences.ToListAsync();
                        var groupOfOne = new List<Guest> { guest };

                        // Calls the public method on the Service for the constraint check
                        if (_seatingService.CheckMustNotSitWithConflict(groupOfOne, targetTableId, allPreferences))
                        {
                            // Assignment is valid
                            guest.TableId = targetTableId;
                            await _context.SaveChangesAsync();

                            // 3. Refresh the visualization
                            await DrawRoomLayout(WeddingId);
                            await PopulateUnassignedGuests();
                        }
                        else
                        {
                            MessageBox.Show("Cannot seat guest: Conflict with MUST NOT SIT WITH rule.", "Constraint Violation", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
        }

        // --- ROOM LAYOUT DEFINITION ---
        private List<TableCoordinates> GetTableCoordinates(int weddingId)
        {
            // Ensure canvas dimensions are available before drawing
            if (SeatingCanvas.ActualWidth == 0 || SeatingCanvas.ActualHeight == 0)
                return new List<TableCoordinates>();

            double canvasWidth = SeatingCanvas.ActualWidth;
            double canvasHeight = SeatingCanvas.ActualHeight;
            double tableRadius = 50;
            double padding = 50;

            // Note: We use the context directly here, assuming it's available and quick.
            var tables = _context.Tables.Where(t => t.WeddingId == weddingId).OrderBy(t => t.TableNumber).ToList();
            if (!tables.Any()) return new List<TableCoordinates>();

            _currentTableCoordinates.Clear(); // Clear and rebuild the coordinate list

            // Determine the room layout based on the number of guest tables (TableNumber > 0)
            int guestTableCount = tables.Count(t => t.TableNumber > 0);

            // --- 1. Sweetheart Table (Table 0) ---
            var sweetheart = tables.FirstOrDefault(t => t.TableNumber == 0);
            if (sweetheart != null)
            {
                // Sweetheart Table is always top center (small rectangle)
                _currentTableCoordinates.Add(new TableCoordinates(
                    sweetheart.TableId, 0,
                    canvasWidth / 2, padding + tableRadius, tableRadius * 1.5, // Radius is larger for drawing size
                    sweetheart.SeatingCapacity));
            }

            // --- 2. Guest Tables (Layout based on count) ---

            int tablesPerRow = 0;
            int rowCount = 0;

            if (guestTableCount == 2) { tablesPerRow = 2; rowCount = 1; }
            else if (guestTableCount == 4) { tablesPerRow = 2; rowCount = 2; }
            else if (guestTableCount == 6) { tablesPerRow = 3; rowCount = 2; }
            else { return _currentTableCoordinates; } // Unsupported layout

            double gridAreaTop = padding + tableRadius * 3; // Start below sweetheart table
            double gridHeight = canvasHeight - gridAreaTop - padding;
            double tableSpacingX = (canvasWidth - 2 * padding) / tablesPerRow;
            double tableSpacingY = gridHeight / rowCount;

            var guestTablesList = tables.Where(t => t.TableNumber > 0).ToList();

            for (int i = 0; i < guestTablesList.Count; i++)
            {
                var table = guestTablesList[i];
                int row = i / tablesPerRow;
                int col = i % tablesPerRow;

                double centerX = padding + (col * tableSpacingX) + (tableSpacingX / 2);
                double centerY = gridAreaTop + (row * tableSpacingY) + (tableSpacingY / 2);

                _currentTableCoordinates.Add(new TableCoordinates(
                    table.TableId, table.TableNumber,
                    centerX, centerY, tableRadius,
                    table.SeatingCapacity));
            }

            return _currentTableCoordinates;
        }


        // --- VISUALIZATION DRAWING ---
        private async Task DrawRoomLayout(int weddingId)
        {
            SeatingCanvas.Children.Clear();

            // Recalculate coordinates based on current canvas size
            var tableCoords = GetTableCoordinates(weddingId);

            // Get current guest status for drawing
            var tables = await _context.Tables
                .Include(t => t.Guests.Where(g => g.WeddingId == weddingId))
                .Where(t => t.WeddingId == weddingId)
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            foreach (var coord in tableCoords)
            {
                var table = tables.FirstOrDefault(t => t.TableId == coord.TableId);
                if (table == null) continue;

                // --- Draw Table Shape (Rectangle for Table 0, Ellipse for others) ---
                Shape tableShape;
                if (table.TableNumber == 0)
                {
                    tableShape = new Rectangle
                    {
                        Width = coord.Radius * 3, // Sweetheart table is wider
                        Height = coord.Radius * 1.5,
                        Fill = Brushes.LightSteelBlue,
                        Stroke = Brushes.MidnightBlue,
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(tableShape, coord.CenterX - tableShape.Width / 2);
                    Canvas.SetTop(tableShape, coord.CenterY - tableShape.Height / 2);
                }
                else
                {
                    tableShape = new Ellipse
                    {
                        Width = coord.Radius * 2,
                        Height = coord.Radius * 2,
                        Fill = Brushes.LightGray,
                        Stroke = Brushes.Gray,
                        StrokeThickness = 2
                    };
                    Canvas.SetLeft(tableShape, coord.CenterX - coord.Radius);
                    Canvas.SetTop(tableShape, coord.CenterY - coord.Radius);
                }
                SeatingCanvas.Children.Add(tableShape);

                // --- Add Table Number/Info Text ---
                TextBlock tableText = new TextBlock
                {
                    Text = (table.TableNumber == 0 ? "Sweetheart Table" : $"Table {table.TableNumber}")
                         + $"\n({table.Guests.Count}/{table.SeatingCapacity})",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = (table.TableNumber == 0) ? 12 : 10,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(tableText, coord.CenterX - 60);
                Canvas.SetTop(tableText, coord.CenterY - 15);
                SeatingCanvas.Children.Add(tableText);

                // --- Draw Guest Seats around the table ---
                int seatedCount = table.Guests.Count;
                if (seatedCount > 0 && table.SeatingCapacity > 0)
                {
                    double currentRadius = coord.Radius;
                    double angleStep = 360.0 / table.SeatingCapacity;
                    double startAngle = 0;

                    for (int j = 0; j < seatedCount; j++)
                    {
                        var guest = table.Guests.Skip(j).First();
                        double angle = startAngle + (j * angleStep);
                        double radians = angle * (Math.PI / 180.0);

                        // Calculate position slightly outside the table shape
                        double seatX = coord.CenterX + (currentRadius + 15) * Math.Cos(radians);
                        double seatY = coord.CenterY + (currentRadius + 15) * Math.Sin(radians);

                        // Seat Rectangle 
                        Rectangle seatRect = new Rectangle
                        {
                            Width = 60,
                            Height = 30,
                            // Red if allergy, Blue otherwise
                            Fill = (guest.Allergies != null && guest.Allergies.Length > 0) ? Brushes.Red : Brushes.SteelBlue,
                            Stroke = Brushes.DarkBlue,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(seatRect, seatX - 30);
                        Canvas.SetTop(seatRect, seatY - 15);
                        SeatingCanvas.Children.Add(seatRect);

                        // Guest Name and Menu Preference Text
                        TextBlock guestText = new TextBlock
                        {
                            Text = $"{guest.GuestName.Split(' ')[0]}\n({guest.DietaryRestrictions})",
                            FontSize = 9,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            TextAlignment = TextAlignment.Center
                        };
                        Canvas.SetLeft(guestText, seatX - 30);
                        Canvas.SetTop(guestText, seatY - 12);
                        SeatingCanvas.Children.Add(guestText);
                    }
                }
            }
        }
    }
}