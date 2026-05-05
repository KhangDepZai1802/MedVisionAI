using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MedVisionAI.Models;
using MedVisionAI.Services;

using WinOpenFile  = Microsoft.Win32.OpenFileDialog;
using WinSaveFile  = Microsoft.Win32.SaveFileDialog;
using WinMsgBox    = System.Windows.MessageBox;
using WinMsgBtn    = System.Windows.MessageBoxButton;
using WinMsgImg    = System.Windows.MessageBoxImage;
using WinMsgResult = System.Windows.MessageBoxResult;
using WinDataFmt   = System.Windows.DataFormats;
using WinDragArgs  = System.Windows.DragEventArgs;
using WpfColor     = System.Windows.Media.Color;
using WpfColorConv = System.Windows.Media.ColorConverter;
using WpfHA        = System.Windows.HorizontalAlignment;

namespace MedVisionAI.UI.Windows
{
    // ── Per-tab state container ────────────────────────────────────────────────
    internal class TabState
    {
        public string?       ModelPath       { get; set; }
        public string?       ImagePath       { get; set; }
        public string?       ReportPlain     { get; set; }
        public BitmapImage?  ResultImage     { get; set; }
        public AnalysisResult? LastResult    { get; set; }
        public bool          HasResult       => LastResult != null;
    }

    public partial class NSTWindow : Window
    {
        private readonly DispatcherTimer _clock;
        private readonly Action         _onClose;
        private ModelConfig             _sharedConfig; // giữ NormalChromosomeCount / Tolerance

        // 3 tab states
        private readonly TabState _st0 = new(); // Phân đoạn NST
        private readonly TabState _st1 = new(); // NST chồng chất
        private readonly TabState _st2 = new(); // Xác định bất thường

        private AnalysisResult?   _segmentationForAnomaly;
        private int              _activeTab  = 0;
        private int              _dotCount;
        private DispatcherTimer? _animTimer;

        public event Action<string, int, bool, string>? AnalysisDone;

        private static readonly string ModelFilter =
            "Model AI|*.onnx;*.pt;*.pth;*.h5;*.pb|" +
            "ONNX (*.onnx)|*.onnx|PyTorch (*.pt;*.pth)|*.pt;*.pth|" +
            "TensorFlow/Keras (*.h5)|*.h5|Tất cả (*.*)|*.*";

        private static readonly string ImageFilter =
            "Ảnh (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)" +
            "|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|Tất cả (*.*)|*.*";

        public NSTWindow(ModelConfig config, Action onClose)
        {
            InitializeComponent();
            _sharedConfig = config;

            // Khởi tạo model path từ config ban đầu
            _st0.ModelPath = config.SegmentationModelPath;
            _st1.ModelPath = config.OverlapModelPath;
            _st2.ModelPath = config.AnomalyModelPath;

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
                ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");

            _onClose = onClose;

            // Init Denver grid placeholders
            BuildDenverGrid();

            // Activate tab 0 UI
            RefreshTabUI();
        }

        // ══════════════════════════════════════════════════════════════════════
        // TAB SWITCHING
        // ══════════════════════════════════════════════════════════════════════

        private void Tab0_Click(object s, RoutedEventArgs e) => SwitchTab(0);
        private void Tab1_Click(object s, RoutedEventArgs e) => SwitchTab(1);
        private void Tab2_Click(object s, RoutedEventArgs e) => SwitchTab(2);

        private void SwitchTab(int idx)
        {
            // Nếu tab chưa có model → hiện dialog chọn model trước
            var st = GetState(idx);
            if (string.IsNullOrEmpty(st.ModelPath))
            {
                bool chosen = PromptModelForTab(idx);
                if (!chosen) return; // user cancel → không chuyển tab
            }

            _activeTab = idx;
            RefreshTabUI();
        }

        /// <summary>Hiện ModelSelectDialog mini chỉ cho tab đó.</summary>
        private bool PromptModelForTab(int idx)
        {
            string tabName = idx switch
            {
                0 => "Phân đoạn NST",
                1 => "NST chồng chất",
                _ => "Xác định bất thường NST"
            };

            var path = BrowseModel($"Chọn Model: {tabName}");
            if (path == null) return false; // user đóng dialog → không chuyển

            GetState(idx).ModelPath = path;
            SyncConfigFromStates();
            return true;
        }

