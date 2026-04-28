using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MedVisionAI.Models;
using MedVisionAI.Services;
using DragEventArgs = System.Windows.DragEventArgs;

namespace MedVisionAI.UI.Windows
{
    public partial class NSTWindow : Window
    {
        private readonly DispatcherTimer _clock;
        private readonly Action _onClose;
        private ModelConfig _config;

        private string? _currentImagePath;
        private string? _lastReportPlain;
        private BitmapImage? _lastResultImage;
        private int _lastCount;
        private bool _lastIsNormal;

        private int _dotCount;
        private DispatcherTimer? _animTimer;

        public event Action<string, int, bool, string>? AnalysisDone;

        private static readonly string ModelFilter =
            "Model AI|*.onnx;*.pt;*.pth;*.h5;*.pb|" +
            "ONNX (*.onnx)|*.onnx|PyTorch (*.pt;*.pth)|*.pt;*.pth|" +
            "TensorFlow/Keras (*.h5)|*.h5|Tất cả (*.*)|*.*";

        public NSTWindow(ModelConfig config, Action onClose)
        {
            InitializeComponent();
            _config = config;
            _onClose = onClose;

            // Apply model paths from dialog to toolbar labels
            UpdateToolbarLabels();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
        }

        // ── Toolbar label sync ───────────────────────────────────────────────

        private void UpdateToolbarLabels()
        {
            ToolbarSeg.Text = string.IsNullOrEmpty(_config.SegmentationModelPath)
                ? "Chưa chọn"
                : Path.GetFileName(_config.SegmentationModelPath);

            ToolbarOverlap.Text = string.IsNullOrEmpty(_config.OverlapModelPath)
                ? "Chưa chọn"
                : Path.GetFileName(_config.OverlapModelPath);

            ToolbarAnomaly.Text = string.IsNullOrEmpty(_config.AnomalyModelPath)
                ? "Chưa chọn"
                : Path.GetFileName(_config.AnomalyModelPath);

            // Highlight seg label green if loaded
            ToolbarSeg.Foreground = string.IsNullOrEmpty(_config.SegmentationModelPath)
                ? System.Windows.Media.Brushes.DarkGray
                : System.Windows.Media.Brushes.DarkGreen;
        }

        // ── Window chrome ────────────────────────────────────────────────────

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaxRestore_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaxRestore.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                BtnMaxRestore.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _clock.Stop();
            _onClose?.Invoke();
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            _clock.Stop();
            _onClose?.Invoke();
            Close();
        }

        // ── Toolbar browse buttons (Yêu cầu [5]) ────────────────────────────

