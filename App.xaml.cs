using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TsuruNoHitokoe;

public partial class App : Application
{
    Mutex? _mutex;
    Settings _settings = new();
    TriviaDeck _deck = null!;
    CraneWindow _crane = null!;
    BubbleWindow _bubble = null!;
    WinForms.NotifyIcon _tray = null!;
    WinForms.ContextMenuStrip _menu = null!;
    readonly DispatcherTimer _autoSpeak = new();
    readonly OnThisDay _today = new();
    readonly NewsFeed _news = new();
    readonly Random _rng = new();

    static readonly (string Label, string Mode)[] ModeOptions =
        { ("雑学", "trivia"), ("今日の出来事 (Wikipedia)", "today"), ("最新ニュース", "news"), ("まぜる", "mix") };
    static readonly (string Label, string Value)[] NewsSourceOptions =
        { ("Google ニュース", NewsFeed.Google), ("NHK ニュース", NewsFeed.Nhk), ("両方", NewsFeed.Both) };

    static readonly (string Label, int Minutes)[] AutoOptions =
        { ("しない", 0), ("5 分ごと", 5), ("10 分ごと", 10), ("15 分ごと", 15), ("30 分ごと", 30), ("1 時間ごと", 60), ("3 時間ごと", 180) };

    // 時報: 15 秒ごとに時計を見て、毎時 0 分になったら一度だけ知らせる
    readonly DispatcherTimer _chime = new() { Interval = TimeSpan.FromSeconds(15) };
    DateTime _lastChimeHour = DateTime.MinValue;
    static readonly (string Label, double Scale)[] SizeOptions =
        { ("小", 0.8), ("中", 1.0), ("大", 1.3) };

    static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, @"Local\TsuruNoHitokoe.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        DispatcherUnhandledException += (_, ex) =>
        {
            ex.Handled = true;
            Debug.WriteLine(ex.Exception);
        };

        bool firstRun = !Settings.Exists;
        _settings = Settings.Load();
        _deck = new TriviaDeck();

        _crane = new CraneWindow { Scale = _settings.Scale };
        _crane.Clicked += (_, _) => Speak();
        _crane.RightClicked += (_, _) => ShowMenuAtCursor();
        _crane.Moved += (_, _) => { SavePosition(); if (_bubble.IsShowing) Speak(sameAgain: true); };
        _bubble = new BubbleWindow();

        BuildTray();
        PlaceCrane();
        if (_settings.CraneVisible) _crane.Show();

        _autoSpeak.Tick += (_, _) => { if (_crane.IsVisible && !_bubble.IsShowing) Speak(); };
        ApplyAutoSpeak();
        _lastChimeHour = TopOfHour(DateTime.Now); // 起動した時刻の分は知らせない
        _chime.Tick += (_, _) => CheckChime();
        _chime.Start();
        _news.Sources = _settings.NewsSources;
        Prefetch();

