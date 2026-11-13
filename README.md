# ツルの一声

画面の隅にいるツルをクリックすると、雑学をひとつ吹き出しで教えてくれる Windows 用の常駐アプリです。
知人の間で使うために作った個人制作のアプリです。

## 機能

- **クリックで一声**:ツルをクリックすると雑学をひとつ表示(112 個収録)
- **話す内容**:雑学 / 今日の出来事(Wikipedia)/ 最新ニュース(Google ニュース・NHK ニュース)/ まぜる
  - ニュースは吹き出しの「続きを読む ›」から記事をブラウザで開けます
- **一声帳**:収録されている雑学の一覧を、アプリ内で追加・編集・削除できます(デフォルトに戻すことも可能)
- **自動でつぶやく**:5 分〜3 時間ごとに自動で一声
- **表示時間**:雑学・今日の出来事・ニュース・時報ごとに、吹き出しを出しておく時間と読む速さを選べます
- **毎時の時報**:毎時 0 分に「午後3時になりました。」
- ドラッグで移動、大きさ変更、Windows 起動時の自動開始、タスクトレイ常駐

## ダウンロード

[Releases](../../releases) から `TsuruNoHitokoe_vX.Y.Z.zip` をダウンロードし、展開して `TsuruNoHitokoe.exe` を起動してください。
インストールや .NET の導入は不要です(Windows 10 / 11 64bit)。

> 電子署名をしていないため、初回起動時に「Windows によって PC が保護されました」と表示されます。
> 「詳細情報」→「実行」で起動できます。Smart App Control が有効な PC では起動できません。

使い方は zip 内の `はじめにお読みください.txt`(このリポジトリの [dist-readme.txt](dist-readme.txt))を参照してください。

## ビルド方法

必要なもの:Windows、[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

1. **ツルのイラストを用意する**
   ツルのイラストは [いらすとや](https://www.irasutoya.com/) の素材です。
   いらすとやの規約で素材の再配布が禁止されているため、このリポジトリには含めていません。
   [いらすとやの「鶴のイラスト」](https://www.irasutoya.com/2013/08/blog-post_8393.html) のページから
   `animal_tsuru.png` をダウンロードし、`Assets/tsuru.png` という名前で保存してください。
   (アイコン `Assets/app.ico` はビルド時にこの画像から自動で作られます)

2. **ビルドする**

   ```powershell
   dotnet build                       # 開発用
   powershell -ExecutionPolicy Bypass -File build.ps1   # 配布用 zip を dist/ に作成
   ```

`Assets/tsuru.png` と `Assets/app.ico` は `.gitignore` に入っています。**コミットしないでください。**

## ファイル構成

| ファイル | 内容 |
|---|---|
| `App.xaml.cs` | 全体の制御(メニュー、トレイ、自動でつぶやく、時報) |
| `CraneWindow.*` | 画面上のツル |
| `BubbleWindow.*` | 吹き出し |
| `EditorWindow.*` | 一声帳(雑学の編集画面) |
| `TriviaDeck.cs` | 雑学の山札と保存 |
| `OnThisDay.cs` | 今日の出来事(Wikipedia) |
| `NewsFeed.cs` | 最新ニュース(RSS) |
| `Assets/trivia.json` | 収録している雑学 |

設定と一声帳の編集内容は `%APPDATA%\TsuruNoHitokoe` に保存されます。

## インターネットから取得する内容について

「今日の出来事」「最新ニュース」は、アプリを使う人のパソコンが各サービスから直接取得して表示します(中継サーバーはありません)。

- **ニュース**:Google ニュース・NHK ニュースの RSS は、いずれも個人・非商用での利用に限って提供されています。
  表示されたニュースを転載・再配信したり、商用目的で使ったりしないでください。見出しの著作権は各報道機関・配信元に帰属します。
- **今日の出来事**:ウィキペディア日本語版の各日付の記事「できごと」節の文章です([CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/deed.ja))。
- どちらも外部サービスの都合で予告なく使えなくなることがあります。その場合はアプリに入っている雑学を話します。
- 表示される内容の正確さについて、作者は責任を負いません。

## クレジット

- ツルのイラスト:[いらすとや](https://www.irasutoya.com/)
- 今日の出来事:[ウィキペディア日本語版](https://ja.wikipedia.org/)(CC BY-SA 4.0)
- 最新ニュース:Google ニュース RSS / NHK ONE ニュース RSS
