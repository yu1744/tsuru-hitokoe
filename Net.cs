using System;
using System.Net.Http;

namespace TsuruNoHitokoe;

static class Net
{
    public static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TsuruNoHitokoe/1.0 (Windows desktop app)");
        return c;
    }
}
