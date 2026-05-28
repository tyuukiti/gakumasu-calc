## What's Changed

### New Features
- **Pアイテム由来のPドリンク供給を計算に反映**（新スキーマ `value_type: trigger_count_bonus`）
  - サポカに紐づく **Pアイテムが他カードのPドリンク獲得トリガーを追加発火させる効果** を表現可能に
  - HIFで全Da SPを踏む構成だと「ふわふわでワクワク」が DaSP終了×8回 × 2個 = 16回ぶんのPドリンク獲得トリガーを供給し、「いつまでも続けばいいのに」「おい、来てやったぞ！」「いつも頑張ってるね。」などの p_drink_acquire 効果が16倍発火される計算に
  - スキーマ拡張: `CardEffect` に `trigger_target` (加算対象トリガー)、`scales_with` (スケール元トリガー) を追加
- **カード選出ロジックがカード間の相乗効果を考慮するように**
  - postOptimize（フル再計算によるカードスワップ最適化）を**全モード常時実行**に変更（従来は所持カードモードのみ）
  - スワップ時のタイプ制限を緩和: pattern の最低タイプ要件 (例: Vi×2) を満たすクロスタイプスワップを許可
  - HIFモードはユーザが明示的に選んだターン選択を postOptimize の評価にも渡すように（従来は合成された turn 配分で評価していたためズレが発生していた）
- **C# (デスクトップ版) のサポカ行をクリック展開に変更**
  - マウスオーバーのツールチップから、行クリックで展開する形式に
  - 展開時は Vo/Da/Vi 別の内訳に加え、効果別行をスタットカラー付きで表示（Web版と同等）

### Improvements
- **デッキ選出時の相乗効果寄与をカード合計値に併記**
  - ピックされたサポカの右側に `+107 (+240)` 形式で「自カード寄与 + 他カード経由の推定寄与」を表示
- **SyncWiki がPアイテムのドリンク供給を自動検出**
  - `scripts/sync_wiki.py` で Wiki Pアイテムテキストから「Pドリンク（SR以上）を獲得」「Pドリンク2つ」などを検出し、`trigger_count_bonus` 効果を自動生成
  - 既存の手動追加エントリとも key (trigger + value_type + trigger_target) でマッチするので重複なし

### Bug Fixes
- YAML writer が `trigger_target` / `scales_with` を出力していなかった問題を修正（既存の手動追加エントリが sync で破損する事象）
- HIFモードで `postOptimize` が合成 turn 配分を使って評価していたため、ユーザの実選択（全Daレッスン等）と異なる前提でスワップ判定していた事象を修正

### Data
新たに **9 件のサポカに `trigger_count_bonus`（Pドリンク供給）効果を追加**:

| サポカ | アイテム名 | scales_with | 最大回数 |
|---|---|---|---|
| ふわふわでワクワク (SR/Da/フリー) | ふわふわでもこもこ | da_sp_end | 無制限 |
| なぜこんなところにッ！？ (SSR/Da/アノマリー) | 完全制覇でポン | fullpower_acquire | 2 |
| あっちも行きたいですわ！ (SSR/Da/センス) | ほっこりまんぷく | good_condition_acquire | 4 |
| 会長、準備は万端です (SSR/Vi/ロジック) | 会長の完璧な計画 | genki_acquire | 4 |
| おひさま笑顔、満開ふたつ (SR/Da/アノマリー) | そっくりワンワン | conserve_acquire | 2 |
| はっぴぃはろうぃ～～ん！ (SR/Da/センス) | びっくり仮装グッズ | good_condition_acquire | 2 |
| 目指すはテッペン (SR/Da/フリー) | トレーナーの優しさ | da_sp_end | 2 |
| まだまだのばしてー (SR/Vi/フリー) | 体ほぐしローラー | vi_sp_end | 2 |
| 基礎＞応用 (SR/Vo/フリー) | うるおいのどケア | vo_sp_end | 2 |

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v2.1.0-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.1.0/GakumasuCalc-v2.1.0-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v2.1.0-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.1.0/GakumasuCalc-v2.1.0-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