        private void RefreshTabUI()
        {
            PanelTab0.Visibility = _activeTab == 0 ? Visibility.Visible : Visibility.Collapsed;
            PanelTab1.Visibility = _activeTab == 1 ? Visibility.Visible : Visibility.Collapsed;
            PanelTab2.Visibility = _activeTab == 2 ? Visibility.Visible : Visibility.Collapsed;

            // Tab button highlight — tìm TextBlock đầu tiên trong từng button
            SetTabBtnHighlight(Tab0Btn, _activeTab == 0, "#7132F5");
            SetTabBtnHighlight(Tab1Btn, _activeTab == 1, "#DC2626");
            SetTabBtnHighlight(Tab2Btn, _activeTab == 2, "#16A34A");

            // Model badge
            var st = GetState(_activeTab);
            ModelBadgeText.Text = string.IsNullOrEmpty(st.ModelPath)
                ? "Chưa chọn"
                : System.IO.Path.GetFileName(st.ModelPath);
        }

        /// <summary>Tô màu tab button bằng cách set Tag rồi dùng trigger, hoặc set trực tiếp Foreground trên button.</summary>
        private static void SetTabBtnHighlight(System.Windows.Controls.Button btn, bool active, string activeHex)
        {
            // Set foreground trực tiếp trên button — override style trigger
            btn.Foreground = active
                ? new SolidColorBrush((WpfColor)WpfColorConv.ConvertFromString(activeHex)!)
                : new SolidColorBrush(WpfColor.FromRgb(0x90, 0x90, 0xA8));
            btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            btn.Tag        = active ? "active" : "";
        }

        // ══════════════════════════════════════════════════════════════════════
        // "Đổi model" badge button
        // ══════════════════════════════════════════════════════════════════════

        private void BtnChangeModel_Click(object s, RoutedEventArgs e)
        {
            string tabName = _activeTab switch
            {
                0 => "Phân đoạn NST",
                1 => "NST chồng chất",
                _ => "Xác định bất thường NST"
            };
            var path = BrowseModel($"Đổi model: {tabName}");
            if (path == null) return;
            GetState(_activeTab).ModelPath = path;
            SyncConfigFromStates();
            RefreshTabUI();
        }

        // ══════════════════════════════════════════════════════════════════════
        // TAB 0 — PHÂN ĐOẠN NST
        // ══════════════════════════════════════════════════════════════════════

        private void T0_BtnLoad_Click(object s, RoutedEventArgs e) => LoadImageForTab(0);
        private void T0_ImagePane_Drop(object s, WinDragArgs e)    => DropImageForTab(0, e);

        private async void T0_BtnRun_Click(object s, RoutedEventArgs e)
        {
            if (_st0.ImagePath == null) return;
            await RunAnalysisTab0();
        }

        private void T0_BtnReset_Click(object s, RoutedEventArgs e)
        {
            _st0.ImagePath    = null;
            _st0.ReportPlain  = null;
            _st0.ResultImage  = null;
            _st0.LastResult   = null;
            T0ImgOriginal.Source             = null;
            T0ImgResult.Source               = null;
            T0PlaceholderOrig.Visibility     = Visibility.Visible;
            T0PlaceholderResult.Visibility   = Visibility.Visible;
            T0CountText.Text                 = "Số NST: —";
            T0ReportText.Text                = "Báo cáo phân đoạn NST sẽ xuất hiện sau khi chạy AI.";
            T0BtnRun.IsEnabled               = false;
            T0BtnReset.IsEnabled             = false;
            T0BtnSave.IsEnabled              = false;
            T0BtnCSV.IsEnabled               = false;
            T0BtnAnomaly.IsEnabled           = false;
            SetStatusT0("Trạng thái: Sẵn sàng", "#9090A8", "#F7F7FB", "#EBEBF0");
            ClearDenverGrid();
        }

        private void T0_BtnSave_Click(object s, RoutedEventArgs e) => SaveResult(_st0, "phan_doan_NST");
        private void T0_BtnCSV_Click (object s, RoutedEventArgs e) => ExportCSV(_st0);
        private async void T0_BtnAnomaly_Click(object s, RoutedEventArgs e)
            => await RunAnomalyFromTab0();

