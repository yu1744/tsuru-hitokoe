using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TsuruNoHitokoe;

/// <summary>
/// 日本語版ウィキペディアの日付記事 (例: 「11月12日」) の「できごと」節から
/// 今日の出来事を取ってくる。Wikimedia の On This Day API は日本語非対応のため action API を使う。
/// 本文は CC BY-SA 4.0 (出典: ウィキペディア日本語版)。
/// </summary>
public sealed class OnThisDay
{
    static HttpClient Http => Net.Http;
    const string Api = "https://ja.wikipedia.org/w/api.php";

    readonly Random _rng = new();
    readonly Queue<string> _queue = new();
    DateTime _loadedFor = DateTime.MinValue;
    List<string> _items = new();
    Task? _loading;

    /// <summary>今日の分を読み込み済みか</summary>
    public bool IsReady => _loadedFor == DateTime.Today && _items.Count > 0;

    /// <summary>今日の分を取得 (取得済みなら何もしない)。失敗しても例外は投げない。</summary>
    public Task EnsureLoadedAsync()
    {
        if (_loadedFor == DateTime.Today) return Task.CompletedTask;
        if (_loading is { IsCompleted: false }) return _loading;
        return _loading = LoadAsync(DateTime.Today);
    }

    /// <summary>次の出来事。取得できていなければ null。</summary>
    public string? Next()
    {
        if (!IsReady) return null;
        if (_queue.Count == 0)
            foreach (var s in _items.OrderBy(_ => _rng.Next())) _queue.Enqueue(s);
        return _queue.Dequeue();
    }

    async Task LoadAsync(DateTime day)
    {
        try
        {
            string page = $"{day.Month}月{day.Day}日";
            string pageEsc = Uri.EscapeDataString(page);

            // 「できごと」節の番号を調べる
            using var secDoc = JsonDocument.Parse(await Http.GetStringAsync(
                $"{Api}?action=parse&page={pageEsc}&prop=sections&format=json&formatversion=2"));
            string? index = null;
            foreach (var s in secDoc.RootElement.GetProperty("parse").GetProperty("sections").EnumerateArray())
            {
                if (s.GetProperty("line").GetString() == "できごと") { index = s.GetProperty("index").GetString(); break; }
            }
            if (index == null) return;

            using var txtDoc = JsonDocument.Parse(await Http.GetStringAsync(
                $"{Api}?action=parse&page={pageEsc}&prop=wikitext&section={index}&format=json&formatversion=2"));
            string wikitext = txtDoc.RootElement.GetProperty("parse").GetProperty("wikitext").GetString() ?? "";

            var items = Parse(wikitext, day).ToList();
            if (items.Count > 0)
            {
                _items = items;
                _queue.Clear();
                _loadedFor = day;
            }
        }
        catch
        {
            // オフラインなど。次回また試す。
        }
    }

    static readonly Regex LineRx = new(@"^\*\s*(?<year>.+?)\s+-\s+(?<text>.+)$", RegexOptions.Compiled);

    static IEnumerable<string> Parse(string wikitext, DateTime day)
    {
        foreach (var raw in wikitext.Split('\n'))
        {
            var m = LineRx.Match(raw.Trim());
            if (!m.Success) continue;

            var year = Clean(m.Groups["year"].Value);
            if (!Regex.IsMatch(year, @"^(紀元前)?\d{1,4}年")) continue;
            year = Regex.Match(year, @"^(紀元前)?\d{1,4}年").Value;

            var text = Clean(m.Groups["text"].Value);
            if (text.Length < 8 || text.Length > 140) continue;
            if (!text.EndsWith("。")) text += "。";

            yield return $"{year}({day.Month}月{day.Day}日)の今日、\n{text}";
        }
    }

    /// <summary>ウィキ記法を素のテキストにする</summary>
    static string Clean(string s)
    {
        s = Regex.Replace(s, @"<!--.*?-->", "");
        s = Regex.Replace(s, @"<ref[^>/]*/>", "");
        s = Regex.Replace(s, @"<ref[^>]*>.*?</ref>", "");
        s = Regex.Replace(s, @"<[^>]+>", "");

        // よく使われるテンプレートは中身を残し、それ以外は消す (内側から順に)
        for (int i = 0; i < 5 && s.Contains("{{"); i++)
        {
            s = Regex.Replace(s, @"\{\{([^{}]*)\}\}", m =>
            {
                var parts = m.Groups[1].Value.Split('|');
                var name = parts[0].Trim().ToLowerInvariant();
                if ((name == "仮リンク" || name == "ill2") && parts.Length > 1) return parts[1];
                if ((name == "lang" || name == "lang-en") && parts.Length > 2) return parts[^1];
                if (name == "読み仮名" && parts.Length > 2) return $"{parts[1]}({parts[2]})";
                if (name.StartsWith("lang-") && parts.Length > 1) return parts[^1];
                return "";
            });
        }

        s = Regex.Replace(s, @"\[\[(?:[^\]|]*\|)?([^\]]*)\]\]", "$1"); // [[a|b]] → b, [[a]] → a
        s = Regex.Replace(s, @"\[https?://\S+\s([^\]]+)\]", "$1");     // [url 表示] → 表示
        s = s.Replace("'''", "").Replace("''", "");
        s = s.Replace("&nbsp;", " ");
        s = Regex.Replace(s, @"\s{2,}", " ");
        return s.Trim();
    }
}
