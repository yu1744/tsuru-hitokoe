using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TsuruNoHitokoe;

/// <summary>
/// ツルの代わりに表示する、利用者が選んだ画像。
/// 選んだファイルは設定フォルダにコピーしておく (元のファイルを動かしても表示できるように)。
/// </summary>
public static class CustomImage
{
    public const string DialogFilter = "画像ファイル (*.png;*.jpg;*.jpeg;*.gif;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp";
    static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
    const long MaxBytes = 20 * 1024 * 1024;

    static string BasePath => Path.Combine(Settings.Dir, "custom_crane");

    /// <summary>保存済みの画像ファイル (なければ null)</summary>
    public static string? SavedFile =>
        Extensions.Select(e => BasePath + e).FirstOrDefault(File.Exists);

    /// <summary>画像を読み込む。読めなければ null。</summary>
    public static ImageSource? Load(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;              // ファイルをつかんだままにしない
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // 差し替えたとき古い画像が出ないように
            bmp.DecodePixelHeight = 300;                              // 大きな写真でもメモリを食わないように
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 選んだ画像を確かめて設定フォルダにコピーし、読み込んだ画像を返す。
    /// 使えない場合は理由を error に入れて null を返す。
    /// </summary>
    public static ImageSource? Import(string sourcePath, out string? error)
    {
        error = null;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!Extensions.Contains(ext)) { error = "PNG・JPEG・GIF・BMP の画像を選んでください。"; return null; }
        if (new FileInfo(sourcePath).Length > MaxBytes) { error = "画像が大きすぎます(20MB まで)。"; return null; }
        if (Load(sourcePath) == null) { error = "この画像は読み込めませんでした。"; return null; }

        Directory.CreateDirectory(Settings.Dir);
        Remove();
        var dest = BasePath + ext;
        File.Copy(sourcePath, dest, overwrite: true);
        return Load(dest);
    }

    /// <summary>保存した画像を消して、元のツルに戻す</summary>
    public static void Remove()
    {
        foreach (var e in Extensions)
        {
            var f = BasePath + e;
            if (File.Exists(f)) File.Delete(f);
        }
    }
}
