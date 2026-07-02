# データ形式 (Data/)

デスクトップ版・Web版が共通で読み込むYAMLデータの仕様。

```
Data/
├── SupportCards/   # サポートカード定義 (ssr_cards.yaml / sr_cards.yaml / r_cards.yaml)
├── Plans/          # 育成プラン定義 (hif.yaml / hatsu_legend.yaml / nia.yaml)
├── Templates/      # イベント回数テンプレート (event_count_templates.yaml)
├── Characters/     # キャラ定義 (characters.yaml)
├── Images/         # カード画像 + マッピングファイル（画像は著作権上リポジトリ外）
└── Inventory/      # ユーザー所持データ (inventory.yaml、自動生成)
```

デスクトップ版では、各種プリセット（`MemoryPresets/` `EventCountPresets/` `SchedulePresets/` `HifSchedulePresets/` `HifBonusLevels/` など）も実行時に `Data/` 配下へ自動生成される（リポジトリには含まれない）。Web版の同等データは localStorage に保存される。

> `web/public/Data` は `Data/` へのシンボリックリンク。**データ編集は必ず `Data/` 側だけ**を変更する。

## サポートカード (SupportCards/*.yaml)

```yaml
support_cards:
- id: SP_SSR_0093            # カードID (SP_<レアリティ>_<連番>)
  name: あたしの勝ち、ですね～！
  rarity: SSR                 # SSR / SR / R
  type: vi                    # vo / da / vi / all (属性タイプ。編成の属性枠判定に使用)
  plan: anomaly               # sense / logic / anomaly / free
  tag: skill                  # 後述
  effects:
  - trigger: equip            # 発動契機 (後述)
    stat: vi                  # 対象属性 (vo / da / vi / all)
    values: [20, 20, 20, 20, 20]   # 凸0〜4 の効果値 (5要素固定)
    value_type: flat          # 値の種類 (後述)
    event_param: true         # 任意属性 (後述)
```

各エフェクトは `trigger`（発動契機）× `value_type`（値の種類）× `stat`（対象属性）× `values[5]`（凸0〜4）で表現される。

### tag

| tag | 意味 |
|---|---|
| `skill` | スキルカード付き |
| `produce_item` | プロデュースアイテム付き |
| `exam_item` | コンテスト（試験）アイテム付き |
| `none` | 付属なし |

コンテストモードは `skill` と `exam_item` のカードを編成候補から除外する。

### value_type

| value_type | 意味 |
|---|---|
| `flat` | 実数値加算（ステータス上昇） |
| `sp_rate` | SPレッスン発生率% |
| `para_bonus` | パラメータボーナス%（該当属性のレッスン上昇値に乗算） |
| `event_param_boost` | 「このサポートカードのイベントによるパラメータ上昇を+N%増加」効果。同カードの `event_param: true` 付き flat 効果に乗算（`flat × (1 + boost%/100)`） |

### エフェクトの任意属性

| 属性 | 意味 |
|---|---|
| `event_param: true` | サポートイベント由来の固定値 `equip`+`flat` 効果。同カードの `event_param_boost` の対象 |
| `max_count` | 発動回数上限（省略時は無制限） |
| `condition` | 発動条件（例: `vo>=400`, `deck>=20`, `hp>=50%`） |
| `source: item` | プロデュースアイテム由来の効果（結果内訳で `[アイテム]` 表示） |

### trigger 一覧

**装備時（常時発動）**

| trigger | 内容 |
|---|---|
| `equip` | 装備時（常時）。`flat`(初期値ボーナス) / `sp_rate`(SP発生率) / `para_bonus`(レッスン補正) / `event_param_boost`(イベントパラ%増) |

**レッスン・試験**

| trigger | 内容 |
|---|---|
| `sp_end` | SPレッスン終了時（汎用） |
| `vo_sp_end` / `da_sp_end` / `vi_sp_end` | 各属性SPレッスン終了時 |
| `lesson_end` | レッスン終了時（汎用） |
| `vo_lesson_end` / `da_lesson_end` / `vi_lesson_end` | 各属性レッスン終了時 |
| `vo_normal_end` / `da_normal_end` / `vi_normal_end` | 各属性通常レッスン終了時 |
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
| `skill_acquire` | スキルカード獲得時（汎用。SSR/アクティブ/メンタル等の特殊系に該当しない獲得） |
| `skill_enhance` | スキルカード強化時 |
| `skill_delete` | スキルカード削除時 |
| `skill_custom` | スキルカードカスタマイズ時 |
| `skill_change` | スキルカードチェンジ時 |

