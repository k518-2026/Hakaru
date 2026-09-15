# Hakaru（測る）— ディスク速度ベンチマーク / 容量偽装チェック

実行ファイルのダウンロード.
https://github.com/k518-2026/Hakaru/releases

USB メモリや SD カードの **本当の読み書き速度** を測り、**容量偽装品**（「64GB」と書いてあるのに
実際は数 GB しかないフラッシュメモリ）を検出する Windows デスクトップアプリです。

CrystalDiskMark 的な速度計測と、H2testw / F3 的な容量チェックを 1 つにまとめています。
UI は **7 言語**（日本語・English・简体中文・한국어・Deutsch・Español・Français）に対応し、
実行中に切り替えられます。

![type](https://img.shields.io/badge/type-drive%20utility-35c1a6) ![ui](https://img.shields.io/badge/WPF-.NET%2010-512bd4) ![deps](https://img.shields.io/badge/NuGet-none-blue) ![i18n](https://img.shields.io/badge/languages-7-informational)

---

## できること

### 速度テスト

| 項目 | 内容 |
| --- | --- |
| シーケンシャル読み書き | 1 MiB ブロック連続 I/O（CrystalDiskMark の SEQ1M 相当） |
| ランダム読み書き | 4 KiB ブロック・キュー深度 1（RND4K Q1T1 相当）。MB/s と IOPS を表示 |
| 測り方 | `FILE_FLAG_NO_BUFFERING` でOSキャッシュを介さず計測。書き込みは `WRITE_THROUGH` |
| テストサイズ | 256 MiB 〜 4 GiB。中止はいつでも可能 |

### 容量チェック（偽装検出）

1. ドライブの空き容量を、位置情報を埋め込んだ検証可能なパターンで埋める
2. すべて読み戻して照合する
3. 判定：
   - **問題なし** … 書き込んだ分がそのまま読み戻せた
   - **⚠ 容量偽装の疑い** … 途中でデータが壊れる／別のオフセットのデータが読める
     （＝アドレスが巻き戻り、同じ物理メモリを使い回している）。実効容量の目安を表示

- 既存ファイルには一切触れず、専用フォルダー `__hakaru_captest__` だけを使います
- 「クイックテスト」で一部だけ（1〜16 GiB）を素早く確認することもできます
- 終了後はテストファイルを自動削除（残すオプションあり）。異常終了などでテストファイルが残っていると、
  そのドライブを選んだときに知らせ、「削除する」ボタンで消せます

---

## 使い方

1. 上部の「ドライブ」で測りたいドライブを選ぶ（USB メモリ・SD カードが優先して選ばれます。挿し直したら「再読み込み」）
   - **Windows が入っているドライブ（通常は C:）は一覧に表示されず、テストもできません**（下の「OS ドライブの保護」参照）
2. **速度テスト** タブ … 「テストサイズ」を選んで「開始」。4 項目を順に測ります
3. **容量チェック** タブ … 「空き容量すべてをテスト」か「クイックテストのみ」を選び、「容量チェックを開始」
   - 結果が「⚠ 容量偽装の疑いあり」なら、表示された「実際に使える容量」を超えてデータを保存しないでください
4. 右上の「言語」で表示言語を切り替えられます

テスト中に使う一時データは、ドライブ直下の `__hakaru_bench__`（速度テスト）と
`__hakaru_captest__`（容量チェック）フォルダーだけに置き、終了時に削除します。

---

## 動作環境とビルド

- Windows 10 / 11（x64）
- 配布版（Releases の `Hakaru.zip`）の `Hakaru.exe` は .NET ランタイムを同梱した単一ファイルなので、インストール不要でそのまま動きます
- ソースからビルドする場合: Visual Studio 2026（ワークロード「**.NET デスクトップ開発**」）／ または .NET 10 SDK
- 外部 NuGet パッケージへの依存はありません
- 通常のユーザー権限で動作します（`app.manifest` は `asInvoker`。バッファなし I/O に管理者権限は不要）

```bash
dotnet run --project Hakaru/Hakaru.csproj -c Release
```

単一 exe を作る場合:

```bash
dotnet publish Hakaru/Hakaru.csproj -c Release -p:PublishProfile=FolderProfile
```

---

## 多言語対応の仕組み

- `Localization/Strings.<code>.xaml` … 言語ごとの文字列辞書（`ResourceDictionary`）
- `LocalizationManager` … 英語を土台に読み込み、選択言語をその上に重ねる
  （未翻訳キーは英語にフォールバック）。実行中に差し替え可能
- XAML 側は `{DynamicResource キー}`、コード側は `LocalizationManager.Get(...)` / `Format(...)`
- 初回は OS の UI 言語から自動選択。選択は `%LOCALAPPDATA%\Hakaru\settings.json` に保存

新しい言語を足すには `Strings.<code>.xaml` を追加し、`LocalizationManager.Languages` に 1 行加えるだけです。

---

## 構成

```
Hakaru.slnx
└ Hakaru/
   ├ App.xaml(.cs)                   ダークテーマ。起動時に言語を初期化
   ├ app.manifest / app.ico          asInvoker / PerMonitorV2 DPI / 長いパス対応
   ├ MainWindow.xaml(.cs)            2 タブ（速度テスト / 容量チェック）
   ├ Common/                         MVVM 補助 + 単位整形（MB/s, GiB, IOPS…）
   ├ Localization/                   LocalizationManager + Strings.*.xaml ×7
   ├ Models/Models.cs                DriveItem / BenchmarkResult / CapacityTestResult ほか
   ├ Properties/PublishProfiles/     単一 exe 発行用のプロファイル（win-x64・ランタイム同梱）
   ├ ViewModels/MainViewModel.cs
   └ Services/
      ├ NativeMethods.cs             CreateFileW / ReadFile / WriteFile / SHFileOperation の P/Invoke
      ├ DiskIo.cs                    整列バッファ・バッファなしファイル・検証パターン
      ├ BenchmarkService.cs          逐次 / ランダム速度計測
      ├ CapacityTestService.cs       容量いっぱいの書き込み → 読み戻し照合
      ├ DriveService.cs              ドライブ列挙（OS ドライブの除外）・セクターサイズ取得
      ├ SettingsService.cs           設定の保存 / 読み込み
      └ ShellService.cs              エクスプローラーで開く・残ったテストファイルの検出と削除
```

### 検証パターン（容量偽装の見分け方）

各 4 KiB ページの先頭に「テスト空間内の絶対オフセット」と「乱数シード」を埋め込み、
残りを xorshift 乱数で埋めます。読み戻したときに

- ヘッダーのオフセットが書いた位置と違う → **アドレスの巻き戻り（容量偽装）**
- 乱数列が合わない → **データ破損**

として区別します。

---

## OS ドライブの保護

テストはドライブを大量の書き込みで埋めるため、Windows の入っているドライブでは行えないようにしています。

1. **一覧から除外** … Windows のフォルダーがあるボリュームは、ドライブの一覧に出しません。
   ドライブ文字だけでなくボリューム GUID でも比べるので、`subst` などで同じボリュームを別の文字から指していても除外します。
   ボリュームを判定できないドライブも、安全のため OS ドライブとして扱います
2. **開始時に再確認** … 速度テスト・容量チェックの開始時に、もう一度 OS ドライブでないことを確かめます
3. **書き込み直前に拒否** … 測定処理そのものも、最初の書き込みの前に OS ドライブを拒否します。
   残ったテストファイルの削除も、OS ドライブでは行いません

## 注意

- **このアプリの測定・判定は完璧ではありません。** 結果は目安として使い、大切なデータを保存する前や、
  製品の返品・交換を判断する前には、ほかのアプリでも検証してください。
  容量偽装の確認は H2testw や F3、速度の確認は CrystalDiskMark などと結果を見比べることをおすすめします。
- 容量チェックはドライブの空き容量をほぼ使い切るまで書き込みます。終わるまでドライブを抜かないでください。
- SSD に対してフルの容量チェックを繰り返すと寿命を縮めます。用途は USB メモリ / SD カードの検品です。

## ライセンス

[MIT License](LICENSE.txt)
