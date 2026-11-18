using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TsuruNoHitokoe;

/// <param name="Since">組み込みの雑学が追加されたバージョン (最初からあるものは null)</param>
public sealed record Trivia(
    [property: JsonPropertyName("c")] string Category,
    [property: JsonPropertyName("t")] string Text,
    [property: JsonPropertyName("v")] string? Since = null);

/// <summary>
/// 雑学の山札。一巡するまで同じものは出ない。
/// 一声帳で編集した内容は %APPDATA%\TsuruNoHitokoe\trivia.json に保存し、
/// それが無ければ組み込みの雑学を使う。これを「いつもの一声帳」と呼ぶ。
///
/// ほかに名前を付けた一声帳を books\&lt;名前&gt;.json にいくつでも作れて、切り替えて使える。
/// (アップデートで増えた雑学が足されるのは、いつもの一声帳だけ)
///
/// 一声帳を保存している人にも、アップデートで増えた組み込みの雑学が届くように、
/// 「受け取り済みの組み込み雑学」を trivia_known.json に記録しておき、
/// まだ受け取っていないものだけを一声帳に足す (消した・書き換えたものは戻さない)。
/// </summary>
public sealed class TriviaDeck
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    List<Trivia> _all;
    readonly Queue<Trivia> _queue = new();
    readonly Random _rng = new();
    Trivia? _last;

    public static string UserPath => Path.Combine(Settings.Dir, "trivia.json");
    static string KnownPath => Path.Combine(Settings.Dir, "trivia_known.json");
    static string BooksDir => Path.Combine(Settings.Dir, "books");
    static string BookPath(string name) => Path.Combine(BooksDir, name + ".json");

    public const string MainBookName = "いつもの一声帳";
    const int MaxBookNameLength = 30;

    /// <param name="book">使う一声帳の名前 (null = いつもの一声帳)。読めなければいつもの一声帳を使う</param>
    public TriviaDeck(string? book = null)
    {
        if (book != null)
        {
            try { _all = LoadBook(book); Book = book; return; }
            catch { /* 消えた・壊れた一声帳なら、いつもの一声帳で起動 */ }
        }
        _all = LoadMain();
    }

    /// <summary>今使っている一声帳の名前 (null = いつもの一声帳)</summary>
    public string? Book { get; private set; }
    public string BookName => Book ?? MainBookName;
    public bool IsMain => Book == null;

    /// <summary>使う一声帳が切り替わった・名前が変わったとき</summary>
    public event Action? Switched;

    public int Count => _all.Count;
    public IReadOnlyList<Trivia> Items => _all;

    /// <summary>起動時に、アップデートで一声帳に足した雑学の数</summary>
    public int NewlyAdded { get; private set; }

    /// <summary>いつもの一声帳が組み込みのまま (一度も保存していない) か</summary>
    public bool IsDefault => IsMain && !File.Exists(UserPath);

    public Trivia Next()
    {
        if (_all.Count == 0)
            return new Trivia("", "一声帳が空っぽです。右クリック →「一声帳を開く」から書き足してね。");
        if (_queue.Count == 0) Refill();
        var t = _queue.Dequeue();
        _last = t;
        return t;
    }

    void Refill()
    {
        var shuffled = _all.OrderBy(_ => _rng.Next()).ToList();
        if (shuffled.Count > 1 && shuffled[0] == _last)
            (shuffled[0], shuffled[^1]) = (shuffled[^1], shuffled[0]);
        foreach (var t in shuffled) _queue.Enqueue(t);
    }

    /// <summary>一声帳の内容を保存して、山札を作り直す</summary>
    public void Save(IEnumerable<Trivia> items)
    {
        var list = items.Where(t => !string.IsNullOrWhiteSpace(t.Text))
                        .Select(t => t with { Text = t.Text.Trim(), Since = null })
                        .ToList();
        if (Book != null)
        {
            WriteJson(BookPath(Book), list);
        }
        else
        {
            WriteJson(UserPath, list);
            // 一声帳には今の組み込み雑学がすべて並んでいたので、全部受け取り済みにする
            WriteJson(KnownPath, LoadBuiltIn().Select(t => t.Text).Distinct().ToList());
        }
        Replace(list);
    }

    /// <summary>いつもの一声帳の編集をすべて捨てて組み込みの雑学に戻す</summary>
    public void ResetToDefault()
    {
        if (!IsMain) throw new InvalidOperationException("デフォルトに戻せるのは、いつもの一声帳だけです");
        if (File.Exists(UserPath)) File.Delete(UserPath);
        if (File.Exists(KnownPath)) File.Delete(KnownPath);
        Replace(LoadBuiltIn());
    }

    void Replace(List<Trivia> list)
    {
        _all = list;
        _queue.Clear();
        _last = null;
    }

    // ───────────── 一声帳の切り替え・作成 ─────────────

    /// <summary>名前を付けた一声帳 (いつもの一声帳以外) の一覧</summary>
    public static List<string> ExtraBooks()
    {
        if (!Directory.Exists(BooksDir)) return new();
        return Directory.GetFiles(BooksDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>一声帳の名前として使えなければ、その理由を返す</summary>
    /// <param name="except">名前を変えるときの今の名前 (同じ名前のままは許す)</param>
    public static string? ValidateName(string name, string? except = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "名前を入れてください。";
        if (name.Length > MaxBookNameLength) return $"名前は {MaxBookNameLength} 文字までにしてください。";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "名前に \\ / : * ? \" < > | は使えません。";
        if (name.EndsWith('.')) return "名前の最後に「.」は使えません。";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            return "その名前は使えません。";
        if (except != null && string.Equals(name, except, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(name, MainBookName, StringComparison.OrdinalIgnoreCase) ||
            ExtraBooks().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            return "同じ名前の一声帳がすでにあります。";
        return null;
    }

    /// <summary>使う一声帳を切り替える (null = いつもの一声帳)</summary>
    public void SwitchTo(string? book)
    {
        var list = book == null ? LoadMain() : LoadBook(book);
        Book = book;
        Replace(list);
        Switched?.Invoke();
    }

    /// <summary>新しい一声帳を作る (切り替えはしない)</summary>
    public static void CreateBook(string name, IEnumerable<Trivia> items)
    {
        name = name.Trim();
        if (ValidateName(name) is string err) throw new InvalidOperationException(err);
        WriteJson(BookPath(name), items.Select(t => t with { Since = null }).ToList());
    }

    /// <summary>今使っている一声帳の名前を変える</summary>
    public void RenameCurrent(string newName)
    {
        if (Book == null) throw new InvalidOperationException("いつもの一声帳の名前は変えられません");
        newName = newName.Trim();
        if (newName == Book) return;
        if (ValidateName(newName, except: Book) is string err) throw new InvalidOperationException(err);
        var tmp = BookPath(Book) + ".rename";
        File.Move(BookPath(Book), tmp); // 大文字・小文字だけ変えるときにも対応
        File.Move(tmp, BookPath(newName));
        Book = newName;
        Switched?.Invoke();
    }

    /// <summary>今使っている一声帳を消して、いつもの一声帳に戻る</summary>
    public void DeleteCurrent()
    {
        if (Book == null) throw new InvalidOperationException("いつもの一声帳は消せません");
        var path = BookPath(Book);
        SwitchTo(null);
        File.Delete(path);
    }

    public static List<Trivia> LoadBuiltIn()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("trivia.json")
                      ?? throw new InvalidOperationException("trivia.json が見つかりません");
        return JsonSerializer.Deserialize<List<Trivia>>(s) ?? new();
    }

    static List<Trivia> LoadBook(string name) =>
        JsonSerializer.Deserialize<List<Trivia>>(File.ReadAllText(BookPath(name))) ?? new();

    List<Trivia> LoadMain()
    {
        if (!File.Exists(UserPath)) return LoadBuiltIn();

        List<Trivia> user;
        try
        {
            user = JsonSerializer.Deserialize<List<Trivia>>(File.ReadAllText(UserPath)) ?? new();
        }
        catch
        {
            return LoadBuiltIn(); // 壊れていたら組み込みを使う (ファイルは残しておく)
        }

        try { NewlyAdded = MergeNewBuiltIns(user); }
        catch { /* 足せなくても、今の一声帳はそのまま使う */ }
        return user;
    }

    /// <summary>まだ受け取っていない組み込みの雑学を一声帳の末尾に足し、足した数を返す</summary>
    static int MergeNewBuiltIns(List<Trivia> user)
    {
        var builtIn = LoadBuiltIn();
        bool hadKnown = File.Exists(KnownPath);
        var known = hadKnown
            ? (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(KnownPath)) ?? new()).ToHashSet()
            // 記録がない = v1.2.1 より前に保存した一声帳。そのときにあった最初の雑学は受け取り済みとみなす
            : builtIn.Where(t => t.Since == null).Select(t => t.Text).ToHashSet();

        var have = user.Select(t => t.Text).ToHashSet();
        var fresh = builtIn.Where(t => !known.Contains(t.Text) && !have.Contains(t.Text)).ToList();

        bool knownChanged = !hadKnown;
        foreach (var t in builtIn)
            if (known.Add(t.Text)) knownChanged = true;

        if (fresh.Count > 0)
        {
            user.AddRange(fresh.Select(t => t with { Since = null }));
            WriteJson(UserPath, user);
        }
        if (knownChanged) WriteJson(KnownPath, known.ToList());
        return fresh.Count;
    }

    static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}