        private async Task RunAnalysisTab0()
        {
            EnsureModelForActiveTab();
            T0BtnRun.IsEnabled      = false;
            T0Progress.Visibility   = Visibility.Visible;
            StartAnim(txt => SetStatusT0($"Đang phân tích{txt}", "#B45309", "#FEFCE8", "#FDE68A"));

            try
            {
                var cfg = BuildConfig(_st0.ModelPath, null, null);
                var svc = new InferenceService(cfg);
                var res = await Task.Run(() => svc.Analyze(_st0.ImagePath!));
                _st0.LastResult   = res;
                _st0.ReportPlain  = res.ReportPlain;
                if (res.AnnotatedImageBytes != null)
                {
                    _st0.ResultImage             = BytesToBitmap(res.AnnotatedImageBytes);
                    T0ImgResult.Source           = _st0.ResultImage;
                    T0PlaceholderResult.Visibility = Visibility.Collapsed;
                }
                T0CountText.Text = $"{res.ChromosomeCount} NST — " +
                                   $"{(res.IsNormalCount ? "Bình thường" : "Cần xem xét")}";
                T0ReportText.Text = res.ReportPlain;
                UpdateDenverGrid(res.DenverGroups);
                T0BtnSave.IsEnabled = true;
                T0BtnCSV.IsEnabled  = true;
                T0BtnAnomaly.IsEnabled = res.BoundingBoxes.Count > 0;
                bool ok = res.IsNormalCount;
                SetStatusT0(ok ? "Phân tích hoàn tất ✓" : "Cần theo dõi ⚠",
                    ok ? "#15803D" : "#B45309",
                    ok ? "#DCFCE7" : "#FEFCE8",
                    ok ? "#86EFAC" : "#FDE68A");

                AnalysisDone?.Invoke(
                    System.IO.Path.GetFileName(_st0.ImagePath ?? ""),
                    res.ChromosomeCount, res.IsNormalCount,
                    res.IsNormalCount ? "Bình thường" : "Cần theo dõi");
            }
            catch (Exception ex) { OnFailed(ex.Message); }
            finally
            {
                StopAnim();
                T0Progress.Visibility = Visibility.Collapsed;
                T0BtnRun.IsEnabled    = true;
            }
        }

        private async Task RunAnomalyFromTab0()
        {
            if (_st0.ImagePath == null || _st0.LastResult == null)
                return;

            if (_st0.LastResult.BoundingBoxes.Count == 0)
            {
                WinMsgBox.Show(
                    "Chưa có bounding box từ bước phân đoạn để crop NST.",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Warning);
                return;
            }

            var modelPath = _st2.ModelPath;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                modelPath = BrowseModel("Chọn model xác định bất thường (BatThuongNST.pth)");
                if (modelPath == null)
                    return;
            }

            _st2.ModelPath = modelPath;
            _st2.ImagePath = _st0.ImagePath;
            _st2.ReportPlain = null;
            _st2.ResultImage = null;
            _st2.LastResult = null;
            _segmentationForAnomaly = _st0.LastResult;
            _sharedConfig.AnomalyModelPath = modelPath;

            T2ImgOriginal.Source = T0ImgOriginal.Source;
            T2ImgResult.Source = null;
            T2PlaceholderOrig.Visibility = Visibility.Collapsed;
            T2PlaceholderResult.Visibility = Visibility.Visible;
            T2RiskText.Text = "—";
            T2SyndromeText.Text = "—";
            T2SexText.Text = "—";
            T2ReportText.Text = "Đang dùng kết quả phân đoạn NST để crop và xác định bất thường.";
            T2BtnRun.IsEnabled = true;
            T2BtnReset.IsEnabled = true;
            T2BtnSave.IsEnabled = false;
            T2BtnCSV.IsEnabled = false;

            _activeTab = 2;
            RefreshTabUI();
            await RunAnalysisTab2();
        }

        private void SetStatusT0(string text, string fg, string bg, string border)
            => SetStatus(T0StatusText, T0StatusBadge, text, fg, bg, border);

        // ══════════════════════════════════════════════════════════════════════
        // TAB 1 — NST CHỒNG CHẤT
        // ══════════════════════════════════════════════════════════════════════

        private void T1_BtnLoad_Click(object s, RoutedEventArgs e) => LoadImageForTab(1);
        private void T1_ImagePane_Drop(object s, WinDragArgs e)    => DropImageForTab(1, e);

        private async void T1_BtnRun_Click(object s, RoutedEventArgs e)
        {
            if (_st1.ImagePath == null) return;
            await RunAnalysisTab1();
        }

        private void T1_BtnReset_Click(object s, RoutedEventArgs e)
        {
            _st1.ImagePath  = null;
            _st1.ReportPlain = null;
            _st1.ResultImage = null;
            _st1.LastResult  = null;
            T1ImgOriginal.Source           = null;
            T1ImgResult.Source             = null;
            T1PlaceholderOrig.Visibility   = Visibility.Visible;
            T1PlaceholderResult.Visibility = Visibility.Visible;
            T1OverlapCount.Text            = "NST chồng lấn: —";
            T1TotalDetected.Text           = "—";
            T1OverlapPairs.Text            = "—";
            T1ReportText.Text              = "Báo cáo phân tách NST chồng lấn sẽ xuất hiện sau khi chạy AI.";
            T1BtnRun.IsEnabled             = false;
            T1BtnReset.IsEnabled           = false;
            T1BtnSave.IsEnabled            = false;
            SetStatusT1("Trạng thái: Sẵn sàng", "#9090A8", "#F7F7FB", "#EBEBF0");
        }

