using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TsuruNoHitokoe;

/// <summary>
/// 一声帳に読み込む CSV を解釈する。
///
/// 受け付ける形:
///   ・1 列 … 雑学の本文だけ (カテゴリは「じぶん」)
///   ・2 列 … カテゴリ, 本文
///   ・1 行目が見出し (「カテゴリ」「雑学」など) なら、その並びに従う
/// 文字コードは UTF-8 (BOM あり・なし) と Shift_JIS (Excel の「CSV」保存) に対応。
/// "..." で囲めば、カンマや改行を含む本文も書ける。
/// </summary>
public static class CsvImport
{
    const string DefaultCategory = "じぶん";
    const int MaxCategoryLength = 12; // これより長い 1 列目はカテゴリではなく本文の一部とみなす

    static readonly string[] TextHeaders = { "雑学", "一声", "本文", "内容", "テキスト", "text", "t", "trivia" };
    static readonly string[] CategoryHeaders = { "カテゴリ", "カテゴリー", "分類", "ジャンル", "category", "c", "genre" };

    /// <summary>書き方の見本。Excel でも文字化けしないよう BOM 付き UTF-8 で書く</summary>
    public static void WriteTemplate(string path)
    {
        const string template =
            "カテゴリ,雑学\r\n" +
            "生き物,ラッコは眠るとき、仲間と前足をつないで浮かんでいることがある。\r\n" +
            "食べ物,\"本文に半角カンマ(,)や改行を入れたいときは、このように \"\"ダブルクォート\"\" で囲む。\"\r\n" +
            ",カテゴリを空にすると「じぶん」になる。\r\n";
        File.WriteAllText(path, template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static List<Trivia> Read(string path)
    {
        var text = Decode(File.ReadAllBytes(path));
        var rows = Parse(text)
            .Select(r => r.Select(c => c.Trim()).ToList())
            .Where(r => r.Any(c => c.Length > 0))
            .ToList();
        if (rows.Count == 0) return new();

        int textCol = -1, catCol = -1;
        var head = rows[0].Select(c => c.ToLowerInvariant()).ToList();
        int ti = head.FindIndex(c => TextHeaders.Contains(c));
        if (ti >= 0)
        {
            textCol = ti;
            catCol = head.FindIndex(c => CategoryHeaders.Contains(c));
            rows.RemoveAt(0);
        }

        var result = new List<Trivia>();
        foreach (var r in rows)
        {
            string cat, body;
            if (textCol >= 0)
            {
                body = textCol < r.Count ? r[textCol] : "";
                cat = catCol >= 0 && catCol < r.Count ? r[catCol] : "";
            }
            else if (r.Count >= 2 && r[0].Length <= MaxCategoryLength)
            {
                cat = r[0];
                body = string.Join(",", r.Skip(1).Where(c => c.Length > 0)); // 囲み忘れたカンマはつなぎ直す
            }
            else
            {
                cat = "";
                body = string.Join(",", r.Where(c => c.Length > 0));
            }

            if (body.Length == 0) continue;
            result.Add(new Trivia(cat.Length == 0 ? DefaultCategory : cat, body));
        }
        return result;
    }

    /// <summary>UTF-8 として正しく読めればそのまま、だめなら Shift_JIS として読む</summary>
    static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    /// <summary>RFC 4180 に沿って行・列に分ける ("" は " 1 文字、囲みの中の改行も本文)</summary>
    static IEnumerable<List<string>> Parse(string s)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                row.Add(cell.ToString()); cell.Clear();
                yield return row;
                row = new List<string>();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            yield return row;
        }
    }
}
