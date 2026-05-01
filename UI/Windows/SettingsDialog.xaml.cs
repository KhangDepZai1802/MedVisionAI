using System.IO;
using System.Windows;
using MedVisionAI.Models;

using WinOpenFile = Microsoft.Win32.OpenFileDialog;

namespace MedVisionAI.UI.Windows
{
    public partial class SettingsDialog : Window
    {
        private ModelConfig _config;
        public ModelConfig UpdatedConfig => _config;

        private static readonly string ModelFilter =
            "Model AI|*.onnx;*.pt;*.pth;*.h5;*.pb|" +
            "ONNX (*.onnx)|*.onnx|PyTorch (*.pt;*.pth)|*.pt;*.pth|" +
            "TensorFlow/Keras (*.h5)|*.h5|Tất cả (*.*)|*.*";

        public SettingsDialog(ModelConfig current)
        {
            InitializeComponent();
            _config = current.Clone();
            NormalCount.Text = _config.NormalChromosomeCount.ToString();
            Tolerance.Text   = _config.Tolerance.ToString();
            RefreshPathLabels();
        }

        private void RefreshPathLabels()
        {
            SetPathLabel(PathSeg,     _config.SegmentationModelPath);
            SetPathLabel(PathOverlap, _config.OverlapModelPath);
            SetPathLabel(PathAnomaly, _config.AnomalyModelPath);
        }

        private static void SetPathLabel(System.Windows.Controls.TextBlock tb, string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                tb.Text       = "Chưa chọn file model";
                tb.Foreground = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                tb.Text       = Path.GetFileName(path);
                tb.Foreground = System.Windows.Media.Brushes.DarkGreen;
                tb.ToolTip    = path;
            }
        }

        private void BrowseSeg_Click(object sender, RoutedEventArgs e)
        {
            var p = Browse("Chọn Model Phân đoạn NST");
            if (p == null) return;
            _config.SegmentationModelPath = p;
            SetPathLabel(PathSeg, p);
        }

        private void BrowseOverlap_Click(object sender, RoutedEventArgs e)
        {
            var p = Browse("Chọn Model Phân tách NST chồng lấn");
            if (p == null) return;
            _config.OverlapModelPath = p;
            SetPathLabel(PathOverlap, p);
        }

        private void BrowseAnomaly_Click(object sender, RoutedEventArgs e)
        {
            var p = Browse("Chọn Model Xác định bất thường NST");
            if (p == null) return;
            _config.AnomalyModelPath = p;
            SetPathLabel(PathAnomaly, p);
        }

        private static string? Browse(string title)
        {
            var dlg = new WinOpenFile { Title = title, Filter = ModelFilter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(NormalCount.Text, out var nc) || nc <= 0)
            {
                System.Windows.MessageBox.Show(
                    "Số NST chuẩn phải là số nguyên dương.", "Lỗi",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(Tolerance.Text, out var tol) || tol < 0)
            {
                System.Windows.MessageBox.Show(
                    "Sai số cho phép phải là số nguyên không âm.", "Lỗi",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }
            _config.NormalChromosomeCount = nc;
            _config.Tolerance             = tol;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
