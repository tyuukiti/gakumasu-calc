"""定数定義・文字列正規化・値変換"""
import re
from pathlib import Path

WIKI_BASE = "https://seesaawiki.jp/gakumasu"
LIST_URL = f"{WIKI_BASE}/d/%A5%B5%A5%DD%A1%BC%A5%C8%A5%AB%A1%BC%A5%C9%B0%EC%CD%F7"
TAG_PAGE_URL = f"{WIKI_BASE}/d/%A5%B5%A5%DD%A1%BC%A5%C8%A5%AB%A1%BC%A5%C9%C7%BD%CE%CF%C1%E1%B8%AB%C9%BD"
DATA_DIR = Path(__file__).parent.parent.parent / "Data" / "SupportCards"
IMAGE_DIR = Path(__file__).parent.parent.parent / "Data" / "Images"
MAPPING_FILE = IMAGE_DIR / "_mapping.tsv"
SYNCED_FILE = DATA_DIR / "_synced.txt"
DEFAULT_DELAY = 30

# カテゴリキーワード → tag値 のマッピング
SECTION_TAG_MAP = [
    ("スキルカード", "skill"),
    ("コンテストアイテム", "exam_item"),
    ("プロデュースアイテム", "produce_item"),
]

# アビリティ名 → trigger のマッピング (具体的なキーワードを汎用より先に配置)
TRIGGER_MAP = [
    ("活動支給", "activity_supply"), ("差し入れ", "activity_supply"),
    ("スキルカード（SSR）獲得", "skill_ssr_acquire"),
    ("スキルカード(SSR)獲得", "skill_ssr_acquire"),
    # 具体 (アクティブ系) → 派生 (メンタル系) → 汎用 (スキルカード系) → 旧表記の順
    ("アクティブスキルカード強化", "active_enhance"),
    ("アクティブ強化", "active_enhance"),
    ("アクティブスキルカード削除", "active_delete"),
    ("アクティブ削除", "active_delete"),
    ("アクティブスキルカード獲得", "active_acquire"),
    ("アクティブ獲得", "active_acquire"),
    ("メンタルスキルカード強化", "mental_enhance"),
    ("メンタルスキルカード削除", "mental_delete"),
    ("メンタルスキルカード獲得", "mental_acquire"),
    # 状態効果系「○○効果のスキルカード獲得時」: 汎用「スキルカード獲得」より先に判定する。
    # 例:「集中効果のスキルカード獲得時」を skill_acquire ではなく concentrate_acquire にする。
    ("集中効果", "concentrate_acquire"),
    ("好調効果", "good_condition_acquire"),
    ("好印象効果", "good_impression_acquire"),
    ("温存効果", "conserve_acquire"),
    ("やる気効果", "motivation_acquire"),
    ("全力効果", "fullpower_acquire"),
    ("強気効果", "aggressive_acquire"),
    ("根気効果", "conserve_acquire"),
    ("元気効果", "genki_acquire"),
    # 汎用「スキルカード獲得」: SSR/アクティブ/メンタル等の特殊系の後ろに置き、
    # specific patterns に取られなかった場合のフォールバックとしてマッチさせる
    ("スキルカード獲得", "skill_acquire"),
    ("スキルカード強化", "skill_enhance"),
    ("スキルカード削除", "skill_delete"),
    ("スキルカードカスタマイズ", "skill_custom"),
    ("スキルカードカスタム", "skill_custom"),
    ("スキルカードチェンジ", "skill_change"),
    ("スキル強化", "skill_enhance"), ("スキル削除", "skill_delete"),
    ("スキルカスタム", "skill_custom"), ("スキルチェンジ", "skill_change"),
    # SPレッスン系 (具体→汎用の順)
    ("ボーカルSPレッスン終了", "vo_sp_end"),
    ("ダンスSPレッスン終了", "da_sp_end"),
    ("ビジュアルSPレッスン終了", "vi_sp_end"),
    ("SPレッスン終了", "sp_end"),
    # レッスン系 (具体→汎用の順)
    ("ボーカルレッスン終了", "vo_lesson_end"),
    ("ダンスレッスン終了", "da_lesson_end"),
    ("ビジュアルレッスン終了", "vi_lesson_end"),
    ("ボーカル通常レッスン終了", "vo_normal_end"),
    ("ダンス通常レッスン終了", "da_normal_end"),
    ("ビジュアル通常レッスン終了", "vi_normal_end"),
    ("レッスン終了", "lesson_end"),
    # その他
    ("授業・営業終了", "class_end"), ("授業終了", "class_end"),
    ("お出かけ終了", "outing_end"), ("おでかけ終了", "outing_end"),
    ("相談でPドリンク交換", "consultation_drink"),
    ("相談Pドリンク", "consultation_drink"), ("相談選択", "consultation"),
    ("試験終了", "exam_end"), ("試験・オーディション終了", "exam_end"),
    ("特別指導", "special_training"),
    ("Pアイテム獲得", "p_item_acquire"), ("Pドリンク獲得", "p_drink_acquire"),
    ("休む選択", "rest"), ("休憩選択", "rest"), ("休憩", "rest"),
    # 「○○効果」の状態効果系は上部（汎用スキルカード獲得より前）で定義済み。
    # ここでは「○○カード獲得」「○○獲得」表記のみ残す。
    ("元気カード獲得", "genki_acquire"),
    ("元気獲得", "genki_acquire"),
    ("好調カード獲得", "good_condition_acquire"),
    ("好印象カード獲得", "good_impression_acquire"),
    ("温存カード獲得", "conserve_acquire"),
    ("メンタルスキルカード獲得", "mental_acquire"),
    ("メンタル獲得", "mental_acquire"), ("メンタル強化", "mental_enhance"),
    # 「スキル獲得」 (旧表記/短縮表記) 用の fallback。
    # ※「スキルカード獲得」を含むアビリティはこの行に到達する前に上記の汎用 skill_acquire でマッチする。
    ("スキル獲得", "skill_acquire"),
]

