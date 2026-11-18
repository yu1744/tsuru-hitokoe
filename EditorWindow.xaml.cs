using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace TsuruNoHitokoe;

/// <summary>一声帳 (雑学の一覧編集) の 1 行</summary>
public sealed class EditItem : INotifyPropertyChanged
{
    string _text;
    public EditItem(string category, string text) { Category = category; _text = text; }

    public string Category { get; }
    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; OnChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public partial class EditorWindow : Window
{
    readonly TriviaDeck _deck;
    readonly ObservableCollection<EditItem> _items = new();
    readonly ICollectionView _view;
    bool _dirty;

    public EditorWindow(TriviaDeck deck)
    {
        InitializeComponent();
        _deck = deck;

        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => o is EditItem it &&
            (SearchText.Text.Length == 0 || it.Text.Contains(SearchText.Text, StringComparison.OrdinalIgnoreCase));
        List.ItemsSource = _view;

        LoadFrom(_deck.Items.Select(t => new EditItem(t.Category, t.Text)));

        NewText.TextChanged += (_, _) => NewHint.Visibility = NewText.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NewText.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        AddButton.Click += (_, _) => Add();

        SearchText.TextChanged += (_, _) =>
        {
            SearchHint.Visibility = SearchText.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _view.Refresh();
            UpdateCount();
        };

        SaveButton.Click += (_, _) => Save();
        CloseButton.Click += (_, _) => Close();
        ResetButton.Click += (_, _) => Reset();
        var csvMenu = new ContextMenu { PlacementTarget = ImportButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var miImport = new MenuItem { Header = "CSV から読み込む…" };
        miImport.Click += (_, _) => Import();
        var miTemplate = new MenuItem { Header = "テンプレートを保存…" };
        miTemplate.Click += (_, _) => SaveTemplate();
        csvMenu.Items.Add(miImport);
        csvMenu.Items.Add(miTemplate);
        ImportButton.Click += (_, _) => csvMenu.IsOpen = true;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));

        BookBox.SelectionChanged += (_, _) =>
        {
            if (_refreshingBooks || BookBox.SelectedItem is not string name) return;
            string? target = name == TriviaDeck.MainBookName ? null : name;
            if (target != _deck.Book && !RequestSwitch(target)) RefreshBooks(); // やめたら選択を戻す
        };
        NewBookButton.Click += (_, _) => NewBook();
        RenameBookButton.Click += (_, _) => RenameBook();
        DeleteBookButton.Click += (_, _) => DeleteBook();
        _deck.Switched += OnDeckSwitched;
        Closed += (_, _) => _deck.Switched -= OnDeckSwitched;
        RefreshBooks();

        Loaded += (_, _) => NewText.Focus();
    }

    // ───────────── 一声帳の切り替え ─────────────

    bool _refreshingBooks;

    void RefreshBooks()
    {
        _refreshingBooks = true;
        BookBox.Items.Clear();
        BookBox.Items.Add(TriviaDeck.MainBookName);
        foreach (var n in TriviaDeck.ExtraBooks()) BookBox.Items.Add(n);
        BookBox.SelectedItem = _deck.BookName;
        _refreshingBooks = false;

        RenameBookButton.IsEnabled = !_deck.IsMain;
        DeleteBookButton.IsEnabled = !_deck.IsMain;
        RenameBookButton.ToolTip = _deck.IsMain ? "いつもの一声帳の名前は変えられません" : "この一声帳の名前を変えます";
        ResetButton.Visibility = _deck.IsMain ? Visibility.Visible : Visibility.Collapsed;
        SetDirty(_dirty); // タイトルを更新
    }

    bool _renaming;

    void OnDeckSwitched()
    {
        if (_renaming) return;
        SearchText.Clear();
        LoadFrom(_deck.Items.Select(t => new EditItem(t.Category, t.Text)));
        RefreshBooks();
    }

    /// <summary>未保存の変更があれば保存するか聞く。続けてよければ true</summary>
    bool ConfirmLeave()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show(this, $"「{_deck.BookName}」の変更が保存されていません。保存しますか?", "一声帳",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return Save();
        SetDirty(false); // 保存しない = 変更を捨てる
        return true;
    }

    /// <summary>使う一声帳を切り替える。やめた・できなかったら false</summary>
    public bool RequestSwitch(string? book)
    {
        if (!ConfirmLeave()) return false;
        try
        {
            _deck.SwitchTo(book);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "一声帳を切り替えられませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshBooks();
            return false;
        }
    }

