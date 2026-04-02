"""
Seesaa Wiki とYAMLデータの差分同期スクリプト。

- 新規カード: Wiki から取得して YAML に追加 + 画像ダウンロード
- 既存カード: 凸別値のみ更新
- 削除は行わない (安全側)

使い方:
  python scripts/sync_wiki.py                  # 差分チェック + 同期
  python scripts/sync_wiki.py --dry-run        # 確認のみ (書き込みなし)
  python scripts/sync_wiki.py --update-only    # 既存カードの値更新のみ
  python scripts/sync_wiki.py --new-only       # 新規カード追加のみ
  python scripts/sync_wiki.py --delay 10       # リクエスト間隔を変更
"""
import urllib.request
import urllib.parse
import re
import time
import yaml
import sys
import argparse
import os
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

WIKI_BASE = "https://seesaawiki.jp/gakumasu"
LIST_URL = f"{WIKI_BASE}/d/%A5%B5%A5%DD%A1%BC%A5%C8%A5%AB%A1%BC%A5%C9%B0%EC%CD%F7"
DATA_DIR = Path(__file__).parent.parent / "Data" / "SupportCards"
IMAGE_DIR = Path(__file__).parent.parent / "Data" / "Images"
MAPPING_FILE = IMAGE_DIR / "_mapping.tsv"
DEFAULT_DELAY = 30


# ======== 文字列正規化 ========

def normalize_name(name: str) -> str:
    """Wiki と YAML 間の文字差異を吸収する正規化"""
    name = name.replace("〜", "～")      # wave dash → fullwidth tilde
    name = name.replace("&#9825;", "♡")  # HTML entity → heart
    name = name.replace("♥", "♡")
    name = name.replace("ω", "ω")       # halfwidth → fullwidth omega (just in case)
    return name


# ======== ネットワーク ========

