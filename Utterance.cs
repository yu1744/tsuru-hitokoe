using System;

namespace TsuruNoHitokoe;

/// <summary>
/// ツルが話す一言。
/// Meta (出典・時刻など) や Link (続きを読む) はニュースのときだけ付く。
/// </summary>
public sealed record Utterance(string Text, string? Meta = null, string? Link = null)
{
    /// <summary>ブラウザで開いてよいリンク (http / https のみ)</summary>
    public Uri? SafeLink =>
        Uri.TryCreate(Link, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u : null;
}
