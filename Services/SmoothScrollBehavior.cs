using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace AppLauncher.Services;

public static class SmoothScrollBehavior
{
    public static readonly DependencyProperty IsSmoothScrollEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsSmoothScrollEnabled",
            typeof(bool),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(false, OnIsSmoothScrollEnabledChanged));

    public static bool GetIsSmoothScrollEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsSmoothScrollEnabledProperty);

    public static void SetIsSmoothScrollEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsSmoothScrollEnabledProperty, value);

    private static readonly DependencyProperty ScrollerHelperProperty =
        DependencyProperty.RegisterAttached(
            "ScrollerHelper",
            typeof(ScrollAnimationHelper),
            typeof(SmoothScrollBehavior),
            new PropertyMetadata(null));

    private static void OnIsSmoothScrollEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            if ((bool)e.NewValue)
            {
                var helper = new ScrollAnimationHelper(scrollViewer);
                scrollViewer.SetValue(ScrollerHelperProperty, helper);
            }
            else
            {
                if (scrollViewer.GetValue(ScrollerHelperProperty) is ScrollAnimationHelper helper)
                {
                    helper.Detach();
                    scrollViewer.ClearValue(ScrollerHelperProperty);
                }
            }
        }
    }

    private class ScrollAnimationHelper
    {
        private readonly ScrollViewer _scrollViewer;
        private ScrollBar? _verticalScrollBar;
        private double _targetOffset;
        private bool _isRenderingHooked;
        private readonly DispatcherTimer _hideTimer;
        private bool _isScrollBarVisible;

        public ScrollAnimationHelper(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _targetOffset = scrollViewer.VerticalOffset;
            _scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
            _scrollViewer.Loaded += OnScrollViewerLoaded;
            _scrollViewer.Unloaded += OnScrollViewerUnloaded;

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            _hideTimer.Tick += OnHideTimerTick;
        }

        private void OnScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            _scrollViewer.ApplyTemplate();
            _verticalScrollBar = _scrollViewer.Template.FindName("PART_VerticalScrollBar", _scrollViewer) as ScrollBar;

            if (_verticalScrollBar != null)
            {
                _verticalScrollBar.Opacity = 0.0;
                _verticalScrollBar.MouseEnter += (s, args) =>
                {
                    FadeInScrollBar();
                    _hideTimer.Stop();
                };

                _verticalScrollBar.MouseLeave += (s, args) =>
                {
                    _hideTimer.Stop();
                    _hideTimer.Start();
                };
            }
        }

        private void OnScrollViewerUnloaded(object sender, RoutedEventArgs e)
        {
            Detach();
        }

        public void Detach()
        {
            _scrollViewer.PreviewMouseWheel -= OnPreviewMouseWheel;
            _scrollViewer.Loaded -= OnScrollViewerLoaded;
            _scrollViewer.Unloaded -= OnScrollViewerUnloaded;
            _hideTimer.Stop();
            UnhookRendering();
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;

            var maxScroll = _scrollViewer.ScrollableHeight;
            if (maxScroll <= 0) return;

            // Trigger quick fade in of scrollbar
            FadeInScrollBar();

            // Reset auto-hide timer
            _hideTimer.Stop();
            _hideTimer.Start();

            if (!_isRenderingHooked)
            {
                _targetOffset = _scrollViewer.VerticalOffset;
            }

            var step = -e.Delta * 0.85;
            _targetOffset = Math.Clamp(_targetOffset + step, 0, maxScroll);

            HookRendering();
        }

        private void FadeInScrollBar()
        {
            if (_verticalScrollBar == null) return;
            if (!_isScrollBarVisible || _verticalScrollBar.Opacity < 0.95)
            {
                _isScrollBarVisible = true;
                var anim = new DoubleAnimation
                {
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(120),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        private void FadeOutScrollBar()
        {
            if (_verticalScrollBar == null) return;
            if (_isScrollBarVisible)
            {
                _isScrollBarVisible = false;
                var anim = new DoubleAnimation
                {
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(250),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        private void OnHideTimerTick(object? sender, EventArgs e)
        {
            _hideTimer.Stop();

            if (_verticalScrollBar != null && !_verticalScrollBar.IsMouseOver)
            {
                FadeOutScrollBar();
            }
        }

        private void HookRendering()
        {
            if (!_isRenderingHooked)
            {
                _isRenderingHooked = true;
                CompositionTarget.Rendering += OnRenderFrame;
            }
        }

        private void UnhookRendering()
        {
            if (_isRenderingHooked)
            {
                _isRenderingHooked = false;
                CompositionTarget.Rendering -= OnRenderFrame;
            }
        }

        private void OnRenderFrame(object? sender, EventArgs e)
        {
            var current = _scrollViewer.VerticalOffset;
            var diff = _targetOffset - current;

            if (Math.Abs(diff) < 0.4)
            {
                _scrollViewer.ScrollToVerticalOffset(_targetOffset);
                UnhookRendering();
                return;
            }

            // High-FPS Smooth Exponential Damping (120Hz/60Hz adaptive Lerp)
            var newOffset = current + diff * 0.24;
            _scrollViewer.ScrollToVerticalOffset(newOffset);
        }
    }
}
