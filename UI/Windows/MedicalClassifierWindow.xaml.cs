using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MedVisionAI.Models;
using MedVisionAI.Services;

using WinOpenFile = Microsoft.Win32.OpenFileDialog;
using WinMsgBox = System.Windows.MessageBox;
using WinMsgBtn = System.Windows.MessageBoxButton;
using WinMsgImg = System.Windows.MessageBoxImage;
using WinMsgResult = System.Windows.MessageBoxResult;
using WinDragArgs = System.Windows.DragEventArgs;
using WinDataFmt = System.Windows.DataFormats;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConv = System.Windows.Media.ColorConverter;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace MedVisionAI.UI.Windows
{
    public partial class MedicalClassifierWindow : Window
    {
        private readonly MedicalClassifierKind _kind;
        private readonly Action _onClose;
        private readonly DispatcherTimer _clock;
        private DispatcherTimer? _animTimer;
        private int _dotCount;
        private bool _closedHandled;

        private string? _modelPath;
        private string? _imagePath;
        private BitmapImage? _resultImage;
        private MedicalClassifierResult? _lastResult;

        private readonly ModuleInfo _module;

        public event Action<string, string, bool, string>? AnalysisDone;

        private static readonly string ModelFilter =
            "Model AI|*.pth;*.pt;*.onnx;*.h5;*.pb|" +
            "PyTorch (*.pth;*.pt)|*.pth;*.pt|ONNX (*.onnx)|*.onnx|" +
            "TensorFlow/Keras (*.h5)|*.h5|Tất cả (*.*)|*.*";

        private static readonly string ImageFilter =
            "Ảnh (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)" +
            "|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|Tất cả (*.*)|*.*";

        public MedicalClassifierWindow(MedicalClassifierKind kind, Action onClose)
        {
            InitializeComponent();
            _kind = kind;
            _onClose = onClose;
            _module = ModuleInfo.For(kind);

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
                ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");

            ApplyModuleVisuals();
            ResetUi(clearImage: true);
        }

        private void ApplyModuleVisuals()
        {
            Title = $"MedVision AI — {_module.Title}";
            HeaderTitle.Text = _module.Title;
            HeaderSubtitle.Text = _module.Subtitle;
            HeaderLogo.Source = new BitmapImage(new Uri(_module.LogoPath, UriKind.Relative));
            ResultPanelTitle.Text = _module.ResultTitle.ToUpperInvariant();

            var accent = Brush(_module.AccentHex);
            var light = Brush(_module.LightHex);
            var border = Brush(_module.BorderHex);

            foreach (var button in OutlineButtons())
            {
                button.BorderBrush = accent;
                button.Foreground = accent;
                button.Background = WpfBrushes.White;
                AttachOutlineButtonHover(button);
            }

            BtnChangeModel.Background = accent;
            BtnChangeModel.Foreground = WpfBrushes.White;
            AttachFilledButtonHover(BtnChangeModel);
            ModelBadgeText.Foreground = accent;
            Progress.Foreground = accent;
            ConfidenceText.Foreground = accent;
            RiskText.Foreground = accent;

            ControlPanel.BorderBrush = border;
            ImagePanel.BorderBrush = border;
            OriginalPane.BorderBrush = border;
            ResultPane.BorderBrush = border;
            ResultPanel.BorderBrush = border;
            ResultDisplay.Background = light;
            ResultDisplay.BorderBrush = border;
        }

        private WpfButton? BtnHomeButtonSafe()
        {
            return FindName("BtnHome") as WpfButton;
        }

        private WpfButton[] OutlineButtons()
        {
            var home = BtnHomeButtonSafe();
            return home == null
                ? new[] { BtnModel, BtnLoad, BtnRun, BtnReset, BtnSave }
                : new[] { BtnModel, BtnLoad, BtnRun, BtnReset, BtnSave, home };
        }

        private void AttachOutlineButtonHover(WpfButton button)
        {
            button.MouseEnter -= OutlineButton_MouseEnter;
            button.MouseLeave -= OutlineButton_MouseLeave;
            button.IsEnabledChanged -= OutlineButton_IsEnabledChanged;
            button.MouseEnter += OutlineButton_MouseEnter;
            button.MouseLeave += OutlineButton_MouseLeave;
            button.IsEnabledChanged += OutlineButton_IsEnabledChanged;
            RestoreOutlineButton(button);
        }

        private void AttachFilledButtonHover(WpfButton button)
        {
            button.MouseEnter -= FilledButton_MouseEnter;
            button.MouseLeave -= FilledButton_MouseLeave;
            button.IsEnabledChanged -= FilledButton_IsEnabledChanged;
            button.MouseEnter += FilledButton_MouseEnter;
            button.MouseLeave += FilledButton_MouseLeave;
            button.IsEnabledChanged += FilledButton_IsEnabledChanged;
            RestoreFilledButton(button);
        }

        private void OutlineButton_MouseEnter(object sender, WpfMouseEventArgs e)
        {
            if (sender is not WpfButton button || !button.IsEnabled) return;
            button.Background = Brush(_module.AccentHex);
            button.BorderBrush = Brush(_module.AccentHex);
            button.Foreground = WpfBrushes.White;
        }

        private void OutlineButton_MouseLeave(object sender, WpfMouseEventArgs e)
        {
            if (sender is WpfButton button)
                RestoreOutlineButton(button);
        }

        private void OutlineButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is WpfButton button)
                RestoreOutlineButton(button);
        }

        private void FilledButton_MouseEnter(object sender, WpfMouseEventArgs e)
        {
            if (sender is not WpfButton button || !button.IsEnabled) return;
            button.Background = Brush(_module.DarkHex);
            button.Foreground = WpfBrushes.White;
        }

        private void FilledButton_MouseLeave(object sender, WpfMouseEventArgs e)
        {
            if (sender is WpfButton button)
                RestoreFilledButton(button);
        }

        private void FilledButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is WpfButton button)
                RestoreFilledButton(button);
        }

        private void RestoreOutlineButton(WpfButton button)
        {
            button.Background = WpfBrushes.White;
            button.BorderBrush = Brush(_module.AccentHex);
            button.Foreground = Brush(_module.AccentHex);
        }

        private void RestoreFilledButton(WpfButton button)
        {
            button.Background = Brush(_module.AccentHex);
            button.Foreground = WpfBrushes.White;
        }

        private void BtnModel_Click(object sender, RoutedEventArgs e)
            => BrowseAndSetModel();

        private void BtnChangeModel_Click(object sender, RoutedEventArgs e)
            => BrowseAndSetModel();

        private void BrowseAndSetModel()
        {
            var dlg = new WinOpenFile
            {
                Title = _module.ModelDialogTitle,
                Filter = ModelFilter
            };

            if (dlg.ShowDialog() != true) return;

            _modelPath = dlg.FileName;
            ModelBadgeText.Text = Path.GetFileName(_modelPath);
            SetStatus("Model AI đã chọn", "#15803D", "#DCFCE7", "#86EFAC");
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WinOpenFile
            {
                Title = $"Chọn ảnh cho {_module.Title}",
                Filter = ImageFilter
            };

            if (dlg.ShowDialog() == true)
                ApplyImage(dlg.FileName);
        }

        private void ImagePane_Drop(object sender, WinDragArgs e)
        {
            if (!e.Data.GetDataPresent(WinDataFmt.FileDrop)) return;
            var files = (string[])e.Data.GetData(WinDataFmt.FileDrop);
            if (files?.Length > 0)
                ApplyImage(files[0]);
        }

        private void ApplyImage(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                _imagePath = path;
                _resultImage = null;
                _lastResult = null;

                ImgOriginal.Source = bmp;
                ImgResult.Source = null;
                PlaceholderOrig.Visibility = Visibility.Collapsed;
                PlaceholderResult.Visibility = Visibility.Visible;
                ResultText.Text = "—";
                ConfidenceText.Text = "—";
                RiskText.Text = "—";
                ReportText.Text = "Nhấn 'Chạy AI phân tích' để xem kết quả.";
                BtnRun.IsEnabled = true;
                BtnReset.IsEnabled = true;
                BtnSave.IsEnabled = false;
                SetStatus("Ảnh đã tải", "#15803D", "#DCFCE7", "#86EFAC");
            }
            catch (Exception ex)
            {
                WinMsgBox.Show(
                    $"Không đọc được ảnh:\n{ex.Message}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Error);
            }
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_imagePath == null) return;
            if (!EnsureModelSelected()) return;

            BtnRun.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            StartAnim(txt => SetStatus($"Đang phân tích{txt}", "#B45309", "#FEFCE8", "#FDE68A"));

            try
            {
                var svc = new MedicalClassifierService(_modelPath!, _kind);
                var result = await Task.Run(() => svc.Analyze(_imagePath));

                _lastResult = result;
                _resultImage = BytesToBitmap(result.AnnotatedImageBytes!);
                ImgResult.Source = _resultImage;
                PlaceholderResult.Visibility = Visibility.Collapsed;

                ResultText.Text = result.DisplayLabel;
                ConfidenceText.Text = $"{result.Confidence:P1}";
                RiskText.Text = result.RiskLevel;
                ReportText.Text = result.ReportPlain;
                BtnSave.IsEnabled = true;

                SetStatus(
                    result.IsNormal ? "Phân tích hoàn tất ✓" : "Phát hiện dấu hiệu cần theo dõi ⚠",
                    result.IsNormal ? "#15803D" : _module.AccentHex,
                    result.IsNormal ? "#DCFCE7" : _module.LightHex,
                    result.IsNormal ? "#86EFAC" : _module.BorderHex);

                AnalysisDone?.Invoke(
                    Path.GetFileName(_imagePath),
                    result.DisplayLabel,
                    result.IsNormal,
                    result.IsNormal ? "Bình thường" : "Cần theo dõi");
            }
            catch (Exception ex)
            {
                WinMsgBox.Show(
                    $"Lỗi phân tích:\n{ex.Message}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Error);
            }
            finally
            {
                StopAnim();
                Progress.Visibility = Visibility.Collapsed;
                BtnRun.IsEnabled = _imagePath != null;
            }
        }

        private bool EnsureModelSelected()
        {
            if (!string.IsNullOrWhiteSpace(_modelPath))
                return true;

            var answer = WinMsgBox.Show(
                $"Chức năng {_module.Title} cần chọn model AI trước khi phân tích.\n\n" +
                $"Nhấn OK để chọn model {_module.ExpectedModelName}.",
                "MedVision AI — Cần chọn model AI",
                WinMsgBtn.OKCancel,
                WinMsgImg.Information);

            if (answer != WinMsgResult.OK)
                return false;

            BrowseAndSetModel();
            return !string.IsNullOrWhiteSpace(_modelPath);
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
            => ResetUi(clearImage: true);

        private void ResetUi(bool clearImage)
        {
            if (clearImage)
            {
                _imagePath = null;
                ImgOriginal.Source = null;
                PlaceholderOrig.Visibility = Visibility.Visible;
            }

            _resultImage = null;
            _lastResult = null;
            ImgResult.Source = null;
            PlaceholderResult.Visibility = Visibility.Visible;
            ResultText.Text = "—";
            ConfidenceText.Text = "—";
            RiskText.Text = "—";
            ReportText.Text = "Báo cáo phân tích sẽ xuất hiện sau khi chạy AI.";
            BtnRun.IsEnabled = !string.IsNullOrWhiteSpace(_imagePath);
            BtnReset.IsEnabled = !string.IsNullOrWhiteSpace(_imagePath);
            BtnSave.IsEnabled = false;
            SetStatus("Trạng thái: Sẵn sàng", "#9090A8", "#F7F7FB", "#EBEBF0");
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null || _imagePath == null) return;

            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Chọn thư mục lưu kết quả"
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                string folder = dlg.SelectedPath;
                string stem = Path.GetFileNameWithoutExtension(_imagePath);
                string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string prefix = _kind == MedicalClassifierKind.BloodCancer
                    ? "ung_thu_te_bao_mau"
                    : "ky_sinh_trung_sot_ret";

                if (_lastResult.AnnotatedImageBytes != null)
                {
                    File.WriteAllBytes(
                        Path.Combine(folder, $"ket_qua_{prefix}_{stem}_{ts}.png"),
                        _lastResult.AnnotatedImageBytes);
                }

                File.WriteAllText(
                    Path.Combine(folder, $"bao_cao_{prefix}_{stem}_{ts}.txt"),
                    _lastResult.ReportPlain,
                    System.Text.Encoding.UTF8);

                WinMsgBox.Show(
                    $"Đã lưu thành công tại:\n{folder}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Information);
            }
            catch (Exception ex)
            {
                WinMsgBox.Show(
                    $"Lỗi lưu file:\n{ex.Message}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Error);
            }
        }

        private void SetStatus(string text, string fg, string bg, string border)
        {
            StatusText.Text = text;
            StatusText.Foreground = Brush(fg);
            StatusBadge.Background = Brush(bg);
            StatusBadge.BorderBrush = Brush(border);
        }

        private void StartAnim(Action<string> updateFn)
        {
            _dotCount = 0;
            _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _animTimer.Tick += (_, _) =>
            {
                _dotCount = (_dotCount + 1) % 4;
                updateFn(new string('.', _dotCount));
            };
            _animTimer.Start();
        }

        private void StopAnim()
        {
            _animTimer?.Stop();
            _animTimer = null;
        }

        private static BitmapImage BytesToBitmap(byte[] bytes)
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static SolidColorBrush Brush(string hex)
            => new((WpfColor)WpfColorConv.ConvertFromString(hex)!);

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

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            _closedHandled = true;
            _clock.Stop();
            Hide();
            _onClose?.Invoke();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_closedHandled) return;
            _clock.Stop();
            _onClose?.Invoke();
        }

        public void ReActivate()
        {
            if (!_clock.IsEnabled) _clock.Start();
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }

        private sealed record ModuleInfo(
            string Title,
            string Subtitle,
            string ResultTitle,
            string LogoPath,
            string AccentHex,
            string DarkHex,
            string LightHex,
            string BorderHex,
            string ModelDialogTitle,
            string ExpectedModelName)
        {
            public static ModuleInfo For(MedicalClassifierKind kind) => kind switch
            {
                MedicalClassifierKind.BloodCancer => new ModuleInfo(
                    "Phát hiện ung thư tế bào máu",
                    "Phân loại Benign / Early / Pre / Pro",
                    "Kết quả ung thư tế bào máu",
                    "/Assets/Brand/BloodDetection.png",
                    "#DC2626",
                    "#B91C1C",
                    "#FFF0F0",
                    "#FECACA",
                    "Chọn model phát hiện ung thư tế bào máu (best_BloodCancerNET.pth)",
                    "best_BloodCancerNET.pth"),
                MedicalClassifierKind.Malaria => new ModuleInfo(
                    "Phát hiện ký sinh trùng sốt rét",
                    "Phân loại Parasitized / Uninfected",
                    "Kết quả ký sinh trùng sốt rét",
                    "/Assets/Brand/Parasite.png",
                    "#16A34A",
                    "#15803D",
                    "#F0FDF4",
                    "#BBF7D0",
                    "Chọn model phát hiện ký sinh trùng sốt rét (best_MalariaNET.pth)",
                    "best_MalariaNET.pth"),
                _ => throw new NotSupportedException($"Unsupported module: {kind}")
            };
        }
    }
}
