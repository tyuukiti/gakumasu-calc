# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 言語

- ユーザーとの会話は常に**日本語**で行うこと。

## プロジェクト概要

学園アイドルマスター（学マス）の育成理論値計算ツール。サポートカード構成を最適化し、ステータス理論値を算出するWindows デスクトップアプリケーション群＋Web版。

## 技術スタック

### デスクトップ版
- C# 12+ / .NET 10.0 / WPF (MVVM)
- UIライブラリ: MaterialDesignInXamlToolkit v5.3.1
- データ形式: YAML (YamlDotNet v16.3.0)

### Web版 (`web/`)
- React 19 / TypeScript / Vite
- 状態管理: Zustand
- ルーティング: React Router v7
- データ形式: YAML (js-yaml)

### 共通
- データスクレイピング: Python 3

## ビルド・実行

```bash
# ソリューション全体のビルド
dotnet build GakumasuCalc.slnx

# 個別プロジェクトのビルド
dotnet build GakumasuCalc/GakumasuCalc.csproj
dotnet build CardInventoryManager/CardInventoryManager.csproj
dotnet build SupportCardEditor/SupportCardEditor.csproj

# リリースビルド (PowerShell)
.\build-release.ps1 -Version "0.3.0"

# Web版
cd web
npm install
npm run dev      # 開発サーバー起動
npm run build    # プロダクションビルド
```

テストプロジェクトは現在存在しない。

## アーキテクチャ

3つのWPFアプリ＋Web版で構成:

### デスクトップ版

3つのWPFアプリ（すべてMVVMパターン）:

| アプリ | プロジェクト | 役割 |
|--------|-------------|------|
| 学マス育成計算ツール | `GakumasuCalc/` | メイン計算エンジン |
| サポカ所持管理 | `CardInventoryManager/` | カード所持・凸数管理 |
| サポカデータ編集 | `SupportCardEditor/` | カードデータYAML編集 |

### Web版 (`web/`)

デスクトップ版の計算ロジックをTypeScriptで再実装したブラウザ版。React + Zustandで構成。

```
pages → components → stores (Zustand) → services → types
```

- `services/statusCalculation.ts` — ステータス計算（デスクトップ版 StatusCalculationService に対応）
- `services/cardScoring.ts` — デッキ最適化（デスクトップ版 CardScoringService に対応）
- `services/yamlLoader.ts` — YAMLデータ読み込み
- `services/inventory.ts` — 所持カード管理（localStorage永続化）

### レイヤー構成 (GakumasuCalc)

```
Views (XAML) → ViewModels (INotifyPropertyChanged) → Services → Models
```

### 主要サービス

- **StatusCalculationService** — ステータス計算のコアロジック。装備ボーナス→18週の週次処理→最終ステータス算出
- **CardScoringService** — デッキ最適化。6枚(所持5+レンタル1)の最適構成を選出
- **SupportCardLoaderService / PlanLoaderService** — YAMLからのデータ読み込み
- **InventoryService** — ユーザーの所持カード永続化

### 計算フロー

```
MainViewModel.Calculate()
  → CardScoringService.SelectOptimalDeck() — デッキ選出(Main1/Main2/Freeの組合せパターン)
  → StatusCalculationService.Calculate() — 装備ボーナス + SP率 + パラボーナス + 週次処理
  → 結果表示(TurnChoiceViewModel で各ターンの内訳)
```

### デッキ構成パターン

5+1(レンタル)の6枚で、Main1/Main2/Freeの配分を複数パターン試行して最適解を選出。ステータス上限は各属性プランごとに異なる（`hatsu_legend`: 3000、`nia`: 2600 など。各プランYAMLの `status_limit` で定義）。

## データ構造 (Data/)

- `SupportCards/*.yaml` — カード定義(id, name, rarity, type, plan, effects)
- `Plans/*.yaml` — 育成プラン(18週のスケジュール、基礎ステータス)
- `Templates/event_count_templates.yaml` — イベント回数プリセット
- `Characters/characters.yaml` — キャラ定義(id, name, color, initial, base_status_bonus, para_bonus)
- `Inventory/inventory.yaml` — ユーザー所持データ（自動生成）
- `Images/` — カード画像（リポジトリ外、著作権上除外）

### カードエフェクトのトリガータイプ

各エフェクトは `trigger`(発動契機) × `value_type`(値の種類) × `stat`(対象属性) × `values`[5要素](凸0〜4) で表現される。

