> ⚠️ **先行公開リリース**
> 本リリースは H.I.F (Hatsuboshi IDOL FESTIVAL) モードの**先行公開版**です。
> 以下の値・挙動は実プレイデータに基づく調整が未完了で、暫定値で動作しています:
> - レッスン内イベント回数テンプレート（HIFプラン用）
> - 公開レッスンのサブ値、試験日の基礎値・配分値
> - HIFボーナスパネルの効果テーブル
>
> 今後のアップデートで数値が変更される可能性があります。プリセット保存機能を活用して、自分の調整値を残しておくことを推奨します。

## What's Changed

### New Features
- **H.I.F (Hatsuboshi IDOL FESTIVAL) モードを新設**
  - `/hif` ページ（Web版）／メインウィンドウの「HIF」タブ（デスクトップ版）で起動
  - 選抜試験(Day 1-20) + 本戦(Day 21-29) の29日構成プランで、ユーザが各日のアクションを直接選択
  - 公開レッスン日はメイン属性/サブ属性の2選択（メイン+設定値、サブ+設定値）
  - 試験日は基礎値(全属性同値) + 配分値(Vo/Da/Vi振分け) を直接入力
  - 一括設定（全公開レッスンを同じメイン/サブで一括適用、全授業を同属性で一括適用、配分プリセット Vo全振り/Da全振り/Vi全振り/均等）
  - スケジュール調整のプリセット保存（最大10件、デスクトップは `Data/HifSchedulePresets/`、Webは localStorage）
  - HIFボーナスパネル設定（Vo/Da/Vi上昇 Lv1-5、本戦パラメータ上限増加 Lv1-6、その他Lv表示のみ）
  - デッキ選出パターンは Vo×2+フリー3 / Da×2+フリー3 / Vi×2+フリー3 / オールフリー の4パターン
- **デッキ選出スコアリングにキャラクター補正を反映**（HIFモードのみ）
  - HIFボーナス込みのキャラ補正（base_status_bonus / para_bonus）を `CardScoringService` が受け取り、cap-aware なカード選出に反映
  - 通常モードのデッキ選出挙動は従来どおり（順位保存のためキャラ補正は最終計算のみ）

### Bug Fixes
- HIFモードで結果表示時にHIFボーナス無しキャラで再計算してしまい、ボーナスLv5の方がLv0より合計ステータスが低く表示される事象を修正
- HIFタブの上限ステータスが通常モードの値（2800）にフォールバックしていた問題を修正（本戦上限増加加算済みの値を表示）
- メモリープリセット保存YAMLに計算プロパティ `is_empty` が出力される問題を修正、旧バージョンが書き出した YAML も読み込めるよう互換処理を追加

### Improvements
- HIFタブのイベント回数を、通常モードと同等の「詳細を編集」Expander で直接編集可能に
- 通常モードの属性設定UIを、Web版と同じ薄背景カード形式に統一（Vo→ピンク / Da→水色 / Vi→クリーム）
- HIF個別調整のデフォルト優先度を `活動支給 > お出かけ > 相談 > 特別指導` に変更（お出かけはお金不要かつカード獲得枚数を稼げるため、相談より優先）

### Data
- 新サポートカード SSR `……騒々しいお祭りね` (`SP_SSR_0099`) を追加
- イベント回数テンプレートに HIF プラン用プリセット（センス / ロジック(好印象) / ロジック(やる気) など）を追加
- 新規プラン `hif.yaml` を追加（H.I.F 29日スケジュール）

## Download

| | ファイル | 備考 |
|---|---|---|
| 📦 | [GakumasuCalc-v2.0.0-dotnet-required.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.0.0/GakumasuCalc-v2.0.0-dotnet-required.zip) | .NET 10.0 ランタイムが必要 |
| 📦 | [GakumasuCalc-v2.0.0-win-x64.zip](https://github.com/tyuukiti/gakumasu-calc/releases/download/2.0.0/GakumasuCalc-v2.0.0-win-x64.zip) | ランタイム同梱（インストール不要） |
| 🌐 | [Web版](https://tyuukiti.github.io/gakumasu-calc/) | ブラウザで利用可能 |
