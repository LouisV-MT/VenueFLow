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

// Record definition with helper Radius property
public record TableCoordinates(int TableId, int TableNumber, double CenterX, double CenterY, double Width, double Height, int SeatingCapacity)
{
    public double Radius => Width / 2;
}

namespace VenueFlow
{
    public partial class SeatingPlanWindow : Window
    {
        private readonly VenueFlowDbContext _context;
        private readonly SeatingPlannerService _seatingService;
        private readonly int _weddingId;

        // --- LAYOUT CONSTANTS (Defined here to be available everywhere) ---
        private const double CellWidth = 350;
        private const double CellHeight = 350;
        private const double SweetheartAreaHeight = 250;
        private const double GuestTableRadius = 90;
        private const double SweetheartWidth = 160;
        private const double SweetheartHeight = 80;
        private const double Padding = 60;

        private Point _startPoint;
        private List<TableCoordinates> _currentTableCoordinates = new List<TableCoordinates>();

        // In-memory seat assignment: tableId -> slotIndex -> guestId
        // This preserves seat positions for the life of the window and prevents reflowing
        private readonly Dictionary<int, Dictionary<int, int>> _tableSeatAssignments = new();

        // Nullable to fix compiler warning
        private object? _draggedItemData;

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
            await EnsureTablesExistAsync();
            // Note: Algorithm is NOT run here to prevent overwriting manual changes on reload.
            await DrawRoomLayoutIsolated();
            await PopulateUnassignedGuestsIsolated();
        }

        // --- DATA LOGIC ---

