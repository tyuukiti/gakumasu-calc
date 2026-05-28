## What's Changed

### New Features
- 持ち込みメモリーの入力欄を追加（最大4枚、各属性ごとに「実数値加算」または「レッスンパラメーターボーナス%」を1値ずつ指定可能）
- メモリーの値はキャラ固有ボーナスと並列に最終計算へ反映（基礎値の flat 加算、レッスン上昇値の para_bonus% 合流）
- メモリー値のプリセット保存に対応（最大5件、名前付きで保存・呼び出し・削除）
  - デスクトップ版: `Data/MemoryPresets/memory_presets.yaml` に保存（リポジトリ管理外、ユーザー固有）
  - Web版: localStorage に保存
- 計算結果バーにメモリー補正分も差分表示（補正なし値との差分を可視化）

### Improvements
- ステータスバーの幅を**プランの `status_limit` 基準で動的化**（デスクトップ版）。プランを切り替えると基準値も自動で変わる（初レジェンド: 3000、NIA: 2600）
- メモリー入力欄の数値フィールドで小数値（例: `2.8`）を入力できるよう修正（中間状態 `2.` を許可）
- 削除ボタン用に `DangerButton` スタイルを新設、Primary/Danger ボタンに無効時のグレーアウト表示を追加
- C#版: ドロップダウンで同じプリセットを再選択した場合にも値が反映されるよう `DropDownClosed` で再ロード
- Web版: プリセット一覧をボタンリスト形式に変更（同じボタンを再クリックすれば毎回ロード）

### Data
- 初レジェンドプランの `status_limit` を 2800 → **3000** に変更（今後の上限引き上げに対応）
- イベント回数テンプレートの共通項目（ドリンク獲得、アイテム獲得、スキル獲得・強化・削除・カスタム・チェンジ、アクティブ強化・削除・獲得、メンタル強化・削除・獲得、ドリンク交換）を**軸ごと（活動支給軸 / 相談削除軸 / NIA）に統一**。センス・ロジック・アノマリーで同じ値に揃えた
- 全テンプレートに `skill_change: 3` と `mental_delete: 2` を追加

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v1.5.0-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/1.5.0/GakumasuCalc-v1.5.0-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v1.5.0-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/1.5.0/GakumasuCalc-v1.5.0-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