**アクティブ / メンタルスキルカード操作**

| trigger | 内容 |
|---|---|
| `active_acquire` / `active_enhance` / `active_delete` | アクティブスキルカード獲得/強化/削除時 |
| `mental_acquire` / `mental_enhance` / `mental_delete` | メンタルスキルカード獲得/強化/削除時 |

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

## 育成プラン (Plans/*.yaml)

```yaml
plan:
  id: "hif"
  name: "H.I.F (Hatsuboshi IDOL FESTIVAL)"
  description: "..."
  total_weeks: 29          # 週数 (HIFは日数)
  status_limit: 3000       # 属性ごとのステータス上限
  base_status: { vo: 0, da: 0, vi: 0 }
  schedule:
    - week: 2
      type: "public_lesson"          # 週の種別
      available_actions: ["vo_lesson", "da_lesson", "vi_lesson"]
      lessons:                       # レッスン週: 属性ごとの上昇値
        - type: "vo"
          sp_bonus: { vo: 60, da: 0, vi: 0 }
        # ...
      hif_sub_value: 20              # HIF公開レッスンのサブ属性上昇値
    - week: 3
      type: "free"
      available_actions: ["vo_class", "da_class", "vi_class"]
      classes:                       # 授業週: 属性ごとの上昇値
        - type: "vo"
          sp_bonus: { vo: 120, da: 0, vi: 0 }
        # ...
```

- `available_actions` はその週にユーザーが選択できる行動の一覧（`vo_lesson` / `vo_class` / `outing` / `consultation` / `activity_supply` / `special_training` / `rest` など）
- 試験・オーディション週は基礎値＋Vo/Da/Vi配分値をユーザーが入力する

## イベント回数テンプレート (Templates/event_count_templates.yaml)

「活動支給軸」「相談削除軸」など、プレイスタイル別のイベント発動回数プリセット。

```yaml
templates:
- name: センス（活動支給軸）
  plan_id: hatsu_legend
  week_actions:            # テンプレ適用時に該当週の行動も切り替える
    3: activity_supply
    8: consultation
  counts:                  # trigger 名 → 育成1回あたりの発動回数
    p_drink_acquire: 15
    skill_acquire: 20
    skill_enhance: 4
    # ...
```

## キャラ定義 (Characters/characters.yaml)

```yaml
characters:
  - id: "char_saki"
    name: "花海咲季"
    color: "#E30F25"                 # 公式イメージカラー
    initial: "咲"                     # UI表示用の1文字
    base_status_bonus: { vo: 100, da: 100, vi: 105 }   # plan.base_status に加算
    para_bonus: { vo: 19.5, da: 19.5, vi: 22.5 }        # 週次レッスン上昇値に乗算する% (3凸前提の最大値)
    uncap3_bonus: { vo: 3.0, da: 3.0, vi: 2.0 }         # 3凸レッスンボーナス分 (OFF時に減算)
    step4_bonus:                     # 一部キャラのSTEP4開放分 (ON で加算、デフォルトON)
      base_status_bonus: { vo: 0, da: 0, vi: 0 }
      para_bonus: { vo: 3.0, da: 1.0, vi: 3.0 }
    nia_criteria: "balance"          # NIAオーディション審査基準 (balance / concentrate)
    nia_trend: ["vi", "da", "vo"]    # NIAの流行1/2/3が対応する属性 (未設定キャラは獲得0)
```

## 所持データ (Inventory/inventory.yaml)

CardInventoryManager / Web版所持管理が生成するユーザーデータ。

```yaml
inventory:
- card_id: SP_R_0013
  owned: true
  uncap: 4        # 凸数 0〜4
```

## Wiki同期との対応

カードデータは `scripts/sync_wiki.py` で Seesaa Wiki から差分同期される。Wikiのアビリティ名→ `trigger` の解決は `scripts/wiki_sync/constants.py` の `TRIGGER_MAP` が行う。

表記揺れ（「お出かけ」/「おでかけ」、「スキルカード強化」/「スキル強化」など）を吸収するため、**具体的なキーワードを汎用キーワードより上に配置**している。新規トリガー追加時は順序に注意すること。詳細は [development.md](development.md) を参照。