        private void T1_BtnSave_Click(object s, RoutedEventArgs e) => SaveResult(_st1, "NST_chong_chat");

        private async Task RunAnalysisTab1()
        {
            EnsureModelForActiveTab();
            T1BtnRun.IsEnabled    = false;
            T1Progress.Visibility = Visibility.Visible;
            StartAnim(txt => SetStatusT1($"Đang phân tích{txt}", "#B45309", "#FEFCE8", "#FDE68A"));

            try
            {
                var cfg = BuildConfig(null, _st1.ModelPath, null);
                var svc = new InferenceService(cfg);
                var res = await Task.Run(() => svc.Analyze(_st1.ImagePath!));
                _st1.LastResult  = res;
                _st1.ReportPlain = res.ReportPlain;

                if (res.AnnotatedImageBytes != null)
                {
                    _st1.ResultImage             = BytesToBitmap(res.AnnotatedImageBytes);
                    T1ImgResult.Source           = _st1.ResultImage;
                    T1PlaceholderResult.Visibility = Visibility.Collapsed;
                }

                // Tính overlap: các box có IoU > 0 với box khác
                int overlapCount = CountOverlappingBoxes(res.BoundingBoxes);
                int pairs        = overlapCount / 2;

                T1OverlapCount.Text   = $"NST chồng lấn: {overlapCount} / {res.ChromosomeCount}";
                T1TotalDetected.Text  = res.ChromosomeCount.ToString();
                T1OverlapPairs.Text   = pairs.ToString();
                T1ReportText.Text     = BuildOverlapReport(res, overlapCount, pairs);
                T1BtnSave.IsEnabled   = true;

                bool ok = overlapCount == 0;
                SetStatusT1(ok ? "Không phát hiện chồng lấn ✓" : $"Phát hiện {overlapCount} NST chồng lấn ⚠",
                    ok ? "#15803D" : "#B45309",
                    ok ? "#DCFCE7" : "#FEFCE8",
                    ok ? "#86EFAC" : "#FDE68A");
            }
            catch (Exception ex) { OnFailed(ex.Message); }
            finally
            {
                StopAnim();
                T1Progress.Visibility = Visibility.Collapsed;
                T1BtnRun.IsEnabled    = true;
            }
        }

        private void SetStatusT1(string text, string fg, string bg, string border)
            => SetStatus(T1StatusText, T1StatusBadge, text, fg, bg, border);

        // ══════════════════════════════════════════════════════════════════════
        // TAB 2 — XÁC ĐỊNH BẤT THƯỜNG NST
        // ══════════════════════════════════════════════════════════════════════

        private async void T2_BtnRun_Click(object s, RoutedEventArgs e)
        {
            if (_st2.ImagePath == null) return;
            
            // Nhắc nhở người dùng chọn model AI nếu chưa có
            if (string.IsNullOrWhiteSpace(_st2.ModelPath))
            {
                var ans = WinMsgBox.Show(
                    "Để xác định bất thường NST, bạn cần chọn model AI.\n\n" +
                    "Bấm OK để chọn file model hoặc Cancel để hủy.",
                    "MedVision AI — Cần chọn Model AI",
                    WinMsgBtn.OKCancel, WinMsgImg.Information);

                if (ans == WinMsgResult.Cancel)
                    return;
            }
            
            await RunAnalysisTab2();
        }

        private void T2_BtnReset_Click(object s, RoutedEventArgs e)
        {
            _segmentationForAnomaly = null;
            _st2.ImagePath  = null;
            _st2.ReportPlain = null;
            _st2.ResultImage = null;
            _st2.LastResult  = null;
            T2ImgOriginal.Source           = null;
            T2ImgResult.Source             = null;
            T2PlaceholderOrig.Visibility   = Visibility.Visible;
            T2PlaceholderResult.Visibility = Visibility.Visible;
            T2RiskText.Text                = "—";
            T2SyndromeText.Text            = "—";
            T2SexText.Text                 = "—";
            T2ReportText.Text              = "Báo cáo bất thường NST sẽ xuất hiện sau khi chạy AI.";
            T2BtnRun.IsEnabled             = false;
            T2BtnReset.IsEnabled           = false;
            T2BtnSave.IsEnabled            = false;
            T2BtnCSV.IsEnabled             = false;
            SetStatusT2("Trạng thái: Sẵn sàng", "#9090A8", "#F7F7FB", "#EBEBF0");
        }

        private void T2_BtnSave_Click(object s, RoutedEventArgs e) => SaveResult(_st2, "bat_thuong_NST");
        private void T2_BtnCSV_Click (object s, RoutedEventArgs e) => ExportCSV(_st2);

