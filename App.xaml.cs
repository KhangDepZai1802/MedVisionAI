using System.Windows;

// Alias rõ ràng để tránh xung đột System.Windows.Forms.Application
using WpfApp = System.Windows.Application;

namespace MedVisionAI
{
    public partial class App : WpfApp
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, ex) =>
            {
                System.Windows.MessageBox.Show(
                    $"Lỗi không mong đợi:\n{ex.Exception.Message}",
                    "MedVision AI",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
