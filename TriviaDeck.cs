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
/// それが無ければ組み込みの雑学を使う。
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

    public TriviaDeck() => _all = LoadCurrent();

    public int Count => _all.Count;
    public IReadOnlyList<Trivia> Items => _all;

    /// <summary>起動時に、アップデートで一声帳に足した雑学の数</summary>
    public int NewlyAdded { get; private set; }

    /// <summary>組み込みのまま (一度も保存していない) か</summary>
    public static bool IsDefault => !File.Exists(UserPath);

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
        WriteJson(UserPath, list);
        // 一声帳には今の組み込み雑学がすべて並んでいたので、全部受け取り済みにする
        WriteJson(KnownPath, LoadBuiltIn().Select(t => t.Text).Distinct().ToList());
        Replace(list);
    }

    /// <summary>編集をすべて捨てて組み込みの雑学に戻す</summary>
    public void ResetToDefault()
    {
        if (File.Exists(UserPath)) File.Delete(UserPath);
        if (File.Exists(KnownPath)) File.Delete(KnownPath);
        Replace(LoadBuiltIn());
    }

    void Replace(List<Trivia> list)
    {
        _all = list;
        _queue.Clear();
    }

    public static List<Trivia> LoadBuiltIn()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("trivia.json")
                      ?? throw new InvalidOperationException("trivia.json が見つかりません");
        return JsonSerializer.Deserialize<List<Trivia>>(s) ?? new();
    }

    List<Trivia> LoadCurrent()
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
        Directory.CreateDirectory(Settings.Dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}
