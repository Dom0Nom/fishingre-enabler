using System;
using System.Windows;

namespace AetherLinkMonitor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Console.WriteLine("Application starting...");

            try
            {
                base.OnStartup(e);
                Console.WriteLine("Base startup complete");

                AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                {
                    Console.WriteLine($"Fatal error: {ex.ExceptionObject}");
                    MessageBox.Show(
                        $"Fatal error: {ex.ExceptionObject}",
                        "Application Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                };

                DispatcherUnhandledException += (s, ex) =>
                {
                    Console.WriteLine($"Dispatcher error: {ex.Exception.Message}");
                    MessageBox.Show(
                        $"Error: {ex.Exception.Message}\n\nStack Trace:\n{ex.Exception.StackTrace}",
                        "Application Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    ex.Handled = true;
                };

                Console.WriteLine("Event handlers registered");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Startup error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                MessageBox.Show(
                    $"Startup failed: {ex.Message}\n\n{ex.StackTrace}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                throw;
            }
        }
    }
}