def fetch_page(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    resp = urllib.request.urlopen(req)
    return resp.read().decode("euc-jp", errors="replace")


def download_file(url: str, filepath: Path):
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    resp = urllib.request.urlopen(req)
    filepath.parent.mkdir(parents=True, exist_ok=True)
    with open(filepath, "wb") as f:
        f.write(resp.read())


# ======== Wiki 一覧ページ解析 ========

class WikiCardEntry:
    """一覧ページから取得できるカード情報"""
    def __init__(self):
        self.name: str = ""
        self.wiki_id: str = ""         # SP_SSR_0092 等
        self.rarity: str = ""          # SSR, SR, R
        self.lesson_support: str = ""  # "ボーカルレッスン/確率大" 等
        self.date: str = ""            # 登場日
        self.detail_url: str = ""      # 詳細ページURL


def extract_cells(row_html: str) -> list[str]:
    cells = re.findall(r"<t[dh][^>]*>(.*?)</t[dh]>", row_html, re.DOTALL)
    return [re.sub(r"<[^>]+>", "", c).strip() for c in cells]


def parse_list_page() -> list[WikiCardEntry]:
    """一覧ページからカード情報を抽出"""
    html = fetch_page(LIST_URL)

    # カード一覧テーブルを探す (178行以上の最大テーブル)
    tables = re.findall(r"<table[^>]*>(.*?)</table>", html, re.DOTALL)
    card_table = None
    for t in tables:
        rows = re.findall(r"<tr[^>]*>(.*?)</tr>", t, re.DOTALL)
        if len(rows) > 50:
            card_table = t
            break

    if not card_table:
        print("ERROR: カード一覧テーブルが見つかりません")
        return []

    rows = re.findall(r"<tr[^>]*>(.*?)</tr>", card_table, re.DOTALL)

    # リンクのマップを作成 (カード名→URL)
    link_map: dict[str, str] = {}
    raw_links = re.findall(
        r'href="(https://seesaawiki\.jp/gakumasu/d/([^"]+))"', html
    )
    for full_url, path in raw_links:
        try:
            name = urllib.parse.unquote(path, encoding="euc-jp")
        except Exception:
            continue
        if name not in link_map:
            link_map[name] = full_url

    entries = []
    for row in rows[1:]:  # ヘッダー行スキップ
        cells = extract_cells(row)
        if len(cells) < 18:
            continue

        rarity = cells[0].strip()
        if rarity not in ("SSR", "SR", "R"):
            continue

        # カード名(wiki_id) を解析
        name_id = cells[2].strip()
        m = re.match(r"(.+?)\((SP_\w+_\d+)\)", name_id)
        if not m:
            continue

        entry = WikiCardEntry()
        entry.name = m.group(1).strip()
        entry.wiki_id = m.group(2).strip()
        entry.rarity = rarity
        entry.lesson_support = cells[16].strip() if len(cells) > 16 else ""
        entry.date = cells[17].strip() if len(cells) > 17 else ""

        # 詳細ページURL
        entry.detail_url = link_map.get(entry.name, "")

        entries.append(entry)

    return entries


def guess_type_from_lesson(lesson_support: str) -> str:
    """レッスンサポート文字列からカードタイプ (vo/da/vi) を推定"""
    # 先頭の制御文字を除去
    clean = lesson_support.lstrip("ｳｸｽ")
    if "ボーカル" in clean:
        return "vo"
    elif "ダンス" in clean:
        return "da"
    elif "ビジュアル" in clean:
        return "vi"
    return "vo"


def guess_plan_from_lesson(lesson_support: str) -> str:
    """レッスンサポート先頭文字からプラン制限を推定 (詳細ページで上書き)"""
    if lesson_support.startswith("ｳ"):
        return "logic"
    elif lesson_support.startswith("ｸ"):
        return "sense"
    elif lesson_support.startswith("ｽ"):
        return "anomaly"
    return "free"


# ======== Wiki 詳細ページ解析 ========

def parse_detail_page(url: str) -> dict | None:
    """
    詳細ページからメタデータ + アビリティ凸別値 + 画像URL を取得。

    Returns: {
        "plan": str,
        "type": str,
        "abilities": [{unlock, name, uncap_values}],
        "image_url": str | None,
    }
    """
    html = fetch_page(url)
    result: dict = {}

    # --- メタデータ (ページ上部テキスト) ---
    body_start = html.find("user-area")
    body_text = html[body_start:body_start + 3000] if body_start >= 0 else html[:3000]
    text = re.sub(r"<[^>]+>", "\n", body_text)
    lines = [l.strip() for l in text.split("\n") if l.strip()]

    # プラン制限を探す
    result["plan"] = ""
    for i, line in enumerate(lines):
        if "プラン制限" in line and i + 1 < len(lines):
            plan_text = lines[i + 1]
            plan_map = {"センス": "sense", "ロジック": "logic", "アノマリー": "anomaly"}
            result["plan"] = plan_map.get(plan_text, "free")
            break

    # タイプを探す
    result["type"] = ""
    for i, line in enumerate(lines):
        if line == "タイプ" or line == "レッスンサポート":
            # 次の数行にタイプ情報がある
            for j in range(i + 1, min(i + 4, len(lines))):
                if "ボーカル" in lines[j]:
                    result["type"] = "vo"
                    break
                elif "ダンス" in lines[j]:
                    result["type"] = "da"
                    break
                elif "ビジュアル" in lines[j]:
                    result["type"] = "vi"
                    break
            if result["type"]:
                break

    # --- 凸別テーブル ---
    tables = re.findall(r"<table[^>]*>(.*?)</table>", html, re.DOTALL)
    result["abilities"] = []

    for table in tables:
        rows = re.findall(r"<tr[^>]*>(.*?)</tr>", table, re.DOTALL)
        if len(rows) < 4:
            continue

        row0_text = " ".join(extract_cells(rows[0]))
        row1_text = " ".join(extract_cells(rows[1]))

        # サマリーテーブル: Row 0 に「解放条件」、Row 1 に「上限解放」がある
        # レベル別詳細テーブル（Table 3〜8）は Row 0 に「解放条件」がない
        if "解放条件" not in row0_text:
            continue
        if "上限解放" not in row1_text:
            continue

        for row in rows[3:]:
            cells = extract_cells(row)
            if len(cells) < 7:
                continue
            # 末尾5列が凸別値 (9列:cells[4:9], 8列:cells[3:8])
            uncap_values = [c.strip() for c in cells[-5:]]
            result["abilities"].append({
                "unlock": cells[0].strip(),
                "name": cells[1].strip(),
                "uncap_values": uncap_values,
            })
        break

    # --- カード画像 (ページ最初の seesaa 画像) ---
    result["image_url"] = None
    img_pattern = r'src="(https://image\d+\.seesaawiki\.jp/g/u/gakumasu/[^"]+\.(png|jpg|jpeg))"'
    img_match = re.search(img_pattern, html, re.IGNORECASE)
    if img_match:
        result["image_url"] = img_match.group(1)

    return result


# ======== アビリティ → effect マッチング (既存スクリプトから流用) ========

def parse_value(s: str) -> float | None:
    s = s.strip().replace("%", "").replace(",", "")
    if not s:
        return None
    try:
        return float(s)
    except ValueError:
        return None


def classify_and_match(effects: list[dict], wiki_abilities: list[dict]) -> tuple[bool, list[str]]:
    """既存effectsの値をWikiアビリティで更新する"""
    updated = False
    logs = []
    matched_ids: set[int] = set()  # マッチ済みeffectのid()

    for wiki_abi in wiki_abilities:
        name = wiki_abi["name"]
        raw_values = wiki_abi["uncap_values"]

        values = [parse_value(v) for v in raw_values]
        # 空セルは直前の有効値で補完
        for idx in range(len(values)):
            if values[idx] is None:
                values[idx] = values[idx - 1] if idx > 0 and values[idx - 1] is not None else 0.0
        if all(v == 0.0 for v in values):
            continue
        values = [float(v) for v in values]

        stat = None
        for s, kw in [("vo", "ボーカル"), ("da", "ダンス"), ("vi", "ビジュアル")]:
            if kw in name:
                stat = s
                break

        target_trigger = None
        target_vtype = "flat"
        match_strategy = "default"

        if re.search(r"初期.+上昇", name):
            target_trigger = "equip"
            target_vtype = "flat"
            match_strategy = "equip_flat_ability"
        elif "レッスンサポート発生率" in name:
            continue
        elif "SP" in name and "発生率" in name and "終了" not in name:
            target_trigger = "equip"
            target_vtype = "sp_rate"
        elif "イベント" in name and "パラメータ" in name:
            # イベントパラメータ上昇はカード固有のパラボとは別物。スキップ
            continue
        elif "パラメータボーナス" in name or "レッスンボーナス" in name:
            # カード固有のパラメータボーナス/レッスンボーナス
            target_trigger = "equip"
            target_vtype = "para_bonus"
        else:
            trigger_map = [
                ("活動支給", "activity_supply"), ("差し入れ", "activity_supply"),
                ("スキルカード（SSR）獲得", "skill_ssr_acquire"),
                ("スキルカード(SSR)獲得", "skill_ssr_acquire"),
                ("スキル強化", "skill_enhance"), ("スキル削除", "skill_delete"),
                ("スキルカスタム", "skill_custom"), ("スキルチェンジ", "skill_change"),
                ("アクティブスキルカード強化", "active_enhance"),
                ("アクティブ強化", "active_enhance"),
                ("アクティブスキルカード削除", "active_delete"),
                ("アクティブ削除", "active_delete"),
                ("アクティブスキルカード獲得", "active_acquire"),
                ("アクティブ獲得", "active_acquire"),
                ("SPレッスン終了", "sp_end"),
                ("ボーカルSPレッスン終了", "vo_sp_end"),
                ("ダンスSPレッスン終了", "da_sp_end"),
                ("ビジュアルSPレッスン終了", "vi_sp_end"),
                ("ボーカルレッスン終了", "vo_lesson_end"),
                ("ダンスレッスン終了", "da_lesson_end"),
                ("ビジュアルレッスン終了", "vi_lesson_end"),
                ("レッスン終了", "lesson_end"),
                ("ボーカル通常レッスン終了", "vo_normal_end"),
                ("ダンス通常レッスン終了", "da_normal_end"),
                ("ビジュアル通常レッスン終了", "vi_normal_end"),
                ("授業終了", "class_end"), ("お出かけ終了", "outing_end"),
                ("相談Pドリンク", "consultation_drink"), ("相談選択", "consultation"),
                ("試験終了", "exam_end"), ("特別指導", "special_training"),
                ("Pアイテム獲得", "p_item_acquire"), ("Pドリンク獲得", "p_drink_acquire"),
                ("休憩選択", "rest"), ("休憩", "rest"),
                ("元気効果", "genki_acquire"), ("元気カード獲得", "genki_acquire"),
                ("元気獲得", "genki_acquire"),
                ("好調効果", "good_condition_acquire"),
                ("好調カード獲得", "good_condition_acquire"),
                ("好印象効果", "good_impression_acquire"),
                ("好印象カード獲得", "good_impression_acquire"),
                ("温存効果", "conserve_acquire"), ("温存カード獲得", "conserve_acquire"),
                ("集中効果", "good_condition_acquire"),
                ("やる気効果", "good_impression_acquire"),
                ("根気効果", "conserve_acquire"),
                ("メンタルスキルカード獲得", "mental_acquire"),
                ("メンタル獲得", "mental_acquire"), ("メンタル強化", "mental_enhance"),
            ]
            for keyword, trig in trigger_map:
                if keyword in name:
                    target_trigger = trig
                    break

        if target_trigger is None:
            continue

        matched_effect = None
        if match_strategy == "equip_flat_ability":
            candidates = [
                e for e in effects
                if id(e) not in matched_ids
                and e.get("trigger") == "equip"
                and e.get("value_type") == "flat"
                and (stat is None or e.get("stat") == stat)
            ]
            if len(candidates) >= 2:
                candidates.sort(
                    key=lambda e: e.get("values", [0])[min(4, len(e.get("values", [0])) - 1)],
                    reverse=True,
                )
                matched_effect = candidates[0]
            elif len(candidates) == 1:
                matched_effect = candidates[0]
        else:
            for e in effects:
                if id(e) in matched_ids:
                    continue
                if (e.get("trigger") == target_trigger
                    and e.get("value_type") == target_vtype
                    and (stat is None or e.get("stat") == stat)):
                    matched_effect = e
                    break

        if matched_effect is None:
            continue

        matched_ids.add(id(matched_effect))

        old_values = matched_effect.get("values", [])
        if old_values != values:
            matched_effect["values"] = values
            updated = True
            logs.append(f"    ✓ {target_trigger}/{target_vtype}: {old_values} → {values}")

    return updated, logs


# ======== 新規カード作成 ========

def build_new_card(
    card_id: str,
    entry: WikiCardEntry,
    detail: dict,
    abilities: list[dict],
) -> dict:
    """Wikiデータから新規カードYAMLエントリを構築"""
    card_type = detail.get("type") or guess_type_from_lesson(entry.lesson_support)
    plan = detail.get("plan") or guess_plan_from_lesson(entry.lesson_support)
    stat = "all" if card_type == "as" else card_type

    effects = []
    for abi in abilities:
        name = abi["name"]
        raw_values = abi["uncap_values"]
        values = [parse_value(v) for v in raw_values]
        # 空セルは直前の有効値で補完 (Wiki未入力部分への対策)
        for idx in range(len(values)):
            if values[idx] is None:
                values[idx] = values[idx - 1] if idx > 0 and values[idx - 1] is not None else 0.0
        if all(v == 0.0 for v in values):
            continue
        values = [float(v) for v in values]

        # 分類
        abi_stat = stat
        for s, kw in [("vo", "ボーカル"), ("da", "ダンス"), ("vi", "ビジュアル")]:
            if kw in name:
                abi_stat = s
                break

        trigger = None
        value_type = "flat"

        if re.search(r"初期.+上昇", name):
            trigger = "equip"
        elif "レッスンサポート発生率" in name:
            continue  # ステ計算に不要
        elif "SP" in name and "発生率" in name and "終了" not in name:
            trigger = "equip"
            value_type = "sp_rate"
        elif "イベント" in name and "パラメータ" in name:
            continue  # イベントパラメータ上昇はカード固有のパラボとは別物
        elif "パラメータボーナス" in name or "レッスンボーナス" in name:
            trigger = "equip"
            value_type = "para_bonus"
        else:
            trigger_map = [
                ("活動支給", "activity_supply"), ("差し入れ", "activity_supply"),
                ("スキルカード（SSR）獲得", "skill_ssr_acquire"),
                ("スキルカード(SSR)獲得", "skill_ssr_acquire"),
                ("スキル強化", "skill_enhance"), ("スキル削除", "skill_delete"),
                ("スキルカスタム", "skill_custom"), ("スキルチェンジ", "skill_change"),
                ("アクティブスキルカード強化", "active_enhance"),
                ("アクティブ強化", "active_enhance"),
                ("アクティブスキルカード削除", "active_delete"),
                ("アクティブ削除", "active_delete"),
                ("アクティブスキルカード獲得", "active_acquire"),
                ("アクティブ獲得", "active_acquire"),
                ("SPレッスン終了", "sp_end"),
                ("ボーカルSPレッスン終了", "vo_sp_end"),
                ("ダンスSPレッスン終了", "da_sp_end"),
                ("ビジュアルSPレッスン終了", "vi_sp_end"),
                ("ボーカルレッスン終了", "vo_lesson_end"),
                ("ダンスレッスン終了", "da_lesson_end"),
                ("ビジュアルレッスン終了", "vi_lesson_end"),
                ("レッスン終了", "lesson_end"),
                ("ボーカル通常レッスン終了", "vo_normal_end"),
                ("ダンス通常レッスン終了", "da_normal_end"),
                ("ビジュアル通常レッスン終了", "vi_normal_end"),
                ("授業終了", "class_end"), ("お出かけ終了", "outing_end"),
                ("相談Pドリンク", "consultation_drink"), ("相談選択", "consultation"),
                ("試験終了", "exam_end"), ("特別指導", "special_training"),
                ("Pアイテム獲得", "p_item_acquire"), ("Pドリンク獲得", "p_drink_acquire"),
                ("休憩選択", "rest"), ("休憩", "rest"),
                ("元気効果", "genki_acquire"), ("元気カード獲得", "genki_acquire"),
                ("元気獲得", "genki_acquire"),
                ("好調効果", "good_condition_acquire"),
                ("好調カード獲得", "good_condition_acquire"),
                ("集中効果", "good_condition_acquire"),
                ("好印象効果", "good_impression_acquire"),
                ("好印象カード獲得", "good_impression_acquire"),
                ("やる気効果", "good_impression_acquire"),
                ("温存効果", "conserve_acquire"), ("温存カード獲得", "conserve_acquire"),
                ("根気効果", "conserve_acquire"),
                ("メンタルスキルカード獲得", "mental_acquire"),
                ("メンタル獲得", "mental_acquire"), ("メンタル強化", "mental_enhance"),
            ]
            for keyword, trig in trigger_map:
                if keyword in name:
                    trigger = trig
                    break

        if trigger is None:
            continue

        # max_count 抽出
        max_count = None
        mc_match = re.search(r"(\d+)回", name)
        if mc_match:
            max_count = int(mc_match.group(1))

        # condition 抽出
        condition = None
        cond_match = re.search(r"(\w+)が(\d+)\w*以上", name)
        if cond_match:
            cond_key = cond_match.group(1)
            cond_val = cond_match.group(2)
            cond_map = {"所持スキルカード": "deck"}
            key = cond_map.get(cond_key, cond_key)
            condition = f"{key}>={cond_val}"

        eff: dict = {
            "trigger": trigger,
            "stat": abi_stat,
            "values": values,
            "value_type": value_type,
        }
        if max_count:
            eff["max_count"] = max_count
        if condition:
            eff["condition"] = condition

        effects.append(eff)

    return {
        "id": card_id,
        "name": entry.name,
        "rarity": entry.rarity,
        "type": card_type,
        "plan": plan,
        "effects": effects,
    }


# ======== 更新済み記録 ========

SYNCED_FILE = DATA_DIR / "_synced.txt"


def load_synced() -> set[str]:
    """更新済みカード名セットを読み込む"""
    if not SYNCED_FILE.exists():
        return set()
    with open(SYNCED_FILE, "r", encoding="utf-8") as f:
        return {line.strip() for line in f if line.strip()}


def save_synced(names: set[str]):
    """更新済みカード名セットを保存する"""
    with open(SYNCED_FILE, "w", encoding="utf-8") as f:
        for name in sorted(names):
            f.write(name + "\n")


# ======== YAML I/O ========

def load_yaml_cards(filepath: str) -> list[dict]:
    with open(filepath, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return data.get("support_cards", [])


def write_yaml_with_flow_values(filepath: str, cards: list[dict]):
    lines = ["support_cards:"]
    for card in cards:
        lines.append(f"- id: {card['id']}")
        lines.append(f"  name: {card['name']}")
        lines.append(f"  rarity: {card['rarity']}")
        lines.append(f"  type: {card['type']}")
        lines.append(f"  plan: {card['plan']}")
        lines.append("  effects:")
        for eff in card.get("effects", []):
            lines.append(f"  - trigger: {eff['trigger']}")
            lines.append(f"    stat: {eff['stat']}")
            vals = eff.get("values", [])
            val_strs = [str(int(v)) if v == int(v) else str(v) for v in vals]
            lines.append(f"    values: [{', '.join(val_strs)}]")
            lines.append(f"    value_type: {eff['value_type']}")
            if eff.get("max_count") is not None:
                lines.append(f"    max_count: {eff['max_count']}")
            if eff.get("condition"):
                lines.append(f"    condition: {eff['condition']}")
            if eff.get("description"):
                lines.append(f"    description: {eff['description']}")
    with open(filepath, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def load_mapping() -> dict[str, dict]:
    """_mapping.tsv を {card_name: {card_id, filename}} で返す"""
    mapping = {}
    if MAPPING_FILE.exists():
        with open(MAPPING_FILE, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line.startswith("card_id") or not line:
                    continue
                parts = line.split("\t")
                if len(parts) >= 3:
                    mapping[parts[1]] = {"card_id": parts[0], "filename": parts[2]}
    return mapping


def append_mapping(wiki_id: str, card_name: str, filename: str):
    with open(MAPPING_FILE, "a", encoding="utf-8") as f:
        f.write(f"{wiki_id}\t{card_name}\t{filename}\n")


def next_card_id(existing_cards: list[dict], rarity: str) -> str:
    prefix = rarity.lower()
    max_num = 0
    for card in existing_cards:
        m = re.match(rf"{prefix}_(\d+)", card["id"])
        if m:
            max_num = max(max_num, int(m.group(1)))
    return f"{prefix}_{max_num + 1:03d}"


# ======== メイン ========

def main():
    parser = argparse.ArgumentParser(description="Wiki差分同期")
    parser.add_argument("--dry-run", action="store_true", help="書き込みしない")
    parser.add_argument("--update-only", action="store_true", help="既存カードの値更新のみ")
    parser.add_argument("--new-only", action="store_true", help="新規カード追加のみ")
    parser.add_argument("--force", action="store_true", help="更新済み記録を無視して全件更新")
    parser.add_argument("--delay", type=int, default=DEFAULT_DELAY, help="リクエスト間隔(秒)")
    args = parser.parse_args()

    print("=== Wiki 差分同期 ===")
    if args.dry_run:
        print("  [DRY-RUN]")
    if args.force:
        print("  [FORCE: 全件更新]")
    print()

    # 1. Wiki 一覧ページ取得
    print("一覧ページを取得中...")
    wiki_entries = parse_list_page()
    print(f"  Wikiカード: {len(wiki_entries)} 枚")

    # 2. 既存YAML読み込み
    yaml_files = {
        "SSR": str(DATA_DIR / "ssr_cards.yaml"),
        "SR": str(DATA_DIR / "sr_cards.yaml"),
        "R": str(DATA_DIR / "r_cards.yaml"),
    }
    existing_cards: dict[str, dict] = {}  # {name: {card, file}}
    all_by_file: dict[str, list] = {}
    for rarity, filepath in yaml_files.items():
        if os.path.exists(filepath):
            cards = load_yaml_cards(filepath)
            all_by_file[filepath] = cards
            for card in cards:
                existing_cards[card["name"]] = {"card": card, "file": filepath}
        else:
            all_by_file[filepath] = []

    print(f"  既存カード: {len(existing_cards)} 枚")

    # 3. 画像マッピング読み込み (正規化名でもルックアップ可能にする)
    image_mapping = load_mapping()
    norm_image_mapping = {normalize_name(k): v for k, v in image_mapping.items()}

    # 4. 差分検出 (名前の正規化で表記揺れを吸収)
    synced = load_synced() if not args.force else set()

    norm_to_existing: dict[str, str] = {}  # normalized_name → original_name
    for name in existing_cards:
        norm_to_existing[normalize_name(name)] = name

    new_entries = []
    update_entries = []
    skipped_count = 0

    for entry in wiki_entries:
        norm_name = normalize_name(entry.name)
        if norm_name in norm_to_existing:
            # 既存カード → Wiki名を既存名に揃えておく
            entry.name = norm_to_existing[norm_name]
            if entry.detail_url:
                if entry.name in synced:
                    skipped_count += 1
                else:
                    update_entries.append(entry)
        else:
            if entry.detail_url:
                new_entries.append(entry)

    print(f"\n  新規: {len(new_entries)} 枚")
    for e in new_entries:
        print(f"    + {e.rarity} {e.name}")

    print(f"  更新対象: {len(update_entries)} 枚")
    if skipped_count > 0:
        print(f"  スキップ (更新済み): {skipped_count} 枚")
    print()

    if not new_entries and not update_entries:
        print("差分なし。終了。")
        return

    # 5. 処理対象を決定
    to_process = []
    if not args.update_only:
        for e in new_entries:
            to_process.append(("new", e))
    if not args.new_only:
        for e in update_entries:
            to_process.append(("update", e))

    if not to_process:
        print("処理対象なし。終了。")
        return

    # 6. 各カード処理
    new_count = 0
    update_count = 0
    img_count = 0
    modified_files = set()

    for i, (action, entry) in enumerate(to_process):
        label = "NEW" if action == "new" else "UPD"
        print(f"[{i+1}/{len(to_process)}] [{label}] {entry.name}")

        try:
            detail = parse_detail_page(entry.detail_url)
            if detail is None:
                print("  ⚠ 詳細ページ解析失敗")
                continue

            if action == "new":
                # 新規カード追加
                rarity = entry.rarity
                filepath = yaml_files[rarity]
                card_id = next_card_id(all_by_file[filepath], rarity)

                card = build_new_card(card_id, entry, detail, detail.get("abilities", []))
                if not card["effects"]:
                    print(f"  ⚠ effects が空です (アビリティ取得失敗)")
                else:
                    print(f"  id={card_id}, type={card['type']}, plan={card['plan']}, effects={len(card['effects'])}")
                    if not args.dry_run:
                        all_by_file[filepath].append(card)
                        modified_files.add(filepath)
                    new_count += 1

                # 画像ダウンロード
                if detail.get("image_url") and normalize_name(entry.name) not in norm_image_mapping:
                    img_url = detail["image_url"]
                    ext = img_url.rsplit(".", 1)[-1].lower()
                    if ext not in ("png", "jpg", "jpeg"):
                        ext = "png"
                    img_filename = f"{entry.wiki_id}.{ext}"
                    img_path = IMAGE_DIR / img_filename
                    print(f"  画像: {img_filename}")
                    if not args.dry_run:
                        download_file(img_url, img_path)
                        append_mapping(entry.wiki_id, entry.name, img_filename)
                        img_count += 1

            elif action == "update":
                # 既存カード値更新
                info = existing_cards[entry.name]
                abilities = detail.get("abilities", [])
                if abilities:
                    was_updated, logs = classify_and_match(info["card"]["effects"], abilities)
                    for log in logs:
                        print(log)
                    if was_updated:
                        update_count += 1
                        modified_files.add(info["file"])
                        print("  → 更新あり")
                    else:
                        print("  → 変更なし")
                else:
                    print("  - テーブルなし")

                # 画像が未取得なら取得
                if detail.get("image_url") and normalize_name(entry.name) not in norm_image_mapping:
                    img_url = detail["image_url"]
                    ext = img_url.rsplit(".", 1)[-1].lower()
                    if ext not in ("png", "jpg", "jpeg"):
                        ext = "png"
                    img_filename = f"{entry.wiki_id}.{ext}"
                    img_path = IMAGE_DIR / img_filename
                    print(f"  画像: {img_filename}")
                    if not args.dry_run:
                        download_file(img_url, img_path)
                        append_mapping(entry.wiki_id, entry.name, img_filename)
                        img_count += 1

        except Exception as e:
            print(f"  ✗ エラー: {e}")

        if i < len(to_process) - 1:
            time.sleep(args.delay)

    # 7. ファイル保存
    if modified_files and not args.dry_run:
        print("\nファイル保存中...")
        for filepath in modified_files:
            write_yaml_with_flow_values(filepath, all_by_file[filepath])
            print(f"  保存: {filepath}")

    # 8. 更新済み記録を保存
    if not args.dry_run:
        # 処理成功したカード名を記録に追加
        synced_names = load_synced()
        for action, entry in to_process:
            synced_names.add(entry.name)
        save_synced(synced_names)
        print(f"  更新済み記録: {len(synced_names)} 枚")

    print(f"\n=== 完了 ===")
    print(f"  新規追加: {new_count} 枚")
    print(f"  値更新: {update_count} 枚")
    print(f"  画像取得: {img_count} 件")


if __name__ == "__main__":
    main()
