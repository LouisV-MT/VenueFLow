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

namespace VenueFlow
{
    /// <summary>
    /// Interaction logic for WeddingDetailsWindow.xaml
    /// </summary>
    public partial class WeddingDetailsWindow : Window
    {
        private int _weddingId;

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

        private void BtnSeatingPlan_Click(object sender, RoutedEventArgs e)
        {
        }
    }
}