#### value_type

| value_type | 意味 |
|---|---|
| `flat` | 実数値加算（ステータス上昇） |
| `sp_rate` | SPレッスン発生率% |
| `para_bonus` | パラメータボーナス%（該当属性のレッスン上昇値に乗算） |
| `event_param_boost` | 「このサポートカードのイベントによるパラメータ上昇を+N%増加」効果。同カードの `event_param: true` 付き flat 効果に乗算（`flat × (1 + boost%/100)`） |

#### 追加属性

- `event_param: true` — サポートイベント由来の固定値 `equip+flat` 効果。同カードの `event_param_boost` の対象。
- `max_count` — 発動回数上限（null=無制限）
- `condition` — 発動条件（例: `vo>=400`, `deck>=20`, `hp>=50%`）
- `source: item` — プロデュースアイテム由来の効果

#### trigger 一覧

**装備時（常時発動）**

| trigger | 内容 |
|---|---|
| `equip` | 装備時（常時）。`flat`(初期値ボーナス) / `sp_rate`(SP発生率) / `para_bonus`(レッスン補正) / `event_param_boost`(イベントパラ%増) |

**レッスン・試験**

| trigger | 内容 |
|---|---|
| `sp_end` | SPレッスン終了時（汎用） |
| `vo_sp_end` | ボーカルSPレッスン終了時 |
| `da_sp_end` | ダンスSPレッスン終了時 |
| `vi_sp_end` | ビジュアルSPレッスン終了時 |
| `lesson_end` | レッスン終了時（汎用） |
| `vo_lesson_end` | ボーカルレッスン終了時 |
| `da_lesson_end` | ダンスレッスン終了時 |
| `vi_lesson_end` | ビジュアルレッスン終了時 |
| `vo_normal_end` | ボーカル通常レッスン終了時 |
| `da_normal_end` | ダンス通常レッスン終了時 |
| `vi_normal_end` | ビジュアル通常レッスン終了時 |
| `exam_end` | 試験・オーディション終了時 |

**週次イベント**

| trigger | 内容 |
|---|---|
| `class_end` | 授業・営業終了時 |
| `outing_end` | お出かけ終了時 |
| `consultation` | 相談選択時 |
| `consultation_drink` | 相談でPドリンク交換時 |
| `activity_supply` | 活動支給・差し入れ選択時 |
| `special_training` | 特別指導開始時 |
| `rest` | 休む選択時 |

**スキルカード操作（汎用）**

| trigger | 内容 |
|---|---|
| `skill_ssr_acquire` | スキルカード（SSR）獲得時 |
| `skill_enhance` | スキルカード強化時 |
| `skill_delete` | スキルカード削除時 |
| `skill_custom` | スキルカードカスタマイズ時 |
| `skill_change` | スキルカードチェンジ時 |

**アクティブスキルカード操作**

| trigger | 内容 |
|---|---|
| `active_acquire` | アクティブスキルカード獲得時 |
| `active_enhance` | アクティブスキルカード強化時 |
| `active_delete` | アクティブスキルカード削除時 |

**メンタルスキルカード操作**

| trigger | 内容 |
|---|---|
| `mental_acquire` | メンタルスキルカード獲得時 |
| `mental_enhance` | メンタルスキルカード強化時 |
| `mental_delete` | メンタルスキルカード削除時 |

**Pアイテム / Pドリンク**

| trigger | 内容 |
|---|---|
| `p_item_acquire` | Pアイテム獲得時 |
| `p_drink_acquire` | Pドリンク獲得時 |

**スキルカード効果獲得時**

| trigger | 内容 |
|---|---|
| `genki_acquire` | 元気カード獲得時 |
| `good_condition_acquire` | 好調カード獲得時 |
| `good_impression_acquire` | 好印象カード獲得時 |
| `conserve_acquire` | 温存（根気）カード獲得時 |
| `concentrate_acquire` | 集中カード獲得時 |
| `motivation_acquire` | やる気カード獲得時 |
| `fullpower_acquire` | 全力カード獲得時 |
| `aggressive_acquire` | 強気カード獲得時 |

#### Wiki表記との対応（sync_wiki TRIGGER_MAP）