        private void ToolbarSegmentation_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseModel("Chọn Model Phân đoạn NST");
            if (path == null) return;
            _config.SegmentationModelPath = path;
            UpdateToolbarLabels();
        }

        private void ToolbarOverlap_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseModel("Chọn Model Phân tách NST chồng lấn");
            if (path == null) return;
            _config.OverlapModelPath = path;
            UpdateToolbarLabels();
        }

        private void ToolbarAnomaly_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseModel("Chọn Model Xác định bất thường NST");
            if (path == null) return;
            _config.AnomalyModelPath = path;
            UpdateToolbarLabels();
        }

        private static string? BrowseModel(string title)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = ModelFilter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ── Image load ───────────────────────────────────────────────────────

        private void BtnLoadImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn ảnh NST",
                Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|Tất cả (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            LoadImage(dlg.FileName);
        }

        private void ImagePane_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files?.Length > 0)
                    LoadImage(files[0]);
            }
        }

        private void LoadImage(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                ImgOriginal.Source = bmp;
                PlaceholderOriginal.Visibility = Visibility.Collapsed;
                ImgResult.Source = null;
                PlaceholderResult.Visibility = Visibility.Visible;
                PlaceholderResult.Text = "Kết quả AI\nphân tích";

                _currentImagePath = path;
                _lastReportPlain = null;
                _lastResultImage = null;
                _lastCount = 0;

                BtnRunAI.IsEnabled = true;
                BtnReset.IsEnabled = true;
                BtnSave.IsEnabled = false;
                BtnExportCSV.IsEnabled = false;

                CountText.Text = "Kết quả đếm: —";
                ReportText.Text = "Nhấn 'Chạy AI phân tích' để xem kết quả.";
                SetStatus("Ảnh đã tải", "#15803D", "#DCFCE7", "#86EFAC");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Không đọc được ảnh:\n{ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── AI Analysis ──────────────────────────────────────────────────────

        private async void BtnRunAI_Click(object sender, RoutedEventArgs e)
        {
            if (_currentImagePath == null) return;

            if (string.IsNullOrEmpty(_config.SegmentationModelPath))
            {
                var result = MessageBox.Show(
                    "Chưa chọn Model Phân đoạn NST.\n\n" +
                    "Nhấn OK để chọn model ngay, hoặc Cancel để bỏ qua (chạy demo).",
                    "MedVision AI", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

                if (result == MessageBoxResult.OK)
                {
                    var path = BrowseModel("Chọn Model Phân đoạn NST");
                    if (path == null) return;
                    _config.SegmentationModelPath = path;
                    UpdateToolbarLabels();
                }
            }

            // Lock UI
            BtnRunAI.IsEnabled = false;
            BtnLoadImage_Enabled(false);
            ProgressBar.Visibility = Visibility.Visible;
            StartLoadingAnimation();

            try
            {
                var inferenceService = new InferenceService(_config);
                var analysisResult   = await Task.Run(() => inferenceService.Analyze(_currentImagePath));

                OnAnalysisFinished(analysisResult);
            }
            catch (Exception ex)
            {
                OnAnalysisFailed(ex.Message);
            }
        }

        private void OnAnalysisFinished(AnalysisResult result)
        {
            StopLoadingAnimation();
            ProgressBar.Visibility = Visibility.Collapsed;
            BtnRunAI.IsEnabled = true;
            BtnLoadImage_Enabled(true);

            _lastCount     = result.ChromosomeCount;
            _lastIsNormal  = result.IsNormalCount;
            _lastReportPlain = result.ReportPlain;

            // Show result image
            if (result.AnnotatedImageBytes != null)
            {
                var bmp = BytesToBitmap(result.AnnotatedImageBytes);
                ImgResult.Source = bmp;
                _lastResultImage = bmp;
                PlaceholderResult.Visibility = Visibility.Collapsed;
            }

            // Count display
            var countColor = result.IsNormalCount ? "#15803D" : "#DC2626";
            CountText.Text = $"{result.ChromosomeCount} NST — {(result.IsNormalCount ? "Bình thường" : "Cần xem xét")}";
            CountDisplay.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    result.IsNormalCount ? "#86EFAC" : "#FCA5A5"));

            // Report
            ReportText.Text = result.ReportPlain;

            // Status
            var statusMsg = result.IsNormalCount ? "Phân tích hoàn tất ✓" : "Cần theo dõi ⚠";
            SetStatus(statusMsg,
                result.IsNormalCount ? "#15803D" : "#B45309",
                result.IsNormalCount ? "#DCFCE7" : "#FEFCE8",
                result.IsNormalCount ? "#86EFAC" : "#FDE68A");

            BtnSave.IsEnabled = true;
            BtnExportCSV.IsEnabled = true;

            // Notify home window
            AnalysisDone?.Invoke(
                Path.GetFileName(_currentImagePath ?? ""),
                result.ChromosomeCount,
                result.IsNormalCount,
                result.IsNormalCount ? "Bình thường" : "Cần theo dõi");
        }

        private void OnAnalysisFailed(string message)
        {
            StopLoadingAnimation();
            ProgressBar.Visibility = Visibility.Collapsed;
            BtnRunAI.IsEnabled = true;
            BtnLoadImage_Enabled(true);
            SetStatus("Phân tích thất bại ✗", "#DC2626", "#FFF0F0", "#FCA5A5");
            MessageBox.Show($"Lỗi phân tích:\n{message}", "MedVision AI",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ── Loading animation ─────────────────────────────────────────────────

        private void StartLoadingAnimation()
        {
            _dotCount = 0;
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _animTimer.Tick += (_, _) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                StatusText.Text = $"Đang phân tích{new string('.', _dotCount)}";
            };
            _animTimer.Start();
            SetStatus("Đang phân tích...", "#B45309", "#FEFCE8", "#FDE68A");
        }

        private void StopLoadingAnimation()
        {
            _animTimer?.Stop();
            _animTimer = null;
        }

        // ── Status helper ─────────────────────────────────────────────────────

        private void SetStatus(string text, string fg, string bg, string border)
        {
            StatusText.Text = text;
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fg));
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bg));
            StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(border));
        }

        private void BtnLoadImage_Enabled(bool enabled)
        {
            // Find load button — simpler to just track it by x:Name if needed
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _currentImagePath = null;
            _lastReportPlain  = null;
            _lastResultImage  = null;
            _lastCount = 0;

            ImgOriginal.Source = null;
            PlaceholderOriginal.Visibility = Visibility.Visible;
            ImgResult.Source = null;
            PlaceholderResult.Visibility = Visibility.Visible;
            PlaceholderResult.Text = "Kết quả AI\nphân tích";

            CountText.Text = "Kết quả đếm: —";
            ReportText.Text = "Báo cáo sẽ xuất hiện sau khi chạy AI.";

            BtnRunAI.IsEnabled = false;
            BtnReset.IsEnabled = false;
            BtnSave.IsEnabled = false;
            BtnExportCSV.IsEnabled = false;

            SetStatus("Trạng thái: Sẵn sàng", "#9090A8", "#F7F7FB", "#EBEBF0");
        }

        // ── Save & Export ─────────────────────────────────────────────────────

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_lastReportPlain == null) return;

            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Chọn thư mục lưu kết quả"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                var folder  = dlg.SelectedPath;
                var stem    = Path.GetFileNameWithoutExtension(_currentImagePath ?? "anh");
                var ts      = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // Save annotated image
                if (_lastResultImage != null)
                {
                    var imgPath = Path.Combine(folder, $"ket_qua_{stem}_{ts}.png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(_lastResultImage));
                    using var fs = new FileStream(imgPath, FileMode.Create);
                    encoder.Save(fs);
                }

                // Save report text
                var reportPath = Path.Combine(folder, $"bao_cao_{stem}_{ts}.txt");
                var lines = new[]
                {
                    "BÁO CÁO PHÂN TÍCH NST (MedVision AI)",
                    "=" .PadRight(40, '='),
                    $"Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                    $"Ảnh gốc: {Path.GetFileName(_currentImagePath ?? "")}",
                    "",
                    _lastReportPlain ?? "",
                    "",
                    "-".PadRight(40, '-'),
                    "Lưu ý: Kết quả hỗ trợ chẩn đoán, không thay thế xét nghiệm chuyên sâu."
                };
                File.WriteAllLines(reportPath, lines, System.Text.Encoding.UTF8);

                MessageBox.Show($"Đã lưu thành công tại:\n{folder}", "MedVision AI",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi lưu file:\n{ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportCSV_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title  = "Xuất báo cáo CSV",
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"Bao_cao_NST_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = new[]
                {
                    "Thời gian phân tích,Tên file ảnh,Số lượng NST,Kết luận",
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                    $"{Path.GetFileName(_currentImagePath ?? "")}," +
                    $"{_lastCount}," +
                    $"\"{_lastReportPlain?.Replace("\"","'") ?? ""}\""
                };
                File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
                MessageBox.Show($"Đã xuất CSV:\n{dlg.FileName}", "MedVision AI",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi xuất CSV:\n{ex.Message}", "Lỗi",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(_config) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _config = dlg.UpdatedConfig;
                UpdateToolbarLabels();
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static BitmapImage BytesToBitmap(byte[] bytes)
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.StreamSource  = ms;
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
    }
}
