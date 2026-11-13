using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace TsuruNoHitokoe;

/// <summary>
/// GitHub の Releases に新しいバージョンが出ていないか調べる。
/// 公開リポジトリの「最新リリース」API はログイン不要 (1 時間 60 回まで) なので、自前のサーバーはいらない。
/// ダウンロードや入れ替えは行わず、配布ページを知らせるだけ。
/// </summary>
public static class UpdateChecker
{
    const string LatestApi = "https://api.github.com/repos/yu1744/tsuru-hitokoe/releases/latest";

    public sealed record Release(Version Version, string Tag, string PageUrl);

    /// <summary>このアプリのバージョン (例: 1.2.0)</summary>
    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>最新リリースを取得する。取れなければ null (オフライン、上限超過、非公開化など)。</summary>
    public static async Task<Release?> GetLatestAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await Net.Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var page = root.GetProperty("html_url").GetString() ?? "";
            if (!TryParseTag(tag, out var version)) return null;
            return new Release(version, tag, page);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"v1.2.0" や "1.2" を Version にする</summary>
    static bool TryParseTag(string tag, out Version version)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(s, out var v) && v != null)
        {
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }
}
