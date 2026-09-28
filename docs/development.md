# 開発ガイド

## 開発環境

| 対象 | 要件 |
|---|---|
| デスクトップ版 | .NET 10.0 SDK / Windows |
| Web版 | Node.js 22+ |
| データ同期スクリプト | Python 3 |

## ビルド・実行

```bash
# ソリューション全体のビルド
dotnet build GakumasuCalc.slnx

# 個別プロジェクトのビルド
dotnet build GakumasuCalc/GakumasuCalc.csproj
dotnet build CardInventoryManager/CardInventoryManager.csproj
dotnet build SupportCardEditor/SupportCardEditor.csproj

# リリースビルド (PowerShell、配布ZIPを生成)
.\build-release.ps1 -Version "2.8.6"

# Web版
cd web
npm install
npm run dev      # 開発サーバー起動
npm run build    # プロダクションビルド (型チェック + Vite + SPA用404.html生成)
```

## テスト

テストスイートの設計・カタログは [TESTS.md](../TESTS.md) を参照。

```powershell
# C#版・Web版・クロス実装パリティを一括実行 (結果は test-results/ に保存)
.\run-tests.ps1
```

```bash
# C# (xUnit)
dotnet test GakumasuCalc.Tests
dotnet test GakumasuCalc.Tests --filter "FullyQualifiedName~<クラス名・メソッド名>"   # 単体実行

# Web (Vitest)
npm --prefix web test                    # 一括実行
npm --prefix web run test:watch          # 監視モード
npm --prefix web test -- <ファイルパス>  # 単体実行
npm --prefix web test -- -t "<テスト名>"
npm --prefix web run test:types          # テストの型チェック
npm --prefix web run lint                # ESLint
```

### クロス実装パリティ

`TestFixtures/parity/configs.json`（正準シナリオ）と `expected.json`（期待編成）で、TypeScript版とC#版が同一結果を出すことを検証する。

**ロジック変更で編成が意図的に変わった場合**は `TestFixtures/parity/expected.json` を削除して `npm --prefix web test` を1回実行すると再生成される（その後コミット）。

## コーディング規約

- **文字コード**: UTF-8 BOM（ソースコード） / 既存Markdownドキュメントは UTF-8 BOMなし
- **改行コード**: CRLF（全ファイル共通）
- **両実装の同期**: 計算ロジック・データ構造の変更は、**C#版とWeb版の両方に必ず適用する**。片方だけの変更は禁止（パリティテストが検出する）

## データ同期 (Wiki → YAML)

サポートカードデータは Seesaa Wiki から差分同期する。

```bash
python scripts/sync_wiki.py                  # 差分チェック + 同期
python scripts/sync_wiki.py --dry-run        # 確認のみ (書き込みなし)
python scripts/sync_wiki.py --update-only    # 既存カードの値更新のみ
python scripts/sync_wiki.py --new-only       # 新規カード追加のみ
python scripts/sync_wiki.py --images-only    # 画像未取得カードの画像のみ取得
python scripts/sync_wiki.py --delay 10       # リクエスト間隔を変更
python scripts/sync_wiki.py --debug          # 詳細デバッグ出力
```

- 新規カードはWikiから取得してYAMLに追加＋画像ダウンロード、既存カードは凸別値のみ更新、削除は行わない（安全側）
- 同期済みカードは `Data/SupportCards/_synced.txt` で管理
- モジュール本体は `scripts/wiki_sync/`（`constants.py` / `network.py` / `parsers.py` / `card_builder.py` / `yaml_io.py`）

### TRIGGER_MAP の注意

Wikiのアビリティ名→ `trigger` の解決は `scripts/wiki_sync/constants.py` の `TRIGGER_MAP` が行う。表記揺れ（「お出かけ」/「おでかけ」、「スキルカード強化」/「スキル強化」、「スキルカードカスタマイズ」/「スキルカスタム」、「相談でPドリンク交換」/「相談Pドリンク」、「休む選択」/「休憩」など）を吸収するため、**具体的なキーワードを汎用キーワードより上に配置**している。新規追加時は順序に注意。

## リリース手順

1. **バージョン更新**: 正本は `GakumasuCalc/GakumasuCalc.csproj` の `<Version>`（`AssemblyVersion` / `FileVersion` も合わせる）
2. **リリースノート作成**: `releases/release_v<XXX>.md`（バージョンからドットを除去。例: 2.8.6 → `release_v286.md`）。バージョン更新とリリースノート作成は**セットで1コミット**にする
3. **main へマージ**
4. **タグ付け**: bareタグ `X.Y.Z`（`v` なし）を push すると `.github/workflows/release.yml` が起動し、`build-release.ps1` でZIPをビルドして該当ノートを本文に GitHub Release を自動作成する

```powershell
# タグ付けの自動化 (main最新化 → csproj からバージョン検出 → ノート存在確認 → tag & push)
.\tag-release.ps1           # csproj の <Version> から自動決定
.\tag-release.ps1 -DryRun   # チェックのみ
```

### Web版のデプロイ

`main` への push で `.github/workflows/deploy-web.yml` が起動し、GitHub Pages（[gakumasu.tyuukiti.com](https://gakumasu.tyuukiti.com/)）へ自動デプロイされる。

### 旧 URL からのブラウザ保存データ引き継ぎ

2026-09-28 に Web版の URL を `tyuukiti.github.io/gakumasu-calc` から `gakumasu.tyuukiti.com` へ変更した。ブラウザ保存データ (localStorage) はホスト単位なので、旧 URL で登録した所持カード・プリセット等は新ドメインからは読めない。`web/src/services/legacyMigration.ts` が新ドメインの初回訪問時に旧ホスト上の引き継ぎページへ往復し、利用者の操作なしで取り込む（仕組みの詳細は同ファイル冒頭のコメント）。

- 引き継ぎページの実体: `web/migrate/gakumasu-calc-migrate.html`（ビルド対象外。単体で動く静的ページ）
- 公開先: `https://tyuukiti.github.io/gakumasu-anomaly-sim/gakumasu-calc-migrate.html`。旧 URL と同じホストで公開している別プロダクト gakumasu-anomaly-sim（Vite）の `public/` にこのファイルを同じ名前で置き、通常どおりデプロイする。anomaly-sim にカスタムドメインを付けると旧ホストで動かなくなるので、期限まで付けない
- 順序: 引き継ぎページを先に公開してから本リポジトリをマージする。未公開の間に3回訪問したブラウザは以後試みない
- 期限: 2027-03-31（`MIGRATION_DEADLINE_MS`）。以降は往復しないので、引き継ぎページと `legacyMigration.ts`・`LegacyMigrationNotice.tsx`・`web/migrate/`・関連テストを削除してよい
- 動作確認: 旧 URL で所持登録したことのあるブラウザで新ドメインを開くと、一瞬 tyuukiti.github.io を経由して戻り、ヘッダー下に「引き継ぎました」の案内が出る。DevTools の Application > Local Storage で `legacyMigrationDone` を消すと再試行できる
