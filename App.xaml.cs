using System.Windows;
using Application = System.Windows.Application;

namespace MedVisionAI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Global exception handler
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show($"Lỗi không mong đợi: {ex.Exception.Message}",
                    "MedVision AI", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
