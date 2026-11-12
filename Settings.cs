using System;
using System.IO;
using System.Text.Json;

namespace TsuruNoHitokoe;

public sealed class Settings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public int AutoSpeakMinutes { get; set; }
    public double Scale { get; set; } = 1.0;
    public bool CraneVisible { get; set; } = true;
    public int SpokenCount { get; set; }

    /// <summary>話す内容: trivia (雑学) / today (今日の出来事) / news (最新ニュース) / mix (まぜる)</summary>
    public string Mode { get; set; } = "trivia";

    /// <summary>毎時 0 分に「〇時になりました」と知らせる</summary>
    public bool HourlyChime { get; set; } = true;

    /// <summary>ニュースの取得元: google / nhk / both</summary>
    public string NewsSources { get; set; } = "google";

    public static string Dir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsuruNoHitokoe");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static bool Exists => File.Exists(FilePath);

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* 壊れていたら既定値で起動 */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 保存失敗は致命的ではない */ }
    }
}
