# カード選択ロジック テストスイート

`selectOptimalDeck` / `selectMultiplePatterns(Hif)`（自動サポカ編成ロジック）の回帰テスト。
**Web版(TypeScript) と デスクトップ版(C#) の両方**に同じ設計で用意し、さらに両実装が
同一結果を出すことを**クロス実装パリティ**で保証する。

## 実行方法

```powershell
# 両方まとめて実行し、結果を test-results/ に保存
pwsh ./run-tests.ps1
```

個別に走らせる場合:

```powershell
cd web; npm test                 # Web版 (TS / Vitest) … 54 件
dotnet test GakumasuCalc.Tests   # デスクトップ版 (C# / xUnit) … 47 件
```

通常モード(hatsu_legend) と **HIFモード(hif)** の両方を、実データ + **実イベント回数テンプレート**で検証する。
HIF はメイン属性の**順序込み全6通り**(vo/da, vo/vi, da/vo, da/vi, vi/vo, vi/da)を試す。

> テストハーネスはカードを **ID 昇順に正規化**して両実装に渡す (貪欲法は順序依存のため、
> パリティが「読込順の差」ではなく「ロジック差」だけを見るようにする)。詳細は末尾の注記。

補助チェック（Web版）: `npm run test:types`（テストの型検査） / `npx eslint .`

## 出力物（`run-tests.ps1` 実行時、`test-results/` に保存・gitignore対象）

| ファイル | 内容 |
|---|---|
| `vitest.txt` | TS 全テスト名＋合否（verbose コンソールの写し） |
| `vitest-junit.xml` | TS の JUnit XML（CI / ツール用） |
| `dotnet.txt` | C# 全テスト名＋合否（detailed コンソールの写し） |
| `dotnet.trx` | C# の TRX（Visual Studio / CI で開ける） |

> `npm test` 単体でも `verbose`（全テスト名表示）＋ `test-results/vitest-junit.xml` を出力する。

---

## 設計方針（なぜこのテストか）

カード選択は「貪欲フィル → postOptimize → レンタル再最適化 → SP枚数強制 → 型枠強制 →
局所修復」の多段で順序依存が強く、修正のたびに回帰しやすい。そこで：

- **採点の真値 = 実 `calculate` の cap 後合計**（最適化器の内部スコアではない）。
  ヒューリスティックそのものを正解扱いしないため、`scoreDeck` / `DeckScorer.ScoreDeck` が
  実計算結果を上限でクランプして合計する。
- **総当たりオラクル**: 小さな合成カードプールでは全編成を列挙して「真の最適」を求め、
  自動ピックがそれに一致するかを検証（`findOptimalDeck` / `BruteForce.FindOptimalDeck`）。
- **性質ベース**: カードID直書きの脆いアサートを避け、「最適か」「制約を満たすか」など
  スコア調整で壊れない性質を検証する。

---

## テストカタログ

> 各層の符号 — **L1**=合成データで最適性・制約・決定性を厳密検証 / **L2**=実データで「自動編成≧手動編成」(このツールの核心) / **L4**=C#版とWeb版が同結果か(クロス実装パリティ)。

### Web版（`web/tests/`）— 54 件

| ファイル | 件数 | 検証内容 |
|---|---|---|
| `smoke.test.ts` | 2 | 実データ(カード/プラン)の読込／`selectMultiplePatterns`+`scoreDeck`が動く |
| `cardScoring.invariant.test.ts` | 4 | **L1 総当たりオラクル**: ①上限張り付きトラップで同属性を積み過ぎず均等配分を選ぶ ②上限なしで寄与最大の6枚 ③属性枠(vo2/da2)ありでも最適一致 ④全列挙のどの手動にも劣らない |
| `cardScoring.constraints.test.ts` | 13 | **L1 制約遵守**: デッキ6枚・重複なし／必須カード必須／属性枠充足／SP枚数充足／必須+SP両立／**必須5枚(要望#138)で全含有・レンタル1枚+自動選出枠1**／**必須6枚(上限)で全枠固定・借用は必須内に1枚**／決定性(同入力→同出力)／実データ3メイン組合せの全パターンが6枚・重複なし |
| `cardScoring.autoGeManual.test.ts` | 9 | **L2 自動≧手動**(通常モード・**テンプレ適用**): hatsu_legend「センス（活動支給軸）」テンプレの additionalCounts 下で、3メイン組合せ×3バランス手動編成(3+3 / 2+2+2 / 3+2+1)に自動が劣らない |
| `cardScoringHif.test.ts` | 13 | **HIFモード・テンプレ適用**: メイン**順序込み6通り**×(全パターン6枚・重複なし／自動≧単体寄与トップ6) + SP制約。HIF「センス」テンプレ適用 |
| `cardScoringHif.crossSeed.test.ts` | 1 | **L2 回帰**(ユーザ報告2026-06): リーリヤ+HIF Lv5・DaSP3・アノマリー・所持のみ・exam全Vi で、**レンタル枠対応の総当たりオラクル**(各パターンが surface したカードの和集合+Daレンタル候補を全列挙)を実データに適用し、自動最良 ≧ 独立に求めた最適。答えを事前に知らず局所最適落ちを捕捉。`TestFixtures/hif_repro_inventory.json` 使用 |
| `cardScoringHif.requiredRental.test.ts` | 1 | **L1 回帰**(ユーザ報告2026-06「必須を増やすとレンタルが消える」): 紫雲清夏+HIF Lv5・sense・DaSP2・所持のみ・コンテスト・必須4枚(全てDaSP非カバー)で所持枠が6枚に達する overfill 下、各パターンにレンタルがちょうど1枚存在し最低凸カードに乗る。`TestFixtures/hif_repro_inventory.json` 使用 |
| `cardScoring.requiredSpOverflow.test.ts` | 3 | **L1 回帰**(ユーザ報告2026-07「必須+SP指定で編成が7枚に膨張」): hatsu_legend/アノマリー・所持のみ・必須4枚(内 all型SP=食欲・vi型SP=のんびり)・SP Da2/Vi3 で、all型SPが両属性の必要数を同時に満たすため編成が常に6枚・SP充足・必須全含有・レンタル1枚。all型SP必須カードの過剰確保で7枚化する退行ガード |
| `cardScoringHif.unownedRental.test.ts` | 1 | **L2 回帰**(ユーザ報告2026-07「未所持カードがレンタル選出されない」): 倉本千奈+HIF Lv5・sense・DaSP3・所持のみ・必須1枚・0069未所持で、自動最良 ≧ 手動編成(未所持0069を4凸レンタル+0071を0凸SP要員に残す)。インベントリが未所持を uncap:4 で保存するため「4凸所持」誤判定でレンタル候補から除外される問題と、SP要員レンタル固定時に未所持借用の複合手を取り逃す問題の退行ガード。`TestFixtures/hif_unowned_rental_inventory.json` 使用 |
| `cardScoringHif.spTotal6.test.ts` | 3 | **L1 回帰**(issue #145「SP枚数設定が多いと編成パターンが見つからない」): hatsu_legend/アノマリー・SP Vo4+Da2(合計6)・必須2枚(as型SP=食欲・未所持vi型SP=不足なし)で、パターンスキップ判定がレンタル枠(6枚目)を吸収容量に数えず全パターン0件になる退行ガード。パターンが返り各6枚・必須全含・SP充足・レンタル1枚／必須なし(SP先取りoverfill経路)でも同様／レンタルなし時は従来どおり0件 |
| `cardScoring.rental.test.ts` | 2 | **レンタル枠**: レンタルモードで6枚・レンタル枠ちょうど1・重複なし／4凸所持を浪費せず未所持の強カードを借用 |
| `parity.test.ts` | 2 | **L4 パリティ**: `expected.json` と一致(なければ生成)／各シナリオが非空 |

### デスクトップ版（`GakumasuCalc.Tests/`）— 47 件

| ファイル | 件数 | 検証内容 |
|---|---|---|
| `SmokeTests.cs` | 2 | Web版 smoke と同等（実データ読込／編成+採点） |
| `InvariantTests.cs` | 4 | **L1 総当たりオラクル**（Web版と同シナリオ。cap-trap=740 等の期待値も一致） |
| `ConstraintTests.cs` | 13 | **L1 制約遵守**（6枚・重複・必須・属性枠・SP枚数・必須+SP・必須5枚/6枚上限・決定性・実データ3組合せ） |
| `AutoGeManualTests.cs` | 3 | **L2 自動≧手動**(通常モード・テンプレ適用。3メイン組合せ×バランス手動編成） |
| `HifTests.cs` | 13 | **HIFモード・テンプレ適用**（Web版と同等: 順序込み6通り×(6枚・重複なし/自動≧単体トップ6) + SP充足） |
| `ReproHif0030Tests.cs` | 1 | **L2 回帰**(Web版 `cardScoringHif.crossSeed` と対): ユーザ報告シナリオで自動最良 ≧ レンタル枠対応総当たりオラクルの最適。cross-seed 大域最適化の回帰ガード |
| `ReproRequiredRentalTests.cs` | 1 | **L1 回帰**(Web版 `cardScoringHif.requiredRental` と対): 必須4枚 overfill 下でも各パターンにレンタルが1枚存在し最低凸カードに乗る |
| `ReproRequiredSpOverflowTests.cs` | 3 | **L1 回帰**(Web版 `cardScoring.requiredSpOverflow` と対): 必須4枚+SP Da2/Vi3 で all型SP必須カードが両属性を同時に満たし編成が常に6枚(all型SPの過剰確保による7枚化の退行ガード) |
| `ReproHifUnownedRentalTests.cs` | 1 | **L2 回帰**(Web版 `cardScoringHif.unownedRental` と対): 未所持カードを「4凸所持」誤判定でレンタル候補から除外しない・SP要員レンタル固定時も未所持借用の複合手で手動編成以上に到達 |
| `ReproSpTotal6Tests.cs` | 3 | **L1 回帰**(Web版 `cardScoringHif.spTotal6` と対): SP合計6でもパターンが返り各6枚・必須全含・SP充足・レンタル1枚。レンタルなし時は従来どおり0件 |
| `RentalTests.cs` | 2 | **レンタル枠**（Web版と同等） |
| `ParityTests.cs` | 1 | **L4 パリティ**: TS生成の `expected.json`(11シナリオ) に C#実装が完全一致 |

---

## クロス実装パリティの仕組み（`TestFixtures/parity/`）

- `configs.json` … 手書きの正準シナリオ（プラン・メイン属性・サブ・SP指定・テンプレ名）。
  通常モード4 + **HIFモード7** = 計11シナリオ（`"mode": "hif"` / `"planId"` / `"templateName"` で切替）。
- `expected.json` … TS パリティテストが**初回に生成**（編成カードID＋cap後合計）。コミット対象。
- Web版・C#版の両パリティテストが**同じ `expected.json`** に突合する。
  → 2実装が乖離したら必ずどちらかが赤くなる（`fix both` ルールの自動検証）。

**ロジックを変えて編成が意図的に変わったら**: `TestFixtures/parity/expected.json` を削除して
`cd web; npm test` を1回流すと再生成される（その後コミット）。

---

## 既知の仕様（バグではない）

通常モードは「単一属性6枚」編成を生成しない（`[0,0,5]` パターンがサブ属性を1枚強制）。
6枚同属性ではメイン2/サブが必要ステータスに届かずゲーム的に成立しないための**意図した
バランス保持**。理論最大より最大0.56%低く出るケースがあるが実害なし。L2テストが手動比較を
「到達可能なバランス編成」に限定しているのはこの仕様に沿った正しい設計。**この差を見て
最適化器をいじらないこと。**（HIFのオールフリーは単型を生成できるので別挙動。）

---

## 注記: カード探索順への依存と内部正準化（解決済み）

最適化器は貪欲法ベースで**カード候補の順序に依存**する（同点・僅差で別の局所最適に到達しうる）。
読込順は元々 Web版 `ssr→sr→r` / デスクトップ版 `Directory.GetFiles`(ファイル名順 `r→sr→ssr`) と
異なり、**同じ入力でも両版で編成・合計が変わりうる**問題があった（テンプレ適用 HIF da/vi で実検出）。

**対応済み（マルチスタート）**: `selectOptimalDeck`（Web/C#両方）は候補プールを**カードデータ由来の
3順序**(ID昇順 / ID降順 / レアリティ順)で選出を試し、実 `calculate` の cap 後合計が最大の編成を採る
(`candidateOrderings`)。レンタルプールも ID 昇順に正準化。
- 順序がカードデータのみで決まるので**入力順に非依存** → 両版が同一入力で必ず同一編成を出す。
- 複数スタートの最良を採るので、単一順序の貪欲より**取りこぼしが減る**（単調改善・悪化なし）。

> パリティテストのハーネスは**あえて本番の異なる読込順のまま**カードを渡している。それでも
> パリティが通る＝「実装が入力順非依存」かつ「両版のマルチスタートが完全一致」の証明。

> **性能**: 1スタートあたり postOptimize の実計算コスト（テンプレ適用時 約1.5秒）が乗り、3スタートで
> 通常モード約4.5秒/HIF約2.7秒。最適性優先でこのコストを許容する判断（テストは `testTimeout` を延長）。