        private async Task RunAnalysisTab2()
        {
            if (_st2.ImagePath == null || _segmentationForAnomaly == null)
            {
                WinMsgBox.Show(
                    "Vui lòng chạy phân đoạn NST trước, sau đó bấm 'Xác định bất thường' từ trang phân đoạn.",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_st2.ModelPath))
            {
                var modelPath = BrowseModel("Chọn model xác định bất thường (BatThuongNST.pth)");
                if (modelPath == null)
                    return;

                _st2.ModelPath = modelPath;
                _sharedConfig.AnomalyModelPath = modelPath;
                RefreshTabUI();
            }

            T2BtnRun.IsEnabled    = false;
            T2Progress.Visibility = Visibility.Visible;
            StartAnim(txt => SetStatusT2($"Đang phân tích{txt}", "#B45309", "#FEFCE8", "#FDE68A"));

            try
            {
                var cfg = BuildConfig(null, null, _st2.ModelPath);
                var svc = new InferenceService(cfg);
                var res = await Task.Run(() => svc.AnalyzeAnomaliesFromSegmentation(
                    _st2.ImagePath!, _segmentationForAnomaly!, _st2.ModelPath!));
                _st2.LastResult  = res;
                _st2.ReportPlain = res.ReportPlain;

                if (res.AnnotatedImageBytes != null)
                {
                    _st2.ResultImage             = BytesToBitmap(res.AnnotatedImageBytes);
                    T2ImgResult.Source           = _st2.ResultImage;
                    T2PlaceholderResult.Visibility = Visibility.Collapsed;
                }

                // Risk color
                T2RiskText.Text = res.RiskLevel;
                T2RiskText.Foreground = res.RiskLevel switch
                {
                    var r when r.Contains("cao")    => new SolidColorBrush(WpfColor.FromRgb(0xDC, 0x26, 0x26)),
                    var r when r.Contains("dõi")    => new SolidColorBrush(WpfColor.FromRgb(0xD9, 0x77, 0x06)),
                    var r when r.Contains("Monitor")=> new SolidColorBrush(WpfColor.FromRgb(0xD9, 0x77, 0x06)),
                    _                               => new SolidColorBrush(WpfColor.FromRgb(0x16, 0xA3, 0x4A))
                };

                var abnormalPredictions = res.AnomalyPredictions
                    .Where(p => !p.IsNormal)
                    .OrderByDescending(p => p.Confidence)
                    .ToList();

                T2SyndromeText.Text = abnormalPredictions.Count > 0
                    ? string.Join("\n• ", abnormalPredictions.Select(p =>
                        $"NST #{p.Index:D2}: {p.Label} ({p.Confidence:P1})"))
                    : "Không phát hiện hội chứng đặc trưng";

                T2SexText.Text  = $"{res.SexEstimation} (Độ tin cậy: {res.SexConfidence})";
                T2ReportText.Text = res.ReportPlain;
                T2BtnSave.IsEnabled = true;
                T2BtnCSV.IsEnabled  = true;

                bool ok = abnormalPredictions.Count == 0;
                SetStatusT2(ok ? "Phân tích hoàn tất ✓" : "Phát hiện bất thường ⚠",
                    ok ? "#15803D" : "#DC2626",
                    ok ? "#DCFCE7" : "#FFF0F0",
                    ok ? "#86EFAC" : "#FCA5A5");

                // Hiển thị tab "Xác định bất thường" sau khi phân tích lần đầu tiên
                if (Tab2Btn.Visibility == Visibility.Collapsed)
                {
                    Tab2Btn.Visibility = Visibility.Visible;
                }

                AnalysisDone?.Invoke(
                    System.IO.Path.GetFileName(_st2.ImagePath ?? ""),
                    res.ChromosomeCount, ok,
                    ok ? "Bình thường" : res.RiskLevel);
            }
            catch (Exception ex) { OnFailed(ex.Message); }
            finally
            {
                StopAnim();
                T2Progress.Visibility = Visibility.Collapsed;
                T2BtnRun.IsEnabled    = true;
            }
        }

        private void SetStatusT2(string text, string fg, string bg, string border)
            => SetStatus(T2StatusText, T2StatusBadge, text, fg, bg, border);

        // ══════════════════════════════════════════════════════════════════════
        // SHARED HELPERS
        // ══════════════════════════════════════════════════════════════════════

        private void LoadImageForTab(int tabIdx)
        {
            var dlg = new WinOpenFile { Title = "Chọn ảnh NST", Filter = ImageFilter };
            if (dlg.ShowDialog() != true) return;
            ApplyImage(tabIdx, dlg.FileName);
        }

