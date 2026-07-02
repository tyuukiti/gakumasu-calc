# アーキテクチャ

## 全体構成

デスクトップ版（C# / WPF）とWeb版（TypeScript / React）の2系統で、**同一の計算ロジックを両言語で二重実装**している。データ（YAML）は共通。

```
gakumasu_tool/
├── GakumasuCalc/           # デスクトップ版メイン計算ツール (WPF)
├── CardInventoryManager/   # デスクトップ版サポカ所持管理 (WPF)
├── SupportCardEditor/      # サポカデータ編集ツール (WPF、開発者用)
├── GakumasuCalc.Tests/     # C# テスト (xUnit)
├── web/                    # Web版 (React + TypeScript + Vite)
├── Data/                   # 共通YAMLデータ
├── TestFixtures/           # クロス実装パリティテスト用フィクスチャ
└── scripts/                # データ同期スクリプト (Python)
```

> **重要**: 計算ロジックやデータ構造を変更する場合は、**デスクトップ版（C#）とWeb版（TypeScript）の両方を必ず同時に変更する**こと。片方だけの変更は計算結果の不整合を招く。両実装の一致はパリティテストで自動検証される（[TESTS.md](../TESTS.md) 参照）。

## 技術スタック

| | デスクトップ版 | Web版 |
|---|---|---|
| 言語 | C# 12+ / .NET 10.0 | TypeScript |
| UI | WPF (MVVM) + MaterialDesignInXamlToolkit | React 19 + Tailwind CSS |
| 状態管理 | INotifyPropertyChanged (ViewModel) | Zustand |
| ルーティング | TabControl | React Router v7 |
| YAML | YamlDotNet | js-yaml |
| ビルド | dotnet | Vite |

## レイヤー構成

### デスクトップ版 (GakumasuCalc)

```
Views (XAML) → ViewModels (INotifyPropertyChanged) → Services → Models
```

### Web版 (web/)

```
pages → components → stores (Zustand) → services → types
```

## 画面構成（シナリオタブ）

両版とも、シナリオごとの独立タブで構成される（起動時は HIF を表示）。

| タブ | プランID | 特記事項 |
|---|---|---|
| HIF | `hif` | 29日構成（選抜試験20日＋本戦9行程）、HIFボーナスパネル |
| 初レジェンド | `hatsu_legend` | 18週構成、「休む」対応 |
| NIA | `nia` | 26週構成、オーディション獲得パラメータ |

3シナリオとも日程（スケジュール）選択方式で、各週・各日の行動をユーザーが直接選択する。メイン属性は日程の選択内容から自動判定される。

- Web版: `web/src/App.tsx` のルート定義（`/hif` `/legend` `/nia` ＋ `/inventory` `/usage`）
- デスクトップ版: `GakumasuCalc/Views/MainWindow.xaml` の TabControl

## 主要サービス対応表