        if (firstRun)
        {
            _settings.Save();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                SayRaw("ツルの一声です。わたしをクリックすると、雑学をひとつお話しします。" +
                    "ドラッグで好きな場所へ、右クリックでメニューが開きます。"));
        }
    }

    // ───────────── 話す ─────────────

    Utterance? _current;
    bool _speaking;

    async void Speak(bool sameAgain = false)
    {
        if (!_crane.IsVisible) _crane.Show();
        if (!sameAgain || _current == null)
        {
            if (_speaking) return; // 通信中の連打を無視
            _speaking = true;
            try { _current = await NextTextAsync(); }
            finally { _speaking = false; }
            _settings.SpokenCount++;
            _settings.Save();
            _crane.Hop();
        }
        Present(_current);
    }

    /// <summary>モードに応じて次に話す文を選ぶ。ネットから取れなければ雑学にする。</summary>
    async Task<Utterance> NextTextAsync()
    {
        string kind = _settings.Mode == "mix"
            ? new[] { "trivia", "today", "news" }[_rng.Next(3)]
            : _settings.Mode;

        if (kind == "today")
        {
            await _today.EnsureLoadedAsync();
            if (_today.Next() is string s) return new Utterance(s);
        }
        else if (kind == "news")
        {
            await _news.EnsureFreshAsync();
            if (_news.Next() is Utterance u) return u;
        }
        return new Utterance(_deck.Next().Text);
    }

    static DateTime TopOfHour(DateTime t) => new(t.Year, t.Month, t.Day, t.Hour, 0, 0);

    void CheckChime()
    {
        var now = DateTime.Now;
        var hour = TopOfHour(now);
        if (hour == _lastChimeHour) return;
        _lastChimeHour = hour;
        // スリープ明けなどで 0 分を過ぎていたら、古い時報は出さない
        if (now.Minute != 0 || !_settings.HourlyChime || !_crane.IsVisible) return;
        SayRaw(ChimeText(now.Hour));
        ResetAutoSpeak(); // 時報の直後に雑学が重ならないように
    }

    static string ChimeText(int h) => h switch
    {
        0 => "午前0時になりました。日付が変わりました。",
        12 => "正午になりました。",
        < 12 => $"午前{h}時になりました。",
        _ => $"午後{h - 12}時になりました。",
    };

    void ResetAutoSpeak()
    {
        if (!_autoSpeak.IsEnabled) return;
        _autoSpeak.Stop();
        _autoSpeak.Start();
    }

    /// <summary>クリックしたときに待たずに済むよう、先に取っておく</summary>
    void Prefetch()
    {
        if (_settings.Mode is "today" or "mix") _ = _today.EnsureLoadedAsync();
        if (_settings.Mode is "news" or "mix") _ = _news.EnsureFreshAsync();
    }

    void SayRaw(string text)
    {
        _current = new Utterance(text);
        if (!_crane.IsVisible) _crane.Show();
        _crane.Hop();
        Present(_current);
    }

    void Present(Utterance u)
    {
        _bubble.Present(u, _crane.Center, _crane.DiscRadius, WorkAreaOf(_crane), _settings.Scale);
    }

    // ───────────── 位置 ─────────────

    /// <summary>ツルがいるモニターの作業領域 (DIP)</summary>
    static Rect WorkAreaOf(Window w)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        var px = new System.Drawing.Point(
            (int)((w.Left + w.ActualWidth / 2) * dpi.DpiScaleX),
            (int)((w.Top + w.ActualHeight / 2) * dpi.DpiScaleY));
        var wa = WinForms.Screen.FromPoint(px).WorkingArea;
        return new Rect(wa.Left / dpi.DpiScaleX, wa.Top / dpi.DpiScaleY, wa.Width / dpi.DpiScaleX, wa.Height / dpi.DpiScaleY);
    }

    void PlaceCrane()
    {
        double size = 96 * _settings.Scale;
        var vs = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                          SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_settings.Left is double l && _settings.Top is double t &&
            vs.Contains(new Point(l + size / 2, t + size / 2)))
        {
            _crane.Left = l;
            _crane.Top = t;
        }
        else
        {
            var wa = SystemParameters.WorkArea;
            _crane.Left = wa.Right - size - 24;
            _crane.Top = wa.Bottom - size - 24;
        }
    }

    void SavePosition()
    {
        _settings.Left = _crane.Left;
        _settings.Top = _crane.Top;
        _settings.Save();
    }

    // ───────────── 設定 ─────────────

    void ApplyAutoSpeak()
    {
        _autoSpeak.Stop();
        if (_settings.AutoSpeakMinutes > 0)
        {
            _autoSpeak.Interval = TimeSpan.FromMinutes(_settings.AutoSpeakMinutes);
            _autoSpeak.Start();
        }
    }

    void SetScale(double scale)
    {
        double oldSize = 96 * _settings.Scale, newSize = 96 * scale;
        // 中心を保ったまま大きさを変える
        _crane.Left += (oldSize - newSize) / 2;
        _crane.Top += (oldSize - newSize) / 2;
        _settings.Scale = scale;
        _crane.Scale = scale;
        _bubble.FadeOut();
        SavePosition();
    }

    void SetCraneVisible(bool visible)
    {
        _settings.CraneVisible = visible;
        _settings.Save();
        if (visible) _crane.Show();
        else { _bubble.FadeOut(); _crane.Hide(); }
    }

    // ───────────── メニュー / トレイ ─────────────

    void BuildTray()
    {
        _menu = new WinForms.ContextMenuStrip
        {
            Font = new System.Drawing.Font("Yu Gothic UI", 9.5f),
            ShowImageMargin = false,
            ShowCheckMargin = true,
        };
        _menu.Opening += (_, _) => RebuildMenu();
        RebuildMenu();

        System.Drawing.Icon icon;
        var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        using (var s = res.Stream) icon = new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "ツルの一声",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != WinForms.MouseButtons.Left) return;
            if (!_crane.IsVisible) SetCraneVisible(true);
            Speak();
        };
    }

    void RebuildMenu()
    {
        _menu.Items.Clear();

        var speak = new WinForms.ToolStripMenuItem("一声もらう") { Font = new System.Drawing.Font(_menu.Font, System.Drawing.FontStyle.Bold) };
        speak.Click += (_, _) => Speak();
        _menu.Items.Add(speak);
        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var mode = new WinForms.ToolStripMenuItem("話す内容");
        foreach (var (label, value) in ModeOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = _settings.Mode == value };
            item.Click += (_, _) =>
            {
                _settings.Mode = value;
                _settings.Save();
                Prefetch();
            };
            mode.DropDownItems.Add(item);
        }
        _menu.Items.Add(mode);

        var src = new WinForms.ToolStripMenuItem("ニュースの取得元");
        foreach (var (label, value) in NewsSourceOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = _settings.NewsSources == value };
            item.Click += (_, _) =>
            {
                _settings.NewsSources = value;
                _news.Sources = value;
                _settings.Save();
                Prefetch();
            };
            src.DropDownItems.Add(item);
        }
        _menu.Items.Add(src);

        var auto = new WinForms.ToolStripMenuItem("自動でつぶやく");
        foreach (var (label, minutes) in AutoOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = _settings.AutoSpeakMinutes == minutes };
            item.Click += (_, _) => { _settings.AutoSpeakMinutes = minutes; _settings.Save(); ApplyAutoSpeak(); };
            auto.DropDownItems.Add(item);
        }
        _menu.Items.Add(auto);

        var size = new WinForms.ToolStripMenuItem("ツルの大きさ");
        foreach (var (label, scale) in SizeOptions)
        {
            var item = new WinForms.ToolStripMenuItem(label) { Checked = Math.Abs(_settings.Scale - scale) < 0.01 };
            item.Click += (_, _) => SetScale(scale);
            size.DropDownItems.Add(item);
        }
        _menu.Items.Add(size);

        var chime = new WinForms.ToolStripMenuItem("毎時の時報") { Checked = _settings.HourlyChime };
        chime.Click += (_, _) => { _settings.HourlyChime = !_settings.HourlyChime; _settings.Save(); };
        _menu.Items.Add(chime);

        var hide = new WinForms.ToolStripMenuItem(_settings.CraneVisible ? "ツルを隠す" : "ツルを呼び戻す");
        hide.Click += (_, _) => SetCraneVisible(!_settings.CraneVisible);
        _menu.Items.Add(hide);

        _menu.Items.Add(new WinForms.ToolStripSeparator());

        var custom = new WinForms.ToolStripMenuItem("一声帳を開く (雑学の編集)…");
        custom.Click += (_, _) => OpenEditor();
        _menu.Items.Add(custom);

        bool startupOn = false;
        try { startupOn = AutoStart.IsEnabled; } catch { }
        var startup = new WinForms.ToolStripMenuItem("Windows 起動時に呼ぶ") { Checked = startupOn };
        startup.Click += (_, _) =>
        {
            try { AutoStart.Set(!startupOn); }
            catch (Exception ex) { SayRaw("自動起動の設定を変更できませんでした。\n" + ex.Message); }
        };
        _menu.Items.Add(startup);

        var about = new WinForms.ToolStripMenuItem("ツルの一声について");
        about.Click += (_, _) => SayRaw(
            $"ツルの一声 v{Version}\n収録している雑学は {_deck.Count} 個。これまでに {_settings.SpokenCount} 回お話ししました。\nイラスト:いらすとや\n今日の出来事:ウィキペディア日本語版 (CC BY-SA 4.0)\nニュース:Google ニュース / NHK ニュースの RSS");
        _menu.Items.Add(about);

        _menu.Items.Add(new WinForms.ToolStripSeparator());
        var exit = new WinForms.ToolStripMenuItem("終了");
        exit.Click += (_, _) => ExitApp();
        _menu.Items.Add(exit);
    }

    void ShowMenuAtCursor()
    {
        _menu.Show(WinForms.Cursor.Position);
    }

    EditorWindow? _editor;

    void OpenEditor()
    {
        if (_editor != null)
        {
            if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
            _editor.Activate();
            return;
        }
        _editor = new EditorWindow(_deck);
        _editor.Closed += (_, _) => _editor = null;
        _editor.Show();
        _editor.Activate();
    }

    void ExitApp()
    {
        SavePosition();
        _tray.Visible = false;
        _tray.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