        private void DropImageForTab(int tabIdx, WinDragArgs e)
        {
            if (!e.Data.GetDataPresent(WinDataFmt.FileDrop)) return;
            var files = (string[])e.Data.GetData(WinDataFmt.FileDrop);
            if (files?.Length > 0) ApplyImage(tabIdx, files[0]);
        }

        private void ApplyImage(int tabIdx, string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();

                var st = GetState(tabIdx);
                st.ImagePath   = path;
                st.ReportPlain = null;
                st.ResultImage = null;
                st.LastResult  = null;

                switch (tabIdx)
                {
                    case 0:
                        _segmentationForAnomaly = null;
                        T0ImgOriginal.Source           = bmp;
                        T0ImgResult.Source             = null;
                        T0PlaceholderOrig.Visibility   = Visibility.Collapsed;
                        T0PlaceholderResult.Visibility = Visibility.Visible;
                        T0CountText.Text               = "Số NST: —";
                        T0ReportText.Text              = "Nhấn 'Chạy AI phân tích' để xem kết quả.";
                        T0BtnRun.IsEnabled             = true;
                        T0BtnReset.IsEnabled           = true;
                        T0BtnSave.IsEnabled            = false;
                        T0BtnCSV.IsEnabled             = false;
                        T0BtnAnomaly.IsEnabled         = false;
                        SetStatusT0("Ảnh đã tải", "#15803D", "#DCFCE7", "#86EFAC");
                        ClearDenverGrid();
                        break;
                    case 1:
                        T1ImgOriginal.Source           = bmp;
                        T1ImgResult.Source             = null;
                        T1PlaceholderOrig.Visibility   = Visibility.Collapsed;
                        T1PlaceholderResult.Visibility = Visibility.Visible;
                        T1OverlapCount.Text            = "NST chồng lấn: —";
                        T1TotalDetected.Text           = "—";
                        T1OverlapPairs.Text            = "—";
                        T1ReportText.Text              = "Nhấn 'Phân tích chồng lấn' để xem kết quả.";
                        T1BtnRun.IsEnabled             = true;
                        T1BtnReset.IsEnabled           = true;
                        T1BtnSave.IsEnabled            = false;
                        SetStatusT1("Ảnh đã tải", "#15803D", "#DCFCE7", "#86EFAC");
                        break;
                    case 2:
                        T2ImgOriginal.Source           = bmp;
                        T2ImgResult.Source             = null;
                        T2PlaceholderOrig.Visibility   = Visibility.Collapsed;
                        T2PlaceholderResult.Visibility = Visibility.Visible;
                        T2RiskText.Text                = "—";
                        T2SyndromeText.Text            = "—";
                        T2SexText.Text                 = "—";
                        T2ReportText.Text              = "Nhấn 'Phân tích bất thường' để xem kết quả.";
                        T2BtnRun.IsEnabled             = true;
                        T2BtnReset.IsEnabled           = true;
                        T2BtnSave.IsEnabled            = false;
                        T2BtnCSV.IsEnabled             = false;
                        SetStatusT2("Ảnh đã tải", "#15803D", "#DCFCE7", "#86EFAC");
                        break;
                }
            }
            catch (Exception ex)
            {
                WinMsgBox.Show($"Không đọc được ảnh:\n{ex.Message}", "Lỗi", WinMsgBtn.OK, WinMsgImg.Error);
            }
        }

        /// <summary>Nếu tab active chưa có model, prompt lại trước khi run.</summary>
        private void EnsureModelForActiveTab()
        {
            var st = GetState(_activeTab);
            if (!string.IsNullOrEmpty(st.ModelPath)) return;
            // demo mode — model null, InferenceService tự gen demo boxes
        }

        private TabState GetState(int idx) => idx switch { 0 => _st0, 1 => _st1, _ => _st2 };

        private ModelConfig BuildConfig(string? segPath, string? overlapPath, string? anomalyPath)
            => new()
            {
                SegmentationModelPath = segPath,
                OverlapModelPath      = overlapPath,
                AnomalyModelPath      = anomalyPath,
                NormalChromosomeCount = _sharedConfig.NormalChromosomeCount,
                Tolerance             = _sharedConfig.Tolerance,
            };

        private void SyncConfigFromStates()
        {
            _sharedConfig.SegmentationModelPath = _st0.ModelPath;
            _sharedConfig.OverlapModelPath       = _st1.ModelPath;
            _sharedConfig.AnomalyModelPath       = _st2.ModelPath;
        }

        // ── Settings dialog ───────────────────────────────────────────────────
        private void BtnSettings_Click(object s, RoutedEventArgs e)
        {
            SyncConfigFromStates();
            var dlg = new SettingsDialog(_sharedConfig) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _sharedConfig        = dlg.UpdatedConfig;
                _st0.ModelPath       = _sharedConfig.SegmentationModelPath;
                _st1.ModelPath       = _sharedConfig.OverlapModelPath;
                _st2.ModelPath       = _sharedConfig.AnomalyModelPath;
                RefreshTabUI();
            }
        }

        // ── Save & Export ─────────────────────────────────────────────────────
        private void SaveResult(TabState st, string prefix)
        {
            if (st.ReportPlain == null) return;
            var dlg = new System.Windows.Forms.FolderBrowserDialog
                { Description = "Chọn thư mục lưu kết quả" };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            try
            {
                var folder = dlg.SelectedPath;
                var stem   = System.IO.Path.GetFileNameWithoutExtension(st.ImagePath ?? "anh");
                var ts     = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                if (st.ResultImage != null)
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(st.ResultImage));
                    using var fs = new FileStream(
                        System.IO.Path.Combine(folder, $"ket_qua_{prefix}_{stem}_{ts}.png"),
                        FileMode.Create);
                    enc.Save(fs);
                }

                File.WriteAllLines(
                    System.IO.Path.Combine(folder, $"bao_cao_{prefix}_{stem}_{ts}.txt"),
                    new[]
                    {
                        "BÁO CÁO PHÂN TÍCH NST (MedVision AI)",
                        new string('=', 40),
                        $"Thời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                        $"Ảnh gốc: {System.IO.Path.GetFileName(st.ImagePath ?? "")}",
                        "",
                        st.ReportPlain,
                        "",
                        new string('-', 40),
                        "Lưu ý: Kết quả hỗ trợ chẩn đoán, không thay thế xét nghiệm chuyên sâu."
                    }, System.Text.Encoding.UTF8);

                WinMsgBox.Show($"Đã lưu thành công tại:\n{folder}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Information);
            }
            catch (Exception ex)
            {
                WinMsgBox.Show($"Lỗi lưu file:\n{ex.Message}", "Lỗi", WinMsgBtn.OK, WinMsgImg.Error);
            }
        }

        private void ExportCSV(TabState st)
        {
            if (st.LastResult == null) return;
            var dlg = new WinSaveFile
            {
                Title    = "Xuất báo cáo CSV",
                Filter   = "CSV (*.csv)|*.csv",
                FileName = $"Bao_cao_NST_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                File.WriteAllLines(dlg.FileName, new[]
                {
                    "Thời gian phân tích,Tên file ảnh,Số lượng NST,Mức rủi ro,Kết luận",
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                    $"{System.IO.Path.GetFileName(st.ImagePath ?? "")}," +
                    $"{st.LastResult.ChromosomeCount}," +
                    $"{st.LastResult.RiskLevel}," +
                    $"\"{st.ReportPlain?.Replace("\"", "'") ?? ""}\""
                }, System.Text.Encoding.UTF8);

                WinMsgBox.Show($"Đã xuất CSV:\n{dlg.FileName}",
                    "MedVision AI", WinMsgBtn.OK, WinMsgImg.Information);
            }
            catch (Exception ex)
            {
                WinMsgBox.Show($"Lỗi xuất CSV:\n{ex.Message}", "Lỗi", WinMsgBtn.OK, WinMsgImg.Error);
            }
        }

        // ── Denver grid (Tab0) ────────────────────────────────────────────────
        private void BuildDenverGrid()
        {
            T0DenverGrid.Children.Clear();
            foreach (var g in new[] { "A","B","C","D","E","F","G" })
            {
                var cell = new StackPanel { Margin = new Thickness(2, 0, 2, 0) };
                cell.Children.Add(new TextBlock
                {
                    Text = g, FontSize = 10, FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(0x71, 0x32, 0xF5)),
                    HorizontalAlignment = WpfHA.Center
                });
                cell.Children.Add(new TextBlock
                {
                    Text = "—", FontSize = 12, FontWeight = FontWeights.Black,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(0x10, 0x11, 0x14)),
                    HorizontalAlignment = WpfHA.Center,
                    Tag = $"D_{g}"
                });
                T0DenverGrid.Children.Add(cell);
            }
        }

        private void UpdateDenverGrid(Dictionary<string, int> groups)
        {
            foreach (StackPanel cell in T0DenverGrid.Children)
            {
                foreach (var child in cell.Children)
                {
                    if (child is TextBlock tb && tb.Tag is string tag && tag.StartsWith("D_"))
                    {
                        var g = tag.Substring(2);
                        tb.Text = groups.TryGetValue(g, out var cnt) ? cnt.ToString() : "0";
                    }
                }
            }
        }

        private void ClearDenverGrid()
        {
            foreach (StackPanel cell in T0DenverGrid.Children)
                foreach (var child in cell.Children)
                    if (child is TextBlock tb && tb.Tag is string tag && tag.StartsWith("D_"))
                        tb.Text = "—";
        }

        // ── Overlap calculation ───────────────────────────────────────────────
        private static int CountOverlappingBoxes(List<float[]> boxes)
        {
            var flagged = new HashSet<int>();
            for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                if (IoU(boxes[i], boxes[j]) > 0.05f)
                {
                    flagged.Add(i);
                    flagged.Add(j);
                }
            }
            return flagged.Count;
        }

        private static float IoU(float[] a, float[] b)
        {
            float ix1 = Math.Max(a[0], b[0]), iy1 = Math.Max(a[1], b[1]);
            float ix2 = Math.Min(a[2], b[2]), iy2 = Math.Min(a[3], b[3]);
            float inter = Math.Max(0, ix2-ix1) * Math.Max(0, iy2-iy1);
            float aA = (a[2]-a[0])*(a[3]-a[1]);
            float aB = (b[2]-b[0])*(b[3]-b[1]);
            return inter / (aA + aB - inter + 1e-6f);
        }

        private static string BuildOverlapReport(AnalysisResult res, int overlapCount, int pairs)
        {
            return string.Join("\n", new[]
            {
                $"1. Tổng NST phát hiện: {res.ChromosomeCount}",
                $"2. NST có dấu hiệu chồng lấn: {overlapCount}",
                $"3. Số cặp overlap ước tính: {pairs}",
                $"4. Tỉ lệ chồng lấn: {(res.ChromosomeCount > 0 ? (overlapCount * 100f / res.ChromosomeCount):0):F1}%",
                "",
                "Thống kê diện tích NST:",
                $"  Trung bình: {res.MeanArea:F3}",
                $"  Độ lệch chuẩn: {res.StdArea:F3}",
                $"  Hệ số biến thiên: {res.CvPercent:F1}%",
                "",
                overlapCount == 0
                    ? "✓ Không phát hiện NST chồng lấn đáng kể."
                    : $"⚠ Phát hiện {overlapCount} NST có thể bị chồng lấn — khuyến nghị xem xét thủ công.",
                "",
                "⚠ Kết quả AI hỗ trợ chẩn đoán — cần xét nghiệm chuyên sâu để xác nhận.",
            });
        }

        // ── Error handler ─────────────────────────────────────────────────────
        private void OnFailed(string msg)
        {
            WinMsgBox.Show($"Lỗi phân tích:\n{msg}", "MedVision AI", WinMsgBtn.OK, WinMsgImg.Error);
        }

        // ── Status helper ─────────────────────────────────────────────────────
        private static void SetStatus(TextBlock txt, Border badge,
            string text, string fg, string bg, string border)
        {
            txt.Text = text;
            txt.Foreground = new SolidColorBrush(
                (WpfColor)WpfColorConv.ConvertFromString(fg)!);
            badge.Background = new SolidColorBrush(
                (WpfColor)WpfColorConv.ConvertFromString(bg)!);
            badge.BorderBrush = new SolidColorBrush(
                (WpfColor)WpfColorConv.ConvertFromString(border)!);
        }

        // ── Loading animation ─────────────────────────────────────────────────
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

        private void StopAnim() { _animTimer?.Stop(); _animTimer = null; }

        // ── Browse model ──────────────────────────────────────────────────────
        private static string? BrowseModel(string title)
        {
            var dlg = new WinOpenFile { Title = title, Filter = ModelFilter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        // ── Bitmap helper ─────────────────────────────────────────────────────
        private static BitmapImage BytesToBitmap(byte[] bytes)
        {
            var bmp = new BitmapImage();
            using var ms = new System.IO.MemoryStream(bytes);
            bmp.BeginInit();
            bmp.StreamSource  = ms;
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ══════════════════════════════════════════════════════════════════════
        // WINDOW CHROME
        // ══════════════════════════════════════════════════════════════════════

        private void TopBar_MouseDown(object s, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal)
                DragMove();
        }

        private void BtnMinimize_Click(object s, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaxRestore_Click(object s, RoutedEventArgs e)
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

        private bool _closedHandled = false;

        private void BtnClose_Click(object s, RoutedEventArgs e) => Close();

        private void BtnHome_Click(object s, RoutedEventArgs e)
        {
            // Chỉ Hide — KHÔNG Close — để giữ toàn bộ state (ảnh, model, kết quả)
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

        /// <summary>Gọi khi HomeWindow show lại NSTWindow đã có sẵn.</summary>
        public void ReActivate()
        {
            if (!_clock.IsEnabled) _clock.Start();
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }
    }
}
