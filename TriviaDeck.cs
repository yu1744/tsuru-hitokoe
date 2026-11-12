using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TsuruNoHitokoe;

public sealed record Trivia(
    [property: JsonPropertyName("c")] string Category,
    [property: JsonPropertyName("t")] string Text);

/// <summary>
/// 雑学の山札。一巡するまで同じものは出ない。
/// 一声帳で編集した内容は %APPDATA%\TsuruNoHitokoe\trivia.json に保存し、
/// それが無ければ組み込みの雑学を使う。
/// </summary>
public sealed class TriviaDeck
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま書く
    };

    List<Trivia> _all;
    readonly Queue<Trivia> _queue = new();
    readonly Random _rng = new();
    Trivia? _last;

    public static string UserPath => Path.Combine(Settings.Dir, "trivia.json");

    public TriviaDeck() => _all = LoadCurrent();

    public int Count => _all.Count;
    public IReadOnlyList<Trivia> Items => _all;

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
                        .Select(t => t with { Text = t.Text.Trim() })
                        .ToList();
        Directory.CreateDirectory(Settings.Dir);
        var tmp = UserPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOpts));
        File.Move(tmp, UserPath, overwrite: true);
        Replace(list);
    }

    /// <summary>編集をすべて捨てて組み込みの雑学に戻す</summary>
    public void ResetToDefault()
    {
        if (File.Exists(UserPath)) File.Delete(UserPath);
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

    static List<Trivia> LoadCurrent()
    {
        try
        {
            if (File.Exists(UserPath))
                return JsonSerializer.Deserialize<List<Trivia>>(File.ReadAllText(UserPath)) ?? new();
        }
        catch { /* 壊れていたら組み込みを使う (ファイルは残しておく) */ }
        return LoadBuiltIn();
    }
}
