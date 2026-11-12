using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TsuruNoHitokoe;

public partial class BubbleWindow : Window
{
    const double Pad = 14;        // Outer の Padding (影の余白)
    const double TailWidth = 16;

    readonly DispatcherTimer _autoHide = new();
    bool _hiding;

    public BubbleWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, noActivate: true);

        _autoHide.Tick += (_, _) => { _autoHide.Stop(); FadeOut(); };
        MouseEnter += (_, _) => _autoHide.Stop();
        MouseLeave += (_, _) => RestartTimer(TimeSpan.FromSeconds(4));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        FadeOut();
    }

    public bool IsShowing => IsVisible && !_hiding;

    /// <summary>
    /// ツルの位置に合わせて吹き出しを出す。
    /// craneCenter / radius / work はすべてスクリーン DIP 座標。
    /// </summary>
    public void Present(string text, Point craneCenter, double radius, Rect work, double scale)
    {
        BodyText.Text = text;
        ContentScale.ScaleX = ContentScale.ScaleY = scale;

        bool wasShowing = IsShowing;
        _hiding = false;
        BeginAnimation(OpacityProperty, null);
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        UpdateLayout();

        double w = ActualWidth, h = ActualHeight;
        double gap = 2;

        // 上に出せるなら上、無理なら下
        double top = craneCenter.Y - radius - gap - (h - Pad);
        bool above = top >= work.Top - Pad;
        if (!above) top = craneCenter.Y + radius + gap - Pad;

        double left = craneCenter.X - w / 2;
        left = Math.Max(work.Left - Pad + 4, Math.Min(left, work.Right - w + Pad - 4));

        Left = left;
        Top = top;

        TailTop.Visibility = above ? Visibility.Collapsed : Visibility.Visible;
        TailBottom.Visibility = above ? Visibility.Visible : Visibility.Collapsed;
        double cardWidth = Card.ActualWidth;
        double tipX = (craneCenter.X - (left + Pad)) / scale;
        double m = Math.Clamp(tipX - TailWidth / 2, 12, Math.Max(12, cardWidth - TailWidth - 12));
        TailTop.Margin = TailBottom.Margin = new Thickness(m, 0, 0, 0);

        // 登場アニメーション
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(wasShowing ? 160 : 220);
        BeginAnimation(OpacityProperty, new DoubleAnimation(wasShowing ? 0.4 : 0, 1, dur) { EasingFunction = ease });
        Slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(above ? 8 : -8, 0, dur) { EasingFunction = ease });

        // 文の長さに応じて読み終わるくらいの時間で自動で閉じる
        var secs = Math.Clamp(5 + text.Length * 0.13, 8, 24);
        RestartTimer(TimeSpan.FromSeconds(secs));
    }

    void RestartTimer(TimeSpan span)
    {
        if (!IsShowing) return;
        _autoHide.Stop();
        _autoHide.Interval = span;
        _autoHide.Start();
    }

    public void FadeOut()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        _autoHide.Stop();
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        a.Completed += (_, _) =>
        {
            if (!_hiding) return; // 途中で再表示された
            _hiding = false;
            Hide();
        };
        BeginAnimation(OpacityProperty, a);
    }
}
