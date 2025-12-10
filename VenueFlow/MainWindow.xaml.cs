using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input; 
using Microsoft.Win32;
using VenueFlow.Data;
using VenueFlow.Data.Models;

namespace VenueFlow
{
    public partial class MainWindow : Window
    {
        public class WeddingUiItem
        {
            public Wedding Wedding { get; set; }
            public bool IsSelected { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
            LoadWeddings();
        }

        private void LoadWeddings()
        {
            using (var context = new VenueFlowDbContext())
            {
                var dbList = context.Weddings.OrderByDescending(w => w.Date).ToList();

                
                var uiList = dbList.Select(w => new WeddingUiItem { Wedding = w, IsSelected = false }).ToList();

                WeddingsList.ItemsSource = uiList;
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files|*.xlsx;*.xls",
                Title = "Select Guest List"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    
                    string ext = System.IO.Path.GetExtension(openFileDialog.FileName).ToLower();
                    if (ext != ".xlsx" && ext != ".xls")
                    {
                        MessageBox.Show("Please select a valid Excel file (.xlsx).", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    
                    var service = new ImportService();
                    service.ImportWedding(openFileDialog.FileName);

                    
                    LoadWeddings();
                    MessageBox.Show("Wedding created successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.IO.IOException)
                {
                    MessageBox.Show("The file is currently open in Excel.\nPlease close it and try again.",
                        "File Locked", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show($"Failed to import wedding.\nError: {ex.Message}",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            
            var allItems = WeddingsList.ItemsSource as List<WeddingUiItem>;
            var itemsToDelete = allItems?.Where(i => i.IsSelected).ToList();

            if (itemsToDelete == null || itemsToDelete.Count == 0)
            {
                MessageBox.Show("Please check at least one wedding to delete.", "Nothing Selected");
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete {itemsToDelete.Count} wedding(s)?\nThis will permanently delete all guests for these events.",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var context = new VenueFlowDbContext())
                {
                    foreach (var item in itemsToDelete)
                    {
                        int targetId = item.Wedding.WeddingId;

                        var guestsToDelete = context.Guests
                                                    .Where(g => g.WeddingId == targetId)
                                                    .ToList();
                        context.Guests.RemoveRange(guestsToDelete);

                        var tablesToDelete = context.Tables
                                                    .Where(t => t.WeddingId == targetId)
                                                    .ToList();
                        context.Tables.RemoveRange(tablesToDelete);

                        var weddingToDelete = context.Weddings.Find(targetId);
                        if (weddingToDelete != null)
                        {
                            context.Weddings.Remove(weddingToDelete);
                        }
                    }

                    context.SaveChanges();
                }
                LoadWeddings(); 
            }
        }


        private void WeddingItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is WeddingUiItem selectedItem)
            {
                var detailsWindow = new WeddingDetailsWindow(selectedItem.Wedding.WeddingId)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                // Open details as modal over MainWindow; closing details returns to MainWindow
                detailsWindow.ShowDialog();

                // Refresh the list after the details window closes so the main list reflects any edits (date, name, capacity, etc.)
                LoadWeddings();

                // Do not close the main window here so closing details returns to it
            }
        }
    }
}