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
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => Save()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.S, ModifierKeys.Control));

        Loaded += (_, _) => NewText.Focus();
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
        StatusText.Text = dirty ? "未保存の変更があります" : (TriviaDeck.IsDefault ? "最初の状態です" : "");
        Title = (dirty ? "● " : "") + "一声帳 ― ツルの一声";
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
        if (!_dirty) return;
        var r = MessageBox.Show(this, "変更が保存されていません。保存しますか?", "一声帳",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel || (r == MessageBoxResult.Yes && !Save()))
            e.Cancel = true;
    }
}
