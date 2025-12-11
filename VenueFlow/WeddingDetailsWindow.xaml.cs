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
using Microsoft.EntityFrameworkCore;
using VenueFlow.Data.Models;
using VenueFlow.Data;
using VenueFlow.Services;


namespace VenueFlow
{
    /// <summary>
    /// Interaction logic for WeddingDetailsWindow.xaml
    /// </summary>
    public partial class WeddingDetailsWindow : Window
    {
        private int _weddingId;
        private List<Guest> _allGuests;

        public WeddingDetailsWindow(int weddingId)
        {
            InitializeComponent();
            _weddingId = weddingId;
            LoadData();
        }

        private void LoadData()
        {
            using (var context = new VenueFlowDbContext())
            {
                var wedding = context.Weddings.Find(_weddingId);
                if (wedding == null) return;

                TxtWeddingName.Text = wedding.Name;
                WeddingDatePicker.SelectedDate = wedding.Date.ToDateTime(System.TimeOnly.MinValue);

                
                var guests = context.Guests
                                    .Include(g => g.MenuOption)
                                    .Where(g => g.WeddingId == _weddingId)
                                    .ToList();
                _allGuests = guests;

                ListGuests.ItemsSource = guests;

                
                int guestCount = guests.Count;

                RoomSmall.IsEnabled = true;
                RoomMedium.IsEnabled = true;
                RoomLarge.IsEnabled = true;

                
                if (guestCount > 22) RoomSmall.IsEnabled = false;
                if (guestCount > 42) RoomMedium.IsEnabled = false;
                if (guestCount > 62) RoomLarge.IsEnabled = false;

                
                if (wedding.RoomCapacity == 22) RoomSmall.IsChecked = true;
                else if (wedding.RoomCapacity == 42) RoomMedium.IsChecked = true;
                else if (wedding.RoomCapacity == 62) RoomLarge.IsChecked = true;
                else
                {
                    if (RoomSmall.IsEnabled) RoomSmall.IsChecked = true;
                    else if (RoomMedium.IsEnabled) RoomMedium.IsChecked = true;
                    else RoomLarge.IsChecked = true;
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            using (var context = new VenueFlowDbContext())
            {
                var wedding = context.Weddings.Find(_weddingId);
                if (wedding != null)
                {
                    wedding.Name = TxtWeddingName.Text;

                    if (WeddingDatePicker.SelectedDate.HasValue)
                    {
                        wedding.Date = System.DateOnly.FromDateTime(WeddingDatePicker.SelectedDate.Value);
                    }

                    if (RoomSmall.IsChecked == true) wedding.RoomCapacity = 22;
                    if (RoomMedium.IsChecked == true) wedding.RoomCapacity = 42;
                    if (RoomLarge.IsChecked == true) wedding.RoomCapacity = 62;

                    context.SaveChanges();
                    MessageBox.Show("Changes Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (_allGuests == null) return;

            string filter = textBox.Text.ToLower();

            if (string.IsNullOrWhiteSpace(filter))
            {
                ListGuests.ItemsSource = _allGuests;
            }
            else
            {
                ListGuests.ItemsSource = _allGuests.Where(g =>
                    (g.GuestName != null && g.GuestName.ToLower().Contains(filter)) ||
                    (g.FamilyGroup != null && g.FamilyGroup.ToLower().Contains(filter))
                ).ToList();
            }
        }

        private async void BtnSeatingPlan_Click(object sender, RoutedEventArgs e)
        {
            
            using (var checkContext = new VenueFlowDbContext())
            {
                // Check if ANY guest for this wedding is already seated
                bool isWorkInProgress = await checkContext.Guests
                    .AnyAsync(g => g.WeddingId == _weddingId && g.TableId != null);

                if (!isWorkInProgress)
                {
                   
                    var wedding = await checkContext.Weddings.FindAsync(_weddingId);
                    if (wedding != null)
                    {
                        if (RoomSmall.IsChecked == true) wedding.RoomCapacity = 22;
                        else if (RoomMedium.IsChecked == true) wedding.RoomCapacity = 42;
                        else if (RoomLarge.IsChecked == true) wedding.RoomCapacity = 62;
                        await checkContext.SaveChangesAsync();
                    }

                 
                    var seatingService = new SeatingPlannerService(checkContext);
                    await seatingService.AutoSeatGuests(_weddingId);
                }
            }

            
            var context = new VenueFlowDbContext();
            var serviceForWindow = new SeatingPlannerService(context);

            var seatingWindow = new SeatingPlanWindow(context, serviceForWindow, _weddingId)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            seatingWindow.ShowDialog();

            LoadData();
        }
    }
}