        private async Task EnsureTablesExistAsync()
        {
            using (var isolatedContext = new VenueFlowDbContext())
            {
                var wedding = await isolatedContext.Weddings.FindAsync(_weddingId);
                if (wedding == null) return;

                // 1. Determine Target Table Count
                int targetGuestTables;
                if (wedding.RoomCapacity <= 22) targetGuestTables = 2;
                else if (wedding.RoomCapacity <= 42) targetGuestTables = 4;
                else targetGuestTables = 6;

                // 2. Check current state
                var existingGuestTables = await isolatedContext.Tables
                    .Where(t => t.WeddingId == _weddingId && t.TableNumber > 0)
                    .OrderBy(t => t.TableNumber)
                    .ToListAsync();

                bool sweetheartExists = await isolatedContext.Tables.AnyAsync(t => t.WeddingId == _weddingId && t.TableNumber == 0);

                bool changesMade = false;

                // 3. Add Sweetheart if missing
                if (!sweetheartExists)
                {
                    isolatedContext.Tables.Add(new Table { WeddingId = _weddingId, TableNumber = 0, SeatingCapacity = 2 });
                    changesMade = true;
                }

                // 4. Sync Guest Tables
                if (existingGuestTables.Count < targetGuestTables)
                {
                    // Add missing tables
                    for (int i = existingGuestTables.Count + 1; i <= targetGuestTables; i++)
                    {
                        isolatedContext.Tables.Add(new Table { WeddingId = _weddingId, TableNumber = i, SeatingCapacity = 10 });
                    }
                    changesMade = true;
                }
                else if (existingGuestTables.Count > targetGuestTables)
                {
                    // Remove extra tables
                    var tablesToRemove = existingGuestTables
                        .Where(t => t.TableNumber > targetGuestTables)
                        .ToList();

                    foreach (var t in tablesToRemove)
                    {
                        // Clear guests from table before deleting table
                        var guestsOnTable = await isolatedContext.Guests.Where(g => g.TableId == t.TableId).ToListAsync();
                        guestsOnTable.ForEach(g => g.TableId = null);
                        isolatedContext.Tables.Remove(t);
                    }
                    changesMade = true;
                }

                if (changesMade) await isolatedContext.SaveChangesAsync();
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

                DrawRoomLayoutInternal(tables);
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

        // --- DRAG SOURCE LOGIC ---

        private void UnassignedGuestsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);

            // Fix: Find the ListBoxItem immediately to allow drag without prior selection
            var potentialSource = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(potentialSource);

            if (listBoxItem != null)
            {
                _draggedItemData = listBoxItem.Content;
            }
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

                if (_draggedItemData != null)
                {
                    string dragData = (string)_draggedItemData;
                    DragDrop.DoDragDrop(listBox, dragData, DragDropEffects.Move);
                    _draggedItemData = null;
                }
            }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T) return (T)current;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // --- DROP TARGET & INTERACTION ---

        private async void Placemat_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Rectangle placemat && placemat.Tag is int guestId)
            {
                // Unseat the guest (use isolated context)
                using (var isolatedContext = new VenueFlowDbContext())
                {
                    var guest = await isolatedContext.Guests.FindAsync(guestId);
                    if (guest != null)
                    {
                        // Remove in-memory assignment for this guest (keeps other assignments unchanged)
                        RemoveGuestFromInMemoryAssignments(guestId);

                        guest.TableId = null;
                        await isolatedContext.SaveChangesAsync();
                    }
                }
                await DrawRoomLayoutIsolated();
                await PopulateUnassignedGuestsIsolated();
            }
        }

        private async void SeatingCanvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
            {
                string data = (string)e.Data.GetData(DataFormats.StringFormat);
                var idString = data.Split(new[] { "[ID:", "]" }, StringSplitOptions.None)
                                   .Skip(1).FirstOrDefault();
                if (!int.TryParse(idString, out int guestId)) return;

                Point dropPosition = e.GetPosition(SeatingCanvas);

                // Find nearest table (within an expanded radius) and the nearest slot on that table
                var tableCoord = _currentTableCoordinates
                    .OrderBy(tc => DistanceSquared(new Point(tc.CenterX, tc.CenterY), dropPosition))
                    .FirstOrDefault();

                if (tableCoord == null) return;

                // Build seat positions for that table
                var seatPositions = ComputeSeatPositions(tableCoord, tableCoord.SeatingCapacity);

                // Find nearest slot index (within reasonable threshold)
                int nearestIndex = -1;
                double nearestDistSq = double.MaxValue;
                for (int i = 0; i < seatPositions.Count; i++)
                {
                    double dsq = DistanceSquared(seatPositions[i], dropPosition);
                    if (dsq < nearestDistSq)
                    {
                        nearestDistSq = dsq;
                        nearestIndex = i;
                    }
                }

                // Accept only if reasonably close to seat (threshold ~ 80px squared)
                const double maxAcceptDistanceSq = 80 * 80;
                if (nearestIndex < 0 || nearestDistSq > maxAcceptDistanceSq)
                {
                    // If not near any seat, reject the drop
                    return;
                }

                var targetTableId = tableCoord.TableId;
                var targetSeatIndex = nearestIndex;

                // Use shared context for this operation to keep it simple
                var guest = await _context.Guests.FindAsync(guestId);

                if (guest != null)
                {
                    // If the guest is already assigned to the target table at same seat, nothing to do
                    if (guest.TableId == targetTableId)
                    {
                        // Update in-memory mapping if necessary
                        AssignGuestToSeatInMemory(guestId, targetTableId, targetSeatIndex);
                        await DrawRoomLayoutIsolated();
                        return;
                    }

                    // Check capacity: do not allow more occupied seats than capacity
                    var occupancy = GetCurrentInMemoryOccupancyForTable(targetTableId);
                    if (occupancy >= tableCoord.SeatingCapacity && !(guest.TableId == targetTableId))
                    {
                        MessageBox.Show("Table is full.", "Capacity", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Check constraints (get preferences and current guest state)
                    var allPreferences = await _context.SeatingPreferences.ToListAsync();
                    var allGuestsCurrentState = await _context.Guests.Where(g => g.WeddingId == _weddingId).ToListAsync();
                    var groupOfOne = new List<Guest> { guest };

                    if (_seatingService.CheckMustNotSitWithConflict(groupOfOne, targetTableId, allPreferences, allGuestsCurrentState))
                    {
                        // Assign in DB
                        // Remove from any previous in-memory slot
                        RemoveGuestFromInMemoryAssignments(guestId);

                        guest.TableId = targetTableId;
                        await _context.SaveChangesAsync();

                        // Update in-memory assignment to desired seat (reserving it)
                        AssignGuestToSeatInMemory(guestId, targetTableId, targetSeatIndex);

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

        // --- DRAWING LOGIC & SEAT ASSIGNMENT HELPERS ---

        // Ensure assignments exist and stable for this session; populate new guests into free slots deterministically
        private void EnsureSeatAssignments(List<Table> tables)
        {
            foreach (var table in tables)
            {
                if (!_tableSeatAssignments.ContainsKey(table.TableId))
                {
                    _tableSeatAssignments[table.TableId] = new Dictionary<int, int>();
                }

                var mapping = _tableSeatAssignments[table.TableId];

                // Remove assignments for guests that no longer exist at this table
                var guestIdsOnTable = table.Guests.Select(g => g.GuestId).ToHashSet();
                var assignedGuestIds = mapping.Values.ToList();
                foreach (var gid in assignedGuestIds)
                {
                    if (!guestIdsOnTable.Contains(gid))
                    {
                        // remove stale assignment
                        var key = mapping.FirstOrDefault(kvp => kvp.Value == gid).Key;
                        if (mapping.ContainsKey(key)) mapping.Remove(key);
                    }
                }

                // Fill free slots for present guests that don't yet have an assignment
                var capacity = Math.Max(1, table.SeatingCapacity);
                var freeSlots = Enumerable.Range(0, capacity).Where(i => !mapping.ContainsKey(i)).ToList();

                // Determine guests that still need assignment
                var unassignedGuests = table.Guests
                    .Where(g => !mapping.Values.Contains(g.GuestId))
                    .OrderBy(g => g.GuestId) // stable order
                    .ToList();

                foreach (var g in unassignedGuests)
                {
                    if (freeSlots.Count == 0) break;
                    // deterministically pick initial slot candidate
                    int desired = g.GuestId % capacity;
                    int slot = -1;
                    // linear probe from desired to find available slot
                    for (int offset = 0; offset < capacity; offset++)
                    {
                        int idx = (desired + offset) % capacity;
                        if (!mapping.ContainsKey(idx))
                        {
                            slot = idx;
                            break;
                        }
                    }
                    if (slot == -1)
                    {
                        slot = freeSlots[0];
                    }

                    mapping[slot] = g.GuestId;
                    freeSlots.Remove(slot);
                }
            }
        }

        // Assign guest to a particular seat in-memory (removing previous assignment)
        private void AssignGuestToSeatInMemory(int guestId, int tableId, int seatIndex)
        {
            // Remove guest from any existing mapping
            RemoveGuestFromInMemoryAssignments(guestId);

            if (!_tableSeatAssignments.ContainsKey(tableId))
            {
                _tableSeatAssignments[tableId] = new Dictionary<int, int>();
            }

            // If desired slot is occupied, bump the occupant to another free slot (find first free)
            var mapping = _tableSeatAssignments[tableId];
            if (mapping.TryGetValue(seatIndex, out int occupant))
            {
                // find any free slot
                int capacityGuess = Math.Max(1, mapping.Count + 1);
                int newSlot = -1;
                for (int i = 0; i < Math.Max(20, capacityGuess + 20); i++) // safe loop
                {
                    if (!mapping.ContainsKey(i))
                    {
                        newSlot = i;
                        break;
                    }
                }
                if (newSlot >= 0)
                {
                    mapping[newSlot] = occupant;
                }
                else
                {
                    // fallback: overwrite (rare)
                    mapping[seatIndex] = guestId;
                    return;
                }
            }

            mapping[seatIndex] = guestId;
        }

        // Remove guest from any in-memory seat mapping
        private void RemoveGuestFromInMemoryAssignments(int guestId)
        {
            foreach (var kvp in _tableSeatAssignments.ToList())
            {
                var tableId = kvp.Key;
                var mapping = kvp.Value;
                var entry = mapping.FirstOrDefault(x => x.Value == guestId);
                if (!entry.Equals(default(KeyValuePair<int, int>)))
                {
                    mapping.Remove(entry.Key);
                }
            }
        }

        private int GetCurrentInMemoryOccupancyForTable(int tableId)
        {
            if (!_tableSeatAssignments.ContainsKey(tableId)) return 0;
            return _tableSeatAssignments[tableId].Count;
        }

        private List<Point> ComputeSeatPositions(TableCoordinates coord, int capacity)
        {
            var positions = new List<Point>();

            if (coord.TableNumber == 0)
            {
                // linear slots above the sweetheart table
                double spacing = 70;
                double totalWidth = spacing * (capacity - 1);
                double startX = coord.CenterX - (totalWidth / 2);
                double y = coord.CenterY - (coord.Height / 2) - 25;
                for (int i = 0; i < capacity; i++)
                {
                    positions.Add(new Point(startX + (i * spacing), y));
                }
            }
            else
            {
                double layoutRadius = coord.Radius + 35;
                double angleStep = 360.0 / Math.Max(1, capacity);
                double startAngle = -90;
                for (int i = 0; i < capacity; i++)
                {
                    double angle = startAngle + (i * angleStep);
                    double radians = angle * (Math.PI / 180.0);
                    double x = coord.CenterX + layoutRadius * Math.Cos(radians);
                    double y = coord.CenterY + layoutRadius * Math.Sin(radians);
                    positions.Add(new Point(x, y));
                }
            }

            return positions;
        }

        private static double DistanceSquared(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private List<TableCoordinates> GetTableCoordinates(List<Table> tables)
        {
            if (!tables.Any()) return new List<TableCoordinates>();

            _currentTableCoordinates.Clear();

            int guestTableCount = tables.Count(t => t.TableNumber > 0);

            // Calculate grid dimensions
            int tablesPerRow = 2;
            if (guestTableCount >= 5) tablesPerRow = 3;

            int rowCount = (int)Math.Ceiling((double)guestTableCount / tablesPerRow);

            // DYNAMIC CANVAS SIZE
            double totalCanvasWidth = Math.Max(1000, (tablesPerRow * CellWidth) + 100);
            double totalCanvasHeight = SweetheartAreaHeight + (rowCount * CellHeight) + 100;

            SeatingCanvas.Width = totalCanvasWidth;
            SeatingCanvas.Height = totalCanvasHeight;

            // 1. Sweetheart
            var sweetheart = tables.FirstOrDefault(t => t.TableNumber == 0);
            if (sweetheart != null)
            {
                _currentTableCoordinates.Add(new TableCoordinates(
                    sweetheart.TableId, 0,
                    totalCanvasWidth / 2, 100, // Fixed top offset
                    SweetheartWidth, SweetheartHeight,
                    sweetheart.SeatingCapacity));
            }

            // 2. Guest Tables
            var guestTablesList = tables.Where(t => t.TableNumber > 0).OrderBy(t => t.TableNumber).ToList();
            double gridStartX = (totalCanvasWidth - (tablesPerRow * CellWidth)) / 2;

            for (int i = 0; i < guestTablesList.Count; i++)
            {
                var table = guestTablesList[i];
                int row = i / tablesPerRow;
                int col = i % tablesPerRow;

                double centerX = gridStartX + (col * CellWidth) + (CellWidth / 2);
                double centerY = SweetheartAreaHeight + (row * CellHeight) + (CellHeight / 2);

                _currentTableCoordinates.Add(new TableCoordinates(
                    table.TableId, table.TableNumber,
                    centerX, centerY,
                    GuestTableRadius * 2, GuestTableRadius * 2,
                    table.SeatingCapacity));
            }

            return _currentTableCoordinates;
        }

        private void DrawRoomLayoutInternal(List<Table> tables)
        {
            SeatingCanvas.Children.Clear();

            // Prepare stable in-memory seat assignments before drawing
            EnsureSeatAssignments(tables);

            var tableCoords = GetTableCoordinates(tables);

            if (tableCoords.Count == 0)
            {
                SeatingCanvas.Children.Add(new TextBlock { Text = "No tables found.", FontSize = 16, Foreground = Brushes.Red, Margin = new Thickness(20) });
                return;
            }

            foreach (var coord in tableCoords)
            {
                var table = tables.FirstOrDefault(t => t.TableId == coord.TableId);
                if (table == null) continue;

                // Draw Table Shape
                Shape tableShape;
                if (table.TableNumber == 0)
                {
                    tableShape = new Rectangle
                    {
                        Width = coord.Width,
                        Height = coord.Height,
                        Fill = Brushes.Lavender,
                        Stroke = Brushes.Indigo,
                        StrokeThickness = 2,
                        RadiusX = 10,
                        RadiusY = 10
                    };
                    Canvas.SetLeft(tableShape, coord.CenterX - (coord.Width / 2));
                    Canvas.SetTop(tableShape, coord.CenterY - (coord.Height / 2));
                }
                else
                {
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

                // Compute seat positions (one per capacity slot)
                var seatPositions = ComputeSeatPositions(coord, Math.Max(1, table.SeatingCapacity));

                // Prepare mapping for quick lookup
                var mapping = _tableSeatAssignments.ContainsKey(table.TableId)
                    ? _tableSeatAssignments[table.TableId]
                    : new Dictionary<int, int>();

                // Draw all slots (occupied or empty) so empty slots remain visible and stable
                for (int slotIndex = 0; slotIndex < seatPositions.Count; slotIndex++)
                {
                    Point seatPoint = seatPositions[slotIndex];

                    if (mapping.TryGetValue(slotIndex, out int guestId))
                    {
                        // Occupied slot: find guest object
                        var guest = table.Guests.FirstOrDefault(g => g.GuestId == guestId);
                        if (guest == null)
                        {
                            // Stale mapping: remove and draw empty
                            mapping.Remove(slotIndex);
                            DrawEmptyPlacemat(seatPoint);
                            continue;
                        }

                        // Placemat with guest
                        Rectangle placemat = new Rectangle
                        {
                            Width = 60,
                            Height = 30,
                            Fill = (!string.IsNullOrEmpty(guest.Allergies) && guest.Allergies != "None") ? Brushes.LightPink : Brushes.LightBlue,
                            Stroke = Brushes.Black,
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2,
                            Cursor = Cursors.Hand,
                            Tag = guest.GuestId,
                            ToolTip = "Right-Click to Unseat"
                        };

                        double rotationAngle = coord.TableNumber == 0 ? 0 : ( -90 + (slotIndex * (360.0 / Math.Max(1, table.SeatingCapacity))) ) + 90;
                        RotateTransform rotate = new RotateTransform(rotationAngle);
                        placemat.RenderTransformOrigin = new Point(0.5, 0.5);
                        placemat.RenderTransform = rotate;
                        placemat.MouseRightButtonUp += Placemat_MouseRightButtonUp;

                        Canvas.SetLeft(placemat, seatPoint.X - 30);
                        Canvas.SetTop(placemat, seatPoint.Y - 15);
                        SeatingCanvas.Children.Add(placemat);

                        // Text (Name + Optional Diet)
                        string dietText = (string.IsNullOrEmpty(guest.DietaryRestrictions) || guest.DietaryRestrictions == "None") ? "" : $"\n({guest.DietaryRestrictions})";
                        TextBlock nameText = new TextBlock
                        {
                            Text = $"{guest.GuestName.Split(' ')[0]}{dietText}",
                            FontSize = 9,
                            Foreground = Brushes.Black,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                            Width = 60,
                            Height = 30,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            IsHitTestVisible = false,
                            RenderTransformOrigin = new Point(0.5, 0.5),
                            RenderTransform = rotate
                        };

                        Canvas.SetLeft(nameText, seatPoint.X - 30);
                        Canvas.SetTop(nameText, seatPoint.Y - 15);
                        SeatingCanvas.Children.Add(nameText);
                    }
                    else
                    {
                        // Empty slot
                        DrawEmptyPlacemat(seatPoint);
                    }
                }
            }
        }

        private void DrawEmptyPlacemat(Point seatPoint)
        {
            Rectangle emptyPlacemat = new Rectangle
            {
                Width = 60,
                Height = 30,
                Fill = Brushes.LightGray,
                Stroke = Brushes.DarkGray,
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
                Opacity = 0.45,
                ToolTip = "Drop guest here"
            };
            Canvas.SetLeft(emptyPlacemat, seatPoint.X - 30);
            Canvas.SetTop(emptyPlacemat, seatPoint.Y - 15);
            SeatingCanvas.Children.Add(emptyPlacemat);
        }
    }
}