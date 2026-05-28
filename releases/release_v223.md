## What's Changed

### Bug Fixes
- **HIFモードの選抜試験で獲得したパラメータにパラメータボーナスが適用されていなかった問題を修正**
  - 選抜試験①〜③で獲得する基礎値+配分値に、サポカ/キャラ/持ち込みメモリーの `para_bonus` が乗っていなかった
  - 例: メインレッスン全踏み800 + 選抜試験基礎200 = 1000 に対して「いつまでも続けばいいのに」(パラボDa+8.5%) を適用したとき、本来 1000×8.5%=85 のところ 800×8.5%=68 になっていた
  - 週次ゲイン計算 (`statusCalculation`) と編成パターン選出時のパラボ寄与計算 (`cardScoring`) の両方を修正
  - Web版・デスクトップ版の両方で修正

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v2.2.3-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.2.3/GakumasuCalc-v2.2.3-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v2.2.3-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.2.3/GakumasuCalc-v2.2.3-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