    void NewBook()
    {
        if (!ConfirmLeave()) return;
        var dlg = new BookNameDialog(this, "新しい一声帳を作る", "",
            copyLabel: $"「{_deck.BookName}」の中身をコピーして始める");
        if (dlg.ShowDialog() != true) return;
        try
        {
            TriviaDeck.CreateBook(dlg.BookName, dlg.CopyCurrent ? _deck.Items : Array.Empty<Trivia>());
            _deck.SwitchTo(dlg.BookName);
            StatusText.Text = "作りました。CSV から読み込むこともできます";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "作れませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void RenameBook()
    {
        if (_deck.Book is not string current) return;
        var dlg = new BookNameDialog(this, "一声帳の名前を変える", current, except: current);
        if (dlg.ShowDialog() != true) return;
        _renaming = true; // 名前だけ変わるので、編集中の一覧は読み直さない
        try
        {
            _deck.RenameCurrent(dlg.BookName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "名前を変えられませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _renaming = false;
            RefreshBooks();
        }
    }

    void DeleteBook()
    {
        if (_deck.IsMain) return;
        var r = MessageBox.Show(this,
            $"一声帳「{_deck.BookName}」を消します。中の一声 {_deck.Count} 個も消えます。\nこの操作は元に戻せません。よろしいですか?",
            "一声帳を消す", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (r != MessageBoxResult.OK) return;
        try
        {
            _dirty = false;
            _deck.DeleteCurrent();
            StatusText.Text = "消しました。いつもの一声帳に戻りました";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "消せませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void LoadFrom(System.Collections.Generic.IEnumerable<EditItem> items)
    {
        foreach (var it in _items) it.PropertyChanged -= OnItemChanged;
        _items.Clear();
        foreach (var it in items)
        {
            it.PropertyChanged += OnItemChanged;
            _items.Add(it);
        }
        SetDirty(false);
        UpdateCount();
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e) => SetDirty(true);

    void SetDirty(bool dirty)
    {
        _dirty = dirty;
        SaveButton.IsEnabled = dirty;
        StatusText.Text = dirty ? "未保存の変更があります" : (_deck.IsDefault ? "最初の状態です" : "");
        Title = (dirty ? "● " : "") + $"一声帳「{_deck.BookName}」 ― ツルの一声";
    }

    void UpdateCount()
    {
        int shown = _view.Cast<object>().Count();
        CountText.Text = SearchText.Text.Length == 0 ? $"{_items.Count} 件" : $"{shown} / {_items.Count} 件";
        EmptyText.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_items.Count == 0) EmptyText.Text = "まだ一声がありません。上の欄から書き足してね。";
        else EmptyText.Text = "見つかりませんでした";
    }

    void Add()
    {
        var text = NewText.Text.Trim();
        if (text.Length == 0) return;
        var it = new EditItem("じぶん", text);
        it.PropertyChanged += OnItemChanged;
        _items.Insert(0, it);
        NewText.Clear();
        if (SearchText.Text.Length > 0) SearchText.Clear(); // 追加したものが見えるように
        List.ScrollIntoView(it);
        SetDirty(true);
        UpdateCount();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EditItem it) return;
        it.PropertyChanged -= OnItemChanged;
        _items.Remove(it);
        SetDirty(true);
        UpdateCount();
    }

    bool Save()
    {
        try
        {
            _deck.Save(_items.Select(i => new Trivia(i.Category, i.Text)));
            // 空の行は保存時に取り除かれるので、一覧も合わせる
            LoadFrom(_deck.Items.Select(t => new EditItem(t.Category, t.Text)));
            StatusText.Text = "保存しました ✓";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存できませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    void SaveTemplate()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "CSV のテンプレートを保存",
            FileName = "一声帳テンプレート.csv",
            Filter = "CSV ファイル (*.csv)|*.csv",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            CsvImport.WriteTemplate(dlg.FileName);
            MessageBox.Show(this,
                "テンプレートを保存しました。\n\n" +
                "・1 行目の「カテゴリ,雑学」は見出しなので、そのまま残してください\n" +
                "・2 行目からが見本です。消して、自分の一声に書き換えてください\n" +
                "・カテゴリは空でもかまいません(「じぶん」になります)\n" +
                "・Excel で開いて編集し、「CSV」形式で保存すれば読み込めます",
                "一声帳", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存できませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void Import()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "一声帳に読み込む CSV を選ぶ",
            Filter = "CSV ファイル (*.csv)|*.csv|テキスト ファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        System.Collections.Generic.List<Trivia> read;
        try { read = CsvImport.Read(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "読み込めませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (read.Count == 0)
        {
            MessageBox.Show(this, "このファイルには読み込める一声がありませんでした。", "一声帳",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var r = MessageBox.Show(this,
            $"{read.Count} 件の一声が見つかりました。\n\n" +
            "はい … 今の一声帳に書き足す(同じ文は足しません)\n" +
            "いいえ … 今の一声帳と入れ替える\n\n" +
            "どちらも「保存」を押すまでは反映されません。",
            "CSV を読み込む", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (r == MessageBoxResult.Cancel) return;

        bool replace = r == MessageBoxResult.No;
        var have = replace ? new System.Collections.Generic.HashSet<string>()
                           : _items.Select(i => i.Text.Trim()).ToHashSet();
        var fresh = read.Where(t => have.Add(t.Text)).ToList(); // ファイル内の重複も 1 つにまとめる

        if (replace)
        {
            SearchText.Clear();
            LoadFrom(fresh.Select(t => new EditItem(t.Category, t.Text)));
        }
        else
        {
            if (SearchText.Text.Length > 0) SearchText.Clear();
            foreach (var t in fresh)
            {
                var it = new EditItem(t.Category, t.Text);
                it.PropertyChanged += OnItemChanged;
                _items.Add(it);
            }
            if (fresh.Count > 0) List.ScrollIntoView(_items[^1]);
        }
        SetDirty(true);
        UpdateCount();

        int skipped = read.Count - fresh.Count;
        StatusText.Text = (replace ? $"{fresh.Count} 件に入れ替え" : $"{fresh.Count} 件を追加")
                        + (skipped > 0 ? $"(重複 {skipped} 件は省略)" : "") + "・未保存";
    }

    void Reset()
    {
        var builtInCount = TriviaDeck.LoadBuiltIn().Count;
        var r = MessageBox.Show(this,
            $"追加・編集・削除をすべて取り消して、最初から入っている {builtInCount} 個の雑学に戻します。\nこの操作は元に戻せません。よろしいですか?",
            "デフォルトに戻す", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (r != MessageBoxResult.OK) return;
        try
        {
            _deck.ResetToDefault();
            SearchText.Clear();
            LoadFrom(_deck.Items.Select(t => new EditItem(t.Category, t.Text)));
            StatusText.Text = "最初の状態に戻しました";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "戻せませんでした。\n" + ex.Message, "一声帳", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!ConfirmLeave()) e.Cancel = true;
    }
}
