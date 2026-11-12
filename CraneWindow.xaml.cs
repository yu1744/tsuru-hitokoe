using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TsuruNoHitokoe;

public partial class CraneWindow : Window
{
    public event EventHandler? Clicked;
    public event EventHandler? RightClicked;
    public event EventHandler? Moved;

    public CraneWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle, noActivate: false);
        Loaded += (_, _) => StartIdle();
        Root.MouseEnter += (_, _) => AnimateHover(1.07);
        Root.MouseLeave += (_, _) => AnimateHover(1.0);
    }

    public double Scale
    {
        get => RootScale.ScaleX;
        set { RootScale.ScaleX = value; RootScale.ScaleY = value; }
    }

    /// <summary>ツルの中心 (スクリーン DIP 座標)</summary>
    public Point Center => new(Left + ActualWidth / 2, Top + ActualHeight / 2);

    /// <summary>中心からツルの頭/足先までの距離 (DIP)。吹き出しのしっぽ位置に使う。</summary>
    public double DiscRadius => (100 / 2.0 - 4) * Scale;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        double l = Left, t = Top;
        try { DragMove(); } catch (InvalidOperationException) { }
        if (Math.Abs(Left - l) < 3 && Math.Abs(Top - t) < 3)
        {
            Left = l; Top = t;
            Clicked?.Invoke(this, EventArgs.Empty);
        }
        else Moved?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        RightClicked?.Invoke(this, EventArgs.Empty);
    }

    void StartIdle()
    {
        var bob = new DoubleAnimation(0, -2, TimeSpan.FromSeconds(2.4))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Bob.BeginAnimation(TranslateTransform.YProperty, bob);
    }

    void AnimateHover(double to)
    {
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase() };
        HoverScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        HoverScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>話すときの小さな跳ね上がり + 首かしげ</summary>
    public void Hop()
    {
        var hop = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-7, KeyTime.FromPercent(0.35), new CubicEase { EasingMode = EasingMode.EaseOut }));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new BounceEase { Bounces = 1, Bounciness = 3, EasingMode = EasingMode.EaseOut }));
        HopT.BeginAnimation(TranslateTransform.YProperty, hop);

        var tilt = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(560) };
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(0.25), new SineEase()));
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(0.6), new SineEase()));
        tilt.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), new SineEase()));
        Tilt.BeginAnimation(RotateTransform.AngleProperty, tilt);
    }
}
