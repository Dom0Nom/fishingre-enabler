using System;
using System.Windows;
using System.Windows.Input;
using MinecraftMonitor.ViewModels;

namespace MinecraftMonitor.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            try
            {
                InitializeComponent();
                DataContext = new MainViewModel();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize window:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
