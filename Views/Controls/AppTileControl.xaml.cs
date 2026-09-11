using System;
using System.Windows.Input;
using System.Windows.Media.Animation;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using ScaleTransform = System.Windows.Media.ScaleTransform;

namespace AppLauncher.Views.Controls;

public partial class AppTileControl : UserControl
{
    private static readonly Color TransparentColor = Color.FromArgb(0, 255, 255, 255);
    private static readonly Color HoverBgColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
    private static readonly Color HoverBorderColor = Color.FromArgb(0x35, 0xFF, 0xFF, 0xFF);

    private readonly SolidColorBrush _bgBrush = new(TransparentColor);
    private readonly SolidColorBrush _borderBrush = new(TransparentColor);

    public AppTileControl()
    {
        InitializeComponent();
        HoverBackground.Background = _bgBrush;
        HoverBackground.BorderBrush = _borderBrush;

        MouseEnter += (s, e) => AnimateHover(true);
        MouseLeave += (s, e) => AnimateHover(false);
        PreviewMouseDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) AnimateScale(0.95, 60);
        };
        PreviewMouseUp += (s, e) =>
        {
            if (IsMouseOver) AnimateScale(1.06, 80);
            else AnimateScale(1.0, 80);
        };
    }

    private void AnimateHover(bool isHover)
    {
        var duration = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var bgAnim = new ColorAnimation
        {
            To = isHover ? HoverBgColor : TransparentColor,
            Duration = duration,
            EasingFunction = ease
        };
        _bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);

        var borderAnim = new ColorAnimation
        {
            To = isHover ? HoverBorderColor : TransparentColor,
            Duration = duration,
            EasingFunction = ease
        };
        _borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);

        AnimateScale(isHover ? 1.06 : 1.0, 120);
    }

    private void AnimateScale(double targetScale, int durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleAnimX = new DoubleAnimation { To = targetScale, Duration = duration, EasingFunction = ease };
        var scaleAnimY = new DoubleAnimation { To = targetScale, Duration = duration, EasingFunction = ease };

        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
    }
}
