using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VenueFlow.Services;

namespace VenueFlow
{
    /// <summary>
    /// Interaction logic for SeatingPlanWindow.xaml
    /// </summary>
    public partial class SeatingPlanWindow : Window
    {
        public SeatingPlanWindow()
        {
            private readonly VenueFlowDbContext _context;
        private readonly SeatingPlannerService _seatingService;
        private const int WeddingId = 1; // Assuming a single active wedding for MVP

        // Used to track the start point of a drag operation
        private Point _startPoint;

        public SeatingPlanWindow(VenueFlowDbContext context, SeatingPlannerService seatingService)
        {
            InitializeComponent();
            _context = context;
            _seatingService = seatingService;

            // Load and draw the plan when the window opens
            Loaded += SeatingPlanWindow_Loaded;
        }

        private async void SeatingPlanWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Initial draw upon opening
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
            // Get the current mouse position
            Point mousePos = e.GetPosition(null);
            Vector diff = _startPoint - mousePos;

            if (e.LeftButton == MouseButtonState.Pressed &&
                (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                 Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                // Get the ListBoxItem being dragged
                ListBox listBox = sender as ListBox;
                if (listBox.SelectedItem == null) return;

                string dragData = (string)listBox.SelectedItem;

                // Start the drag-and-drop operation
                DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);
            }
        }

        // --- DROP TARGET LOGIC ---
        private async void SeatingCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string data = (string)e.Data.GetData(DataFormats.StringFormat);

                // Extract Guest ID from the drag data string: [ID:123]
                var idString = data.Split(new[] { "[ID:", "]" }, StringSplitOptions.None)
                                   .Skip(1).FirstOrDefault();
                if (!int.TryParse(idString, out int guestId)) return;

                // Get the drop position on the canvas
                Point dropPosition = e.GetPosition(SeatingCanvas);

                // Find the nearest table circle to the drop position (Simplified)
                // In a real app, you would iterate through known table coordinates.
                // For MVP, we'll just check if it landed near *any* table area

                // **TODO: Implement logic to find the specific TableID where the user dropped the guest**

                int targetTableId = 1; // HARDCODED for testing - REPLACE THIS

                // 1. Update the database (similar to the service logic)
                var guest = await _context.Guests.FindAsync(guestId);
                if (guest != null)
                {
                    // **TODO: Add constraint check (Must Not Sit With) here before assignment**

                    guest.TableId = targetTableId;
                    await _context.SaveChangesAsync();

                    // 2. Refresh the visualization
                    await DrawRoomLayout(WeddingId);
                    await PopulateUnassignedGuests();
                }
            }
        }

        // --- VISUALIZATION METHOD (Copied from previous step) ---
        private async Task DrawRoomLayout(int weddingId)
        {
            // ... [Drawing logic remains the same as previously generated] ...
            SeatingCanvas.Children.Clear(); // Clear old drawings

            // 1. Get Tables and their currently seated Guests
            var tables = await _context.Tables
                .Include(t => t.Guests.Where(g => g.WeddingId == weddingId))
                .Where(t => t.WeddingId == weddingId)
                .OrderBy(t => t.TableNumber)
                .ToListAsync();

            if (!tables.Any()) return;

            // Define drawing parameters
            double canvasWidth = SeatingCanvas.ActualWidth;
            double canvasHeight = SeatingCanvas.ActualHeight;
            double tableRadius = 50;
            double padding = 20;

            // Simple grid layout calculation for tables (e.g., 3 tables per row)
            int tablesPerRow = 3;
            int rowCount = (int)Math.Ceiling((double)tables.Count / tablesPerRow);

            double tableSpacingX = (canvasWidth - 2 * padding) / tablesPerRow;
            double tableSpacingY = (canvasHeight - 2 * padding) / rowCount;

            for (int i = 0; i < tables.Count; i++)
            {
                var table = tables[i];
                int row = i / tablesPerRow;
                int col = i % tablesPerRow;

                // Calculate center coordinates
                double centerX = padding + (col * tableSpacingX) + (tableSpacingX / 2);
                double centerY = padding + (row * tableSpacingY) + (tableSpacingY / 2);

                // --- Draw Table Circle ---
                Ellipse tableShape = new Ellipse
                {
                    Width = tableRadius * 2,
                    Height = tableRadius * 2,
                    Fill = Brushes.LightGray,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 2
                };

                // Position the circle on the Canvas
                Canvas.SetLeft(tableShape, centerX - tableRadius);
                Canvas.SetTop(tableShape, centerY - tableRadius);
                SeatingCanvas.Children.Add(tableShape);

                // --- Add Table Number Text ---
                TextBlock tableText = new TextBlock
                {
                    Text = $"Table {table.TableNumber}\n({table.Guests.Count}/{table.SeatingCapacity})",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(tableText, centerX - 40);
                Canvas.SetTop(tableText, centerY - 15);
                SeatingCanvas.Children.Add(tableText);

                // --- Draw Guest Seats around the table ---
                int seatedCount = table.Guests.Count;
                if (seatedCount > 0)
                {
                    // Angle calculation for evenly spaced seats
                    double angleStep = 360.0 / table.SeatingCapacity;
                    double startAngle = 0;

                    for (int j = 0; j < seatedCount; j++)
                    {
                        var guest = table.Guests.Skip(j).First(); // Get the j-th seated guest
                        double angle = startAngle + (j * angleStep);
                        double radians = angle * (Math.PI / 180.0);

                        // Calculate position slightly outside the table radius
                        double seatX = centerX + (tableRadius + 15) * Math.Cos(radians);
                        double seatY = centerY + (tableRadius + 15) * Math.Sin(radians);

                        // Seat Rectangle (the "rectangle in each spot")
                        Rectangle seatRect = new Rectangle
                        {
                            Width = 60,
                            Height = 30,
                            Fill = (guest.Allergies != null && guest.Allergies.Length > 0) ? Brushes.Red : Brushes.SteelBlue, // Highlight based on allergies
                            Stroke = Brushes.DarkBlue,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(seatRect, seatX - 30); // Center the rectangle
                        Canvas.SetTop(seatRect, seatY - 15);
                        SeatingCanvas.Children.Add(seatRect);

                        // Guest Name and Menu Preference Text
                        TextBlock guestText = new TextBlock
                        {
                            // Display Name and a short menu/dietary marker
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
}
