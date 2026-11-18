using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TsuruNoHitokoe;

/// <summary>一声帳の名前を入れる小さな画面 (作る・名前を変える)</summary>
public sealed class BookNameDialog : Window
{
    readonly TextBox _name;
    readonly CheckBox? _copy;
    readonly TextBlock _error;
    readonly string? _except;

    public string BookName => _name.Text.Trim();
    public bool CopyCurrent => _copy?.IsChecked == true;

    /// <param name="copyLabel">null でなければ「今の一声帳をコピーする」チェックを出す</param>
    /// <param name="except">名前を変えるときの今の名前</param>
    public BookNameDialog(Window owner, string title, string initial, string? copyLabel = null, string? except = null)
    {
        Owner = owner;
        Title = title;
        _except = except;
        SizeToContent = SizeToContent.Height;
        Width = 380;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xEE, 0xE2));
        FontFamily = new FontFamily("Yu Gothic UI");
        FontSize = 13;

        _error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x38, 0x2A)),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(new TextBlock { Text = "一声帳の名前", Margin = new Thickness(0, 0, 0, 6) });

        _name = new TextBox { Text = initial, Padding = new Thickness(6, 5, 6, 5) };
        _name.TextChanged += (_, _) => _error.Visibility = Visibility.Collapsed;
        panel.Children.Add(_name);

        if (copyLabel != null)
        {
            _copy = new CheckBox { Content = copyLabel, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(_copy);
        }

        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button { Content = "OK", MinWidth = 80, Padding = new Thickness(10, 4, 10, 4), IsDefault = true };
        var cancel = new Button { Content = "やめる", MinWidth = 80, Padding = new Thickness(10, 4, 10, 4), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    void Accept()
    {
        if (TriviaDeck.ValidateName(BookName, _except) is string err)
        {
            _error.Text = err;
            _error.Visibility = Visibility.Visible;
            _name.Focus();
            return;
        }
        DialogResult = true;
    }
}
