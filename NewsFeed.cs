using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TsuruNoHitokoe;

/// <summary>
/// Google ニュース / NHK ニュースの RSS から最新の見出しを取ってくる。
/// 新しい順に、まだ話していない見出しから出す。
/// </summary>
public sealed class NewsFeed
{
    public const string Google = "google", Nhk = "nhk", Both = "both";

    const string GoogleUrl = "https://news.google.com/rss?hl=ja&gl=JP&ceid=JP:ja";
    // NHK: 主要ニュース(cat0) は数件しかないので各ジャンル(cat1〜7)も読む
    static readonly string[] NhkUrls = Enumerable.Range(0, 8)
        .Select(i => $"https://news.web.nhk/n-data/conf/na/rss/cat{i}.xml").ToArray();

    static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(20);
    static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    sealed record Item(string Title, string Source, DateTimeOffset Published);

    List<Item> _items = new();
    readonly HashSet<string> _seen = new();
    DateTime _fetchedAt = DateTime.MinValue;
    string _fetchedFor = "";
    Task? _loading;

    public string Sources { get; set; } = Google;

    /// <summary>古ければ取り直す。失敗しても例外は投げない。</summary>
    public Task EnsureFreshAsync()
    {
        bool stale = _fetchedFor != Sources || DateTime.Now - _fetchedAt > RefreshEvery;
        if (!stale) return Task.CompletedTask;
        if (_loading is { IsCompleted: false }) return _loading;
        return _loading = LoadAsync(Sources);
    }

    /// <summary>次の見出し。取れていなければ null。</summary>
    public string? Next()
    {
        if (_items.Count == 0) return null;
        var item = _items.FirstOrDefault(i => !_seen.Contains(i.Title));
        if (item == null)
        {
            // 全部話し終えたら最初から
            _seen.Clear();
            item = _items[0];
        }
        _seen.Add(item.Title);
        return $"{item.Title}\n({item.Source} {item.Published.ToLocalTime():M/d H:mm})";
    }

    async Task LoadAsync(string sources)
    {
        var urls = new List<(string Url, bool IsGoogle)>();
        if (sources is Google or Both) urls.Add((GoogleUrl, true));
        if (sources is Nhk or Both) urls.AddRange(NhkUrls.Select(u => (u, false)));

        var results = await Task.WhenAll(urls.Select(u => FetchAsync(u.Url, u.IsGoogle)));
        var cutoff = DateTimeOffset.Now - MaxAge;
        var items = results.SelectMany(r => r)
            .Where(i => i.Published >= cutoff)
            .GroupBy(i => i.Title).Select(g => g.First())
            .OrderByDescending(i => i.Published)
            .ToList();

        if (items.Count > 0)
        {
            _items = items;
            _fetchedFor = sources;
            _fetchedAt = DateTime.Now;
        }
        else if (_fetchedFor != sources)
        {
            _items = new(); // 取得元を変えたのに取れなかった場合、前の取得元のニュースは出さない
        }
    }

    static async Task<IEnumerable<Item>> FetchAsync(string url, bool isGoogle)
    {
        try
        {
            var xml = await Net.Http.GetStringAsync(url);
            var doc = XDocument.Parse(xml);
            var list = new List<Item>();
            foreach (var it in doc.Descendants("item"))
            {
                var title = (it.Element("title")?.Value ?? "").Trim();
                if (title.Length == 0 || title.StartsWith("【動画】")) continue;

                string source = "NHK";
                if (isGoogle)
                {
                    // Google ニュースの見出しは「見出し - 媒体名」
                    source = it.Element("source")?.Value.Trim() ?? "";
                    int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (dash > 0)
                    {
                        if (source.Length == 0) source = title[(dash + 3)..].Trim();
                        title = title[..dash].Trim();
                    }
                    if (source.Length == 0) source = "Google ニュース";
                }

                list.Add(new Item(title, source, ParseRfc822(it.Element("pubDate")?.Value)));
            }
            return list;
        }
        catch
        {
            return Array.Empty<Item>();
        }
    }

    /// <summary>"Thu, 12 Nov 2026 03:48:37 GMT" / "Thu, 12 Nov 2026 13:28:41 +0900"</summary>
    internal static DateTimeOffset ParseRfc822(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return DateTimeOffset.Now;
        s = s.Trim();
        s = Regex.Replace(s, @"\s(GMT|UT|UTC|Z)$", " +00:00");
        s = Regex.Replace(s, @"([+-]\d{2})(\d{2})$", "$1:$2");
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var d)
            ? d : DateTimeOffset.Now;
    }
}
