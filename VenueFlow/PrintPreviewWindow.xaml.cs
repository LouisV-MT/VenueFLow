using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace VenueFlow
{
    /// <summary>
    /// Interaction logic for Window1.xaml
    /// </summary>
    public partial class PrintPreviewWindow : Window
    {
        private BitmapSource _imageToPrint;

        public PrintPreviewWindow(BitmapSource imageToPrint)
        {
            InitializeComponent();
            _imageToPrint = imageToPrint;
            PreviewImage.Source = _imageToPrint;
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            PrintDialog printDialog = new PrintDialog();

            if (printDialog.ShowDialog() == true)
            {
                
                FixedDocument document = new FixedDocument();
                document.DocumentPaginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
                
                FixedPage page = new FixedPage();
                page.Width = document.DocumentPaginator.PageSize.Width;
                page.Height = document.DocumentPaginator.PageSize.Height;
                
                Image printImage = new Image();
                printImage.Source = _imageToPrint;

                double scaleX = page.Width / _imageToPrint.Width;
                double scaleY = page.Height / _imageToPrint.Height;
                double scale = System.Math.Min(scaleX, scaleY);

                printImage.Width = _imageToPrint.Width * scale;
                printImage.Height = _imageToPrint.Height * scale;

                double left = (page.Width - printImage.Width) / 2;
                double top = (page.Height - printImage.Height) / 2;

                FixedPage.SetLeft(printImage, left);
                FixedPage.SetTop(printImage, top);

                page.Children.Add(printImage);

                PageContent content = new PageContent();
                ((IAddChild)content).AddChild(page);
                document.Pages.Add(content);

                printDialog.PrintDocument(document.DocumentPaginator, "Seating Plan");
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
