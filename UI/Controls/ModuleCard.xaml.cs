using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;

namespace MedVisionAI.UI.Controls
{
    public partial class ModuleCard : UserControl
    {
        public static readonly DependencyProperty ModuleTitleProperty =
            DependencyProperty.Register("ModuleTitle", typeof(string), typeof(ModuleCard),
                new PropertyMetadata("", OnPropsChanged));

        public static readonly DependencyProperty ModuleDescProperty =
            DependencyProperty.Register("ModuleDesc", typeof(string), typeof(ModuleCard),
                new PropertyMetadata("", OnPropsChanged));

        public static readonly DependencyProperty ModuleIconProperty =
            DependencyProperty.Register("ModuleIcon", typeof(string), typeof(ModuleCard),
                new PropertyMetadata("🧬", OnPropsChanged));

        public static readonly DependencyProperty AccentColorProperty =
            DependencyProperty.Register("AccentColor", typeof(string), typeof(ModuleCard),
                new PropertyMetadata("#7132F5", OnPropsChanged));

        public static readonly DependencyProperty AccentLightColorProperty =
            DependencyProperty.Register("AccentLightColor", typeof(string), typeof(ModuleCard),
                new PropertyMetadata("#F3EEFF", OnPropsChanged));

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register("IsActive", typeof(bool), typeof(ModuleCard),
                new PropertyMetadata(true, OnPropsChanged));

        public string ModuleTitle
        {
            get => (string)GetValue(ModuleTitleProperty);
            set => SetValue(ModuleTitleProperty, value);
        }
        public string ModuleDesc
        {
            get => (string)GetValue(ModuleDescProperty);
            set => SetValue(ModuleDescProperty, value);
        }
        public string ModuleIcon
        {
            get => (string)GetValue(ModuleIconProperty);
            set => SetValue(ModuleIconProperty, value);
        }
        public string AccentColor
        {
            get => (string)GetValue(AccentColorProperty);
            set => SetValue(AccentColorProperty, value);
        }
        public string AccentLightColor
        {
            get => (string)GetValue(AccentLightColorProperty);
            set => SetValue(AccentLightColorProperty, value);
        }
        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public event RoutedEventHandler? CardClicked;

        public ModuleCard()
        {
            InitializeComponent();
            Loaded += (_, _) => ApplyTheme();
        }

        private static void OnPropsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ModuleCard card && card.IsLoaded)
                card.ApplyTheme();
        }

        private void ApplyTheme()
        {
            TitleText.Text = ModuleTitle;
            DescText.Text  = ModuleDesc;
            IconText.Text  = ModuleIcon;

            var accentBrush = (SolidColorBrush)new BrushConverter().ConvertFrom(AccentColor)!;
            var lightBrush  = (SolidColorBrush)new BrushConverter().ConvertFrom(AccentLightColor)!;

            IconBubble.Background = lightBrush;

            if (IsActive)
            {
                BadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7));
                BadgeText.Text         = "● Hoạt động";
                BadgeText.Foreground   = new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D));
                Cursor = Cursors.Hand;
            }
            else
            {
                BadgeBorder.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF8));
                BadgeText.Text         = "○ Sắp ra mắt";
                BadgeText.Foreground   = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0xA8));
                Cursor = Cursors.Arrow;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (IsActive)
                CardClicked?.Invoke(this, new RoutedEventArgs());
        }
    }
}