| 役割 | デスクトップ版 (C#) | Web版 (TS) |
|---|---|---|
| ステータス計算 | `Services/StatusCalculationService.cs` | `web/src/services/statusCalculation.ts` |
| デッキ最適化 | `Services/CardScoringService.cs` | `web/src/services/cardScoring.ts` |
| サポカ読み込み | `Services/SupportCardLoaderService.cs` | `web/src/services/yamlLoader.ts` |
| プラン読み込み | `Services/PlanLoaderService.cs` | `web/src/services/yamlLoader.ts` |
| キャラ補正 | `Services/CharacterLoaderService.cs` | `web/src/services/characterBonus.ts` |
| 所持カード管理 | `Services/InventoryService.cs` | `web/src/services/inventory.ts`（localStorage） |
| 診断出力 | — | `web/src/services/diagnostics.ts` |
| プリセット永続化 | `*PresetService.cs`（`Data/` 配下にYAML生成） | 各ストア（localStorage） |

## 計算フロー

```
MainViewModel.Calculate()  /  calcStore.executeCalculate()
  → CardScoringService.SelectOptimalDeck()   — デッキ選出 (Main1/Main2/Free の組合せパターン)
  → StatusCalculationService.Calculate()     — 装備ボーナス + SP率 + パラボーナス + 週次処理
  → 結果表示 (各ターンの内訳 / 週別内訳 / アビリティまとめ)
```

ステータス計算は「装備ボーナス（初期値）→ 週次処理（各週の行動に応じた上昇）→ 最終ステータス算出」の順で、プランの `status_limit`（初レジェンド: 3000、NIA: 2600 など）で属性ごとにキャップされる。

キャラ固有ボーナス（基礎ステータス加算＋属性別パラボ%）と持ち込みメモリーは**最終ステータス計算にのみ**反映され、デッキ最適化スコアには影響しない（全カード共通の加算/乗算のため編成順位が保存される）。持ち込みPアイテムのボーナスは計算対象外。

## デッキ最適化 (CardScoringService / cardScoring.ts)

6枚（所持5＋レンタル1）の最適構成を選出する。**ロジック修正時は以下の制約を必ず維持すること。**

### 処理順序 (SelectOptimalDeck)

1. **Step 0: 必須カード強制挿入** — requiredCardIds のカードを先に確保
2. **Step 1: SP率カード先行確保** — spCounts 分のSP率カードを選出し protectedIds に登録
3. **Step 2: グリーディ充足** — 残り枠を GreedyFillOwned で埋める
4. **Step 3: レンタル選出** — Pattern A/B/C で最良レンタルを決定
5. **Step 4: PostOptimize** — 実計算ベースのヒルクライムで微調整

### マルチスタート（入力順非依存）

貪欲法は候補カードの順序に依存するため、候補プールを**カードデータ由来の3順序**（ID昇順 / ID降順 / レアリティ順）で試行し、実 `Calculate()` の cap 後合計が最大の編成を採用する（`candidateOrderings`）。これにより同じ入力なら読込順によらず両実装が同一編成を出す。

### 必須カード枠の制約

- 所持カードなら selected に直接追加、属性枠またはフリー枠を消費
- 未所持カードはレンタル枠（requiredRentalCard）として保留（最大1枚）
- 必須カードは protectedIds に登録 → PostOptimize でスワップ不可（SP率の有無に関わらず `IsRequired` で判定）
- 必須カードがSP率を持つ場合、spCounts を減算してから Step 1 に進む

### SP率カードの制約

- SP率判定: `trigger == "equip" && value_type == "sp_rate"`
- Step 1 でスコア順に必要枚数を確保し、protectedIds に登録
- PostOptimize では、SP率で保護されたカードは**SP率を持つ別の候補とのみ**交換可能
- SP率保護カードをスワップした場合、protectedIds を旧カード→新カードに付け替える
- 必須カード（非SP）の保護とSP率の保護は区別して管理する

### タイプ別スロット制約

- カードには属性タイプ（vo/da/vi/all）があり、パターンごとに属性枠数が決まる
- PostOptimize でのスワップは同一タイプ同士、または "all" タイプとの交換のみ許可
- タイプ分布が崩れるスワップは禁止（例: Da枠にVoカードを入れる）

### デッキパターン

- 通常シナリオ: [3,2,1], [2,3,1], [3,3,0], [2,2,2], [0,0,5]（Main1/Main2/Free枚数）の5パターンを試行
- HIF: Vo×2 / Da×2 / Vi×2 / オールフリーの4編成パターンを試行
- SP枚数が不足するパターンはスキップ
- 各パターンの TotalValue（`status_limit` キャップ適用後）で最良を選出

### PostOptimize の不変条件

- レンタルモード（rentalPool != null）でのみ実行
- 評価には StatusCalculationService.Calculate() の実結果を使用（近似スコア不可）
- スワップ禁止: レンタルカード、必須カード（非SP保護）
- スワップ条件付き: SP率保護カード（SP率持ちの候補とのみ交換可）
- タイプ分布維持を常にチェック

### 編成制約の優先順位

実現可能な限り「必須カード > SP枚数 > パターン枚数」の順で必ず充足する。SP枚数・タイプ枠の充足は強制パス（enforceSpCounts / enforceTypeSlots）で保証される。

### レンタル枠の意味

レンタル枠は「どの1枚を4凸で借りるか」の割当。4凸所持カードをレンタルにするのは枠の浪費なので、低凸・未所持のカードに割り当てる（optimizeRentalAssignment）。

### 既知の仕様（バグではない）

通常シナリオは「単一属性6枚」編成を生成しない（`[0,0,5]` パターンがサブ属性を1枚強制する）。理論最大より最大0.56%低く出るケースがあるが、ゲーム的に成立しないバランスを避ける意図した仕様。**この差を見て最適化器を変更しないこと**（HIFのオールフリーは単型を生成できるため別挙動）。

## web/public/Data について

`web/public/Data` は リポジトリルートの `Data/` への**シンボリックリンク**。データ編集は `Data/` 側だけを変更する（両方に書くと重複する）。
