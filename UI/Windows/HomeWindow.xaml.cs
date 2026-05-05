using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MedVisionAI.Models;

// Alias rõ ràng tránh xung đột System.Windows.Forms vs System.Windows.Input
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseBtnArgs  = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseBtn      = System.Windows.Input.MouseButton;
using WpfColor         = System.Windows.Media.Color;
using WpfColorConv     = System.Windows.Media.ColorConverter;

namespace MedVisionAI.UI.Windows
{
    public partial class HomeWindow : Window
    {
        private readonly DispatcherTimer _clock;
        private int _total, _normal, _warning;
        private readonly List<UIElement> _historyRows = new();

        // ── Giữ instance NSTWindow trong suốt phiên ──────────────────────────
        private NSTWindow? _nstWindow;
        private MedicalClassifierWindow? _bloodWindow;
        private MedicalClassifierWindow? _malariaWindow;

        public HomeWindow()
        {
            InitializeComponent();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
                ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss");
        }

        // ── Window chrome ─────────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, WpfMouseBtnArgs e)
        {
            if (e.ChangedButton == WpfMouseBtn.Left && WindowState == WindowState.Normal)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                BtnMaximize.Content = "□";
            }
            else
            {
                WindowState = WindowState.Maximized;
                BtnMaximize.Content = "❐";
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _clock.Stop();
            System.Windows.Application.Current.Shutdown();
        }

        // ── Card hover animation ──────────────────────────────────────────────

        private void Card_MouseEnter(object sender, WpfMouseEventArgs e)
        {
            if (sender is not Border card) return;
            card.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(0x71, 0x32, 0xF5));
            card.BorderThickness = new Thickness(2);
        }

        private void Card_MouseLeave(object sender, WpfMouseEventArgs e)
        {
            if (sender is not Border card) return;
            card.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(0xEB, 0xEB, 0xF0));
            card.BorderThickness = new Thickness(1);
        }

        // ── Module navigation ─────────────────────────────────────────────────

        private void CardNST_Click(object sender, WpfMouseBtnArgs e)
            => OpenNSTModule();

        private void CardBlood_Click(object sender, WpfMouseBtnArgs e)
            => OpenClassifierModule(MedicalClassifierKind.BloodCancer);

        private void CardMalaria_Click(object sender, WpfMouseBtnArgs e)
            => OpenClassifierModule(MedicalClassifierKind.Malaria);

        private void OpenNSTModule()
        {
            // Nếu đã có instance còn sống → dùng lại (giữ toàn bộ state/model/ảnh)
            if (_nstWindow != null && _nstWindow.IsLoaded)
            {
                Hide();
                _nstWindow.ReActivate();
                return;
            }

            // Lần đầu hoặc window đã bị đóng hẳn → hỏi chọn model
            var dlg = new ModelSelectDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            _nstWindow = new NSTWindow(dlg.SelectedConfig, BackToHome);
            _nstWindow.AnalysisDone += OnAnalysisDone;

            Hide();
            _nstWindow.Show();
        }

        private void OpenClassifierModule(MedicalClassifierKind kind)
        {
            var existing = kind == MedicalClassifierKind.BloodCancer
                ? _bloodWindow
                : _malariaWindow;

            if (existing != null && existing.IsLoaded)
            {
                Hide();
                existing.ReActivate();
                return;
            }

            var window = new MedicalClassifierWindow(kind, BackToHome);
            window.AnalysisDone += OnClassifierAnalysisDone;

            if (kind == MedicalClassifierKind.BloodCancer)
                _bloodWindow = window;
            else
                _malariaWindow = window;

            Hide();
            window.Show();
        }

        public void BackToHome()
        {
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }

        // ── Stats & history ───────────────────────────────────────────────────

        private void OnAnalysisDone(string filename, int count, bool isNormal, string result)
            => AddAnalysisHistory(filename, $"{count} NST", isNormal, result);

        private void OnClassifierAnalysisDone(
            string filename, string label, bool isNormal, string result)
            => AddAnalysisHistory(filename, label, isNormal, result);

        private void AddAnalysisHistory(
            string filename, string detailText, bool isNormal, string result)
        {
            _total++;
            if (isNormal) _normal++; else _warning++;

            StatTotal.Text   = _total.ToString();
            StatNormal.Text  = _normal.ToString();
            StatWarning.Text = _warning.ToString();

            HistoryEmpty.Visibility = Visibility.Collapsed;

            // Build row
            var row = new Grid { Height = 44 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string dotColor    = isNormal ? "#16A34A" : "#D97706";
            string resultColor = isNormal ? "#15803D" : "#B45309";

            TextBlock Tb(int col, string text, string hex, double sz = 12,
                bool bold = false, bool trim = false)
            {
                var tb = new TextBlock
                {
                    Text          = text,
                    FontSize      = sz,
                    Foreground    = new SolidColorBrush(
                        (WpfColor)WpfColorConv.ConvertFromString(hex)!),
                    FontWeight    = bold ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin        = new Thickness(col == 2 ? 8 : 0, 0,
                                                  col == 2 ? 8 : 0, 0),
                    TextTrimming  = trim
                        ? System.Windows.TextTrimming.CharacterEllipsis
                        : System.Windows.TextTrimming.None,
                };
                Grid.SetColumn(tb, col);
                return tb;
            }

            row.Children.Add(Tb(0, "●",       dotColor,    9));
            row.Children.Add(Tb(1, filename,   "#101114",  12, false, true));
            row.Children.Add(Tb(2, detailText, "#9090A8", 11));
            row.Children.Add(Tb(3, result,     resultColor, 11, true));

            // Separator bottom border
            var sep = new Border
            {
                BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF0, 0xF8)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            Grid.SetColumnSpan(sep, 4);
            row.Children.Add(sep);

            HistoryPanel.Children.Insert(0, row);
            _historyRows.Insert(0, row);

            while (_historyRows.Count > 6)
            {
                var old = _historyRows[^1];
                HistoryPanel.Children.Remove(old);
                _historyRows.RemoveAt(_historyRows.Count - 1);
            }
        }
    }
}
