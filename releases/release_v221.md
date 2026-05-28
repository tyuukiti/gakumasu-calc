## What's Changed

### Bug Fixes
- **HIFモードの選択デッキ内訳でイベント回数が固定表示になっていた問題を修正**
  - 個別調整で活動支給日 → お出かけ／相談 などに切り替えても、選択デッキの効果内訳が変更前のイベント回数（×5 等）のままになっていた
  - 例: 「どんな関係なんですか？」の活動支給 Vo+12 が 3日切り替え後も `(×5) +60` のままで、期待値 `(×2) +24` を反映していなかった
  - 内訳のカウント計算が `available_actions` の優先度ベースだったのを、ユーザの実選択 (`turnChoices`) ベースに変更
  - 最終ステータス値は元から正しく計算されており、内訳表示のみのズレ。Web版・デスクトップ版の両方で修正

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v2.2.1-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.2.1/GakumasuCalc-v2.2.1-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v2.2.1-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.2.1/GakumasuCalc-v2.2.1-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
