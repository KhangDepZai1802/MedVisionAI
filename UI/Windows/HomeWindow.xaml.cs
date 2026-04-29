using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MedVisionAI.Models;
using Color          = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace MedVisionAI.UI.Windows
{
    public partial class HomeWindow : Window
    {
        private readonly DispatcherTimer _clock;
        private int _totalAnalyzed = 0;
        private int _totalNormal   = 0;
        private int _totalWarning  = 0;

        public HomeWindow()
        {
            InitializeComponent();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) =>
                ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss  |  dddd");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy  |  HH:mm:ss  |  dddd");
        }

        // ── Window chrome ─────────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
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
            Close();
        }

        // ── Card hover animations ─────────────────────────────────────────────

        private void Card_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border card)
            {
                var st = (System.Windows.Media.ScaleTransform)card.RenderTransform;
                st.ScaleX = 1.012;
                st.ScaleY = 1.012;
                card.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#7132F5"));
                card.BorderThickness = new Thickness(2);
            }
        }

        private void Card_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is System.Windows.Controls.Border card)
            {
                var st = (System.Windows.Media.ScaleTransform)card.RenderTransform;
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
                card.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#EBEBF0"));
                card.BorderThickness = new Thickness(1);
            }
        }

        // ── Module navigation ─────────────────────────────────────────────────

        private void CardNST_Click(object sender, MouseButtonEventArgs e)
        {
            var dlg = new ModelSelectDialog { Owner = this };
            if (dlg.ShowDialog() != true) return;

            var win = new NSTWindow(dlg.SelectedConfig, () => { });
            win.AnalysisDone += OnAnalysisDone;
            win.Show();
        }

        private void CardBlood_Click(object sender, MouseButtonEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Module Phát hiện ung thư tế bào máu đang được phát triển.",
                "MedVision AI", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CardMalaria_Click(object sender, MouseButtonEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Module Phát hiện ký sinh trùng sốt rét đang được phát triển.",
                "MedVision AI", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Stats & history update ────────────────────────────────────────────

        private void OnAnalysisDone(string fileName, int count, bool isNormal, string result)
        {
            Dispatcher.Invoke(() =>
            {
                _totalAnalyzed++;
                if (isNormal) _totalNormal++;
                else _totalWarning++;

                StatTotal.Text   = _totalAnalyzed.ToString();
                StatNormal.Text  = _totalNormal.ToString();
                StatWarning.Text = _totalWarning.ToString();

                // Add history row
                HistoryEmpty.Visibility = Visibility.Collapsed;

                var row = new System.Windows.Controls.Grid();
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

                var nameText = new System.Windows.Controls.TextBlock
                {
                    Text = fileName,
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#101114")),
                    TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                };

                var resultText = new System.Windows.Controls.TextBlock
                {
                    Text = $"{count} NST — {result}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(isNormal ? "#15803D" : "#B45309")),
                    TextAlignment = TextAlignment.Right,
                };

                System.Windows.Controls.Grid.SetColumn(nameText, 0);
                System.Windows.Controls.Grid.SetColumn(resultText, 1);
                row.Children.Add(nameText);
                row.Children.Add(resultText);
                row.Margin = new Thickness(0, 4, 0, 0);

                HistoryPanel.Children.Add(row);
            });
        }
    }
}