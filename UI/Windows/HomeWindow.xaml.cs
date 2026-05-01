using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MedVisionAI.Models;

namespace MedVisionAI.UI.Windows
{
    public partial class HomeWindow : Window
    {
        private readonly DispatcherTimer _clock;
        private int _total, _normal, _warning;
        private readonly List<UIElement> _historyRows = new();

        public HomeWindow()
        {
            InitializeComponent();

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += (_, _) => ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy   HH:mm:ss");
            _clock.Start();
            ClockLabel.Text = DateTime.Now.ToString("dd/MM/yyyy   HH:mm:ss");
        }

        // ── Window chrome ────────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && WindowState == WindowState.Normal)
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
            => Application.Current.Shutdown();

        // ── Card hover animation ─────────────────────────────────────────────

        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not Border card) return;
            var anim = new DoubleAnimation(1.0, 1.025,
                new Duration(TimeSpan.FromMilliseconds(150)));
            if (card.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            card.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x71, 0x32, 0xF5));
            card.BorderThickness = new Thickness(2);
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not Border card) return;
            var anim = new DoubleAnimation(1.025, 1.0,
                new Duration(TimeSpan.FromMilliseconds(150)));
            if (card.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            card.BorderBrush    = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xEB, 0xEB, 0xF0));
            card.BorderThickness = new Thickness(1);
        }

        // ── Module navigation ────────────────────────────────────────────────

        private void CardNST_Click(object sender, MouseButtonEventArgs e)
            => OpenNSTModule();

        private void CardBlood_Click(object sender, MouseButtonEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Module Ung thư tế bào máu sẽ được hoàn thiện sau.\nTập trung vào NST trước.",
                "MedVision AI", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void CardMalaria_Click(object sender, MouseButtonEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Module Sốt rét sẽ được hoàn thiện sau.\nTập trung vào NST trước.",
                "MedVision AI", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        private void OpenNSTModule()
        {
            var selectDialog = new ModelSelectDialog { Owner = this };
            if (selectDialog.ShowDialog() != true) return;

            var config    = selectDialog.SelectedConfig;
            var nstWindow = new NSTWindow(config, BackToHome);
            nstWindow.AnalysisDone += OnAnalysisDone;
            Hide();
            nstWindow.Show();
        }

        // ── Called from child windows ────────────────────────────────────────

        public void BackToHome()
        {
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }

        private void OnAnalysisDone(string filename, int count, bool isNormal, string result)
        {
            _total++;
            if (isNormal) _normal++; else _warning++;

            StatTotal.Text   = _total.ToString();
            StatNormal.Text  = _normal.ToString();
            StatWarning.Text = _warning.ToString();

            HistoryEmpty.Visibility = Visibility.Collapsed;

            // Build history row
            var row = new Grid { Height = 44 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dotColor    = isNormal ? "#16A34A" : "#D97706";
            var resultColor = isNormal ? "#15803D" : "#B45309";

            void AddTb(int col, string text, string color,
                       double size = 12, bool bold = false, bool trim = false)
            {
                var tb = new TextBlock
                {
                    Text                = text,
                    FontSize            = size,
                    Foreground          = new SolidColorBrush(
                        (System.Windows.Media.Color)ColorConverter.ConvertFromString(color)!),
                    FontWeight          = bold ? FontWeights.Bold : FontWeights.Normal,
                    VerticalAlignment   = VerticalAlignment.Center,
                    Margin              = new Thickness(col == 2 ? 8 : 0, 0, col == 2 ? 8 : 0, 0),
                    TextTrimming        = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
                };
                Grid.SetColumn(tb, col);
                row.Children.Add(tb);
            }

            AddTb(0, "●", dotColor, 9);
            AddTb(1, filename, "#101114", 12, false, true);
            AddTb(2, $"{count} NST", "#9090A8", 11);
            AddTb(3, result, resultColor, 11, true);

            // Separator line
            row.Children.Add(new Border
            {
                BorderBrush     = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF8)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                VerticalAlignment = VerticalAlignment.Bottom,
            });

            // Insert at top, max 6 rows
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