`scripts/wiki_sync/constants.py` の `TRIGGER_MAP` でWikiアビリティ名→trigger を解決。表記揺れ（「お出かけ」/「おでかけ」、「スキルカード強化」/「スキル強化」、「スキルカードカスタマイズ」/「スキルカスタム」、「相談でPドリンク交換」/「相談Pドリンク」、「休む選択」/「休憩」など）を吸収するため、具体的なキーワードを汎用キーワードより上に配置している。新規追加時は順序に注意。

## コーディング規約

- **文字コード**: UTF-8 BOM（全ファイル共通）
- **改行コード**: CRLF（全ファイル共通）

ファイルの新規作成・編集時は必ず上記を遵守すること。

## デッキ最適化（CardScoringService）の制約・不変条件

デッキ選出ロジックの修正時は、以下の制約を必ず維持すること。違反すると計算結果が不正になる。

### 処理順序（SelectOptimalDeck）

1. **Step 0: 必須カード強制挿入** — requiredCardIds のカードを先に確保
2. **Step 1: SP率カード先行確保** — spCounts 分のSP率カードを選出し protectedIds に登録
3. **Step 2: グリーディ充足** — 残り枠を GreedyFillOwned で埋める
4. **Step 3: レンタル選出** — Pattern A/B/C で最良レンタルを決定
5. **Step 4: PostOptimize** — 実計算ベースのヒルクライムで微調整

### 必須カード枠の制約

- 所持カードなら selected に直接追加、属性枠またはフリー枠を消費
- 未所持カードはレンタル枠（requiredRentalCard）として保留（最大1枚）
- 必須カードは protectedIds に登録 → PostOptimize でスワップ不可（**SP率の有無に関わらず `IsRequired` で判定**）
- 必須カードがSP率を持つ場合、spCounts を減算してから Step 1 に進む

### SP率カード設定の制約

- SP率判定: `trigger == "equip" && value_type == "sp_rate"`
- Step 1 でスコア順に必要枚数を確保し、protectedIds に登録
- **PostOptimize での扱い**: SP率で保護されたカードは、SP率を持つ別の候補とのみ交換可能（非SPカードへの交換は禁止）
- SP率保護カードをスワップした場合、protectedIds を旧カード→新カードに付け替えること
- 必須カード（非SP）の保護とSP率の保護は区別して管理する

### タイプ別スロット制約

- カードには属性タイプ（vo/da/vi/all）があり、パターンごとに属性枠数が決まる
- PostOptimize でのスワップは同一タイプ同士、または "all" タイプとの交換のみ許可
- タイプ分布が崩れるスワップは禁止（例: Da枠にVoカードを入れる）

### デッキパターン

- 5パターン試行: [3,2,1], [2,3,1], [3,3,0], [2,2,2], [0,0,5]（Main1/Main2/Free枚数）
- SP枚数が不足するパターンはスキップ
- 各パターンの TotalValue（プランの `status_limit` キャップ適用後）で最良を選出

### PostOptimize の不変条件

- レンタルモード（rentalPool != null）でのみ実行
- 評価には StatusCalculationService.Calculate() の実結果を使用（近似スコア不可）
- スワップ禁止: レンタルカード、必須カード（非SP保護）
- スワップ条件付き: SP率保護カード（SP率持ちの候補とのみ交換可）
- タイプ分布維持を常にチェック

## リリース

- GitHub Releaseを作成する際は、リポジトリルートの `release_v{バージョン}.md`（例: `release_v134.md`）を参照し、前回のリリースノートのフォーマットに合わせること。
- `build-release.ps1 -Version "x.y.z"` でビルド後、`release/` フォルダに生成されたZIPファイルをGitHub Releaseに添付する。

## 注意事項

- **デスクトップ版とWeb版の同期**: 計算ロジックやデータ構造の改修・機能追加を行う場合は、デスクトップ版（C#）とWeb版（TypeScript）の両方を同時に変更すること。片方だけの変更は計算結果の不整合を招くため禁止。
- 計算はデフォルトで4凸(完凸)前提、v0.3.0から凸数指定対応
- キャラ固有ボーナス（基礎ステータス加算 + 属性別パラボ%）は計算に含まれる（`Data/Characters/` 参照、未選択時はキャラ補正なし）。ただしキャラ補正は最終ステータス計算にのみ反映され、`CardScoringService` のデッキ最適化スコアには影響しない（全カード共通の加算/乗算で順位保存のため）。Pアイテムボーナスは計算に含まれない
- カード画像は著作権の関係でリポジトリに含めない
- 日本語UIのため、実行ファイル名やYAMLの一部に日本語を使用
