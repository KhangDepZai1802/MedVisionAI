using System.IO;
using System.Windows;
using MedVisionAI.Models;

// Dùng alias rõ ràng để tránh xung đột System.Windows.Forms vs Microsoft.Win32
using WinOpenFile = Microsoft.Win32.OpenFileDialog;

namespace MedVisionAI.UI.Windows
{
    public partial class ModelSelectDialog : Window
    {
        private static readonly string ModelFilter =
            "Model AI|*.onnx;*.pt;*.pth;*.h5;*.pb|" +
            "ONNX (*.onnx)|*.onnx|" +
            "PyTorch (*.pt;*.pth)|*.pt;*.pth|" +
            "TensorFlow/Keras (*.h5)|*.h5|" +
            "Tất cả (*.*)|*.*";

        public ModelConfig SelectedConfig { get; private set; } = new();

        public ModelSelectDialog() => InitializeComponent();

        private void BrowseSegmentation_Click(object sender, RoutedEventArgs e)
        {
            var path = Browse("Chọn Model Phân đoạn NST");
            if (path == null) return;
            SelectedConfig.SegmentationModelPath = path;
            PathSegmentation.Text       = Path.GetFileName(path);
            PathSegmentation.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }

        private void BrowseOverlap_Click(object sender, RoutedEventArgs e)
        {
            var path = Browse("Chọn Model Phân tách NST chồng lấn");
            if (path == null) return;
            SelectedConfig.OverlapModelPath = path;
            PathOverlap.Text       = Path.GetFileName(path);
            PathOverlap.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }

        private void BrowseAnomaly_Click(object sender, RoutedEventArgs e)
        {
            var path = Browse("Chọn Model Xác định bất thường NST");
            if (path == null) return;
            SelectedConfig.AnomalyModelPath = path;
            PathAnomaly.Text       = Path.GetFileName(path);
            PathAnomaly.Foreground = System.Windows.Media.Brushes.DarkGreen;
        }

        private static string? Browse(string title)
        {
            var dlg = new WinOpenFile { Title = title, Filter = ModelFilter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedConfig.SegmentationModelPath))
            {
                System.Windows.MessageBox.Show(
                    "Vui lòng chọn ít nhất Model Phân đoạn NST (bắt buộc).\n\n" +
                    "Nếu chưa có model, nhấn 'Bỏ qua' để xem giao diện.",
                    "MedVision AI",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        private void BtnSkip_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
