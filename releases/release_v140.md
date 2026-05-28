## What's Changed

### New Features
- キャラクター選択機能を追加（13キャラ・任意指定・折りたたみUI）
- キャラ固有の基礎ステータス加算と属性別パラボ%を計算結果に反映
- 3凸レッスンボーナスのON/OFF切替（デフォルトOFF・課金要素のため）
- 計算結果バーにキャラ補正分を暗色で可視化、合計値の隣に補正なしの値も併記
- サポートイベントによるパラメータ上昇増加効果(+N%)を計算に反映（凸数別の倍率: SSR/SR/R で別テーブル）
- ステータス内訳に増加率と補正後の値を表示（例: `初期値+20(+100%)=40`）
- メンタルスキルカード削除トリガー (`mental_delete`) を追加（イベント発生回数入力欄にも対応）

### Fixes
- カードデータ収集ロジックの表記揺れ対応（「おでかけ」「スキルカード強化」「相談でPドリンク交換」など）
- 一部カードの効果が正しいトリガーで分類されていなかった問題を修正

### Data
- `Data/Characters/characters.yaml` に13キャラの基礎/パラボ/3凸ボーナス/公式イメージカラーを追加
- `Data/SupportCards/*.yaml` を再同期し、`event_param` フラグと `event_param_boost` 効果を全対応カードに反映

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v1.4.0-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/1.4.0/GakumasuCalc-v1.4.0-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v1.4.0-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/1.4.0/GakumasuCalc-v1.4.0-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