# アイテム効果用トリガーマッピング
ITEM_TRIGGER_MAP = [
    ("集中効果", "concentrate_acquire"),
    ("やる気効果", "motivation_acquire"),
    ("全力効果", "fullpower_acquire"),
    ("強気効果", "aggressive_acquire"),
    ("好調効果", "good_condition_acquire"),
    ("好印象効果", "good_impression_acquire"),
    ("温存効果", "conserve_acquire"),
    ("元気効果", "genki_acquire"),
    ("特別指導", "special_training"),
    ("Voレッスン終了", "vo_lesson_end"),
    ("Daレッスン終了", "da_lesson_end"),
    ("Viレッスン終了", "vi_lesson_end"),
    ("VoSP終了", "vo_sp_end"),
    ("DaSP終了", "da_sp_end"),
    ("ViSP終了", "vi_sp_end"),
    ("試験・オーディション終了", "exam_end"),
    ("お出かけ終了", "outing_end"),
    ("授業・営業終了", "class_end"),
    ("レッスン終了", "lesson_end"),
    ("活動支給・差し入れ", "activity_supply"),
    ("相談選択", "consultation"),
]


def normalize_name(name: str) -> str:
    """Wiki と YAML 間の文字差異を吸収する正規化"""
    name = name.replace("〜", "～")      # wave dash → fullwidth tilde
    name = name.replace("&#9825;", "♡")  # HTML entity → heart
    name = name.replace("♥", "♡")
    name = name.replace("ω", "ω")       # halfwidth → fullwidth omega (just in case)
    return name


def parse_value(s: str) -> float | None:
    """凸別値のセル文字列を数値に変換"""
    s = s.strip().replace("%", "").replace(",", "")
    if not s:
        return None
    try:
        return float(s)
    except ValueError:
        return None


def parse_uncap_values(raw_values: list[str]) -> list[float] | None:
    """凸別値リストをパース。
    - 全要素が空 → None
    - 中間・末尾の空セル → 直前の有効値で補完
    - 先頭の連続空セル → 後ろにある最初の有効値で補完
      (Wiki が完凸値のみ書いて他凸を空欄にしているアビリティで、
       0凸時点で既に取得・発動するものを 0 にしないため)
    """
    values = [parse_value(v) for v in raw_values]
    # 先頭の連続 None を後ろの最初の有効値で埋める
    first_valid = next((v for v in values if v is not None), None)
    if first_valid is None:
        return None
    for idx in range(len(values)):
        if values[idx] is not None:
            break
        values[idx] = first_valid
    # 中間・末尾の None は直前の値で補完
    for idx in range(len(values)):
        if values[idx] is None:
            values[idx] = values[idx - 1]
    if all(v == 0.0 for v in values):
        return None
    return [float(v) for v in values]
