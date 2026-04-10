"""Wiki HTMLページの解析 (BeautifulSoup使用)"""
import re
import urllib.parse
from bs4 import BeautifulSoup, Tag

from .constants import (
    LIST_URL, TAG_PAGE_URL,
    ITEM_TRIGGER_MAP,
    normalize_name, parse_value,
)
from .network import fetch_page


class WikiCardEntry:
    """一覧ページから取得できるカード情報"""
    def __init__(self):
        self.name: str = ""
        self.wiki_id: str = ""         # SP_SSR_0092 等
        self.rarity: str = ""          # SSR, SR, R
        self.lesson_support: str = ""  # "ボーカルレッスン/確率大" 等
        self.date: str = ""            # 登場日
        self.detail_url: str = ""      # 詳細ページURL


# ======== タグページ解析 ========

def fetch_card_tags(html: str | None = None) -> tuple[dict[str, str], str]:
    """
    能力早見表ページからカード名→タグのマッピングを取得。
    Returns: (tag_map, raw_html)
    """
    if html is None:
        try:
            html = fetch_page(TAG_PAGE_URL)
        except Exception as e:
            print(f"  ⚠ タグページ取得失敗: {e}")
            return {}, ""

    category_patterns = [
        (r'スキルカード', "skill"),
        (r'コンテスト.{0,10}アイテム', "exam_item"),
        (r'プロデュース.{0,10}アイテム', "produce_item"),
    ]

    result: dict[str, str] = {}
    soup = BeautifulSoup(html, "html.parser")

    for row in soup.find_all("tr"):
        cells = row.find_all("td")
        card_name = None
        card_tag = None

        for cell in cells:
            # カード名: 詳細ページへのリンク
            if card_name is None:
                link = cell.find("a", href=re.compile(r"seesaawiki\.jp/gakumasu/d/"))
                if link and link.get("href"):
                    try:
                        path = re.search(r"/d/([^#]+)", link["href"])
                        if path:
                            card_name = normalize_name(
                                urllib.parse.unquote(path.group(1), encoding="euc-jp").strip()
                            )
                    except Exception:
                        pass

            # カテゴリセル: 白色ソートキーspanを持つセル
            if card_tag is None:
                cell_html = str(cell)
                if "color:#ffffff" in cell_html or "color: #ffffff" in cell_html:
                    cell_text = cell.get_text()
                    for pattern, tag in category_patterns:
                        if re.search(pattern, cell_text):
                            card_tag = tag
                            break

        if card_name and card_tag:
            result[card_name] = card_tag

    return result, html


def fetch_item_effects(html: str) -> dict[str, dict]:
    """
    能力早見表ページから produce_item カードのアイテム効果を抽出。
    Returns: {card_name: {trigger, stat, values, value_type, ...}}
    """
    result: dict[str, dict] = {}
    soup = BeautifulSoup(html, "html.parser")

    for row in soup.find_all("tr"):
        cells = row.find_all("td")

        card_name = None
        is_produce_item = False

        for cell in cells:
            if card_name is None:
                link = cell.find("a", href=re.compile(r"seesaawiki\.jp/gakumasu/d/"))
                if link and link.get("href"):
                    try:
                        path = re.search(r"/d/([^#]+)", link["href"])
                        if path:
                            card_name = normalize_name(
                                urllib.parse.unquote(path.group(1), encoding="euc-jp").strip()
                            )
                    except Exception:
                        pass

            cell_html = str(cell)
            if "color:#ffffff" in cell_html or "color: #ffffff" in cell_html:
                if re.search(r'プロデュース.{0,10}アイテム', cell.get_text()):
                    is_produce_item = True

        if not (card_name and is_produce_item):
            continue
        if len(cells) < 6:
            continue

        effect_text = cells[5].get_text(separator="\n").strip()
        parsed = _parse_item_effect_text(effect_text)
        if parsed:
            result[card_name] = parsed

    return result


def _parse_item_effect_text(text: str) -> dict | None:
    """アイテム効果テキストからステータス上昇効果を抽出"""
    flat = text.replace('\n', '')

    # ステータス上昇パターン
    stat_match = re.search(r'(Vo|Da|Vi)\+(\d+)', flat)
    stat_match_jp = re.search(r'(ボーカル|ダンス|ビジュアル)上昇\+(\d+)', flat)

    stat = None
    value = None

    if stat_match:
        stat_map = {"Vo": "vo", "Da": "da", "Vi": "vi"}
        stat = stat_map[stat_match.group(1)]
        value = int(stat_match.group(2))
    elif stat_match_jp:
        stat_map_jp = {"ボーカル": "vo", "ダンス": "da", "ビジュアル": "vi"}
        stat = stat_map_jp[stat_match_jp.group(1)]
        value = int(stat_match_jp.group(2))

    if stat is None or value is None:
        return None

    # トリガー判定
    trigger = None
    for keyword, trig in ITEM_TRIGGER_MAP:
        if keyword in flat:
            trigger = trig
            break
    if trigger is None:
        return None

    # condition 抽出
    condition = None
    cond_match = re.search(r'(Vo|Da|Vi)(\d+)以上', flat)
    if cond_match:
        stat_map = {"Vo": "vo", "Da": "da", "Vi": "vi"}
        condition = f"{stat_map[cond_match.group(1)]}>={cond_match.group(2)}"
    else:
        cond_match_jp = re.search(r'(ボーカル|ダンス|ビジュアル)が(\d+)\w*以上', flat)
        if cond_match_jp:
            stat_map_jp = {"ボーカル": "vo", "ダンス": "da", "ビジュアル": "vi"}
            condition = f"{stat_map_jp[cond_match_jp.group(1)]}>={cond_match_jp.group(2)}"
        else:
            cond_match_hp = re.search(r'体力(\d+)[％%]以上', flat)
            if cond_match_hp:
                condition = f"hp>={cond_match_hp.group(1)}%"

    # max_count 抽出
    max_count = None
    mc_match = re.search(r'[（(]プロデュース中(\d+)回[）)]', flat)
    if mc_match:
        max_count = int(mc_match.group(1))

    eff: dict = {
        "trigger": trigger,
        "stat": stat,
        "values": [float(value)] * 5,
        "value_type": "flat",
        "source": "item",
    }
    if max_count:
        eff["max_count"] = max_count
    if condition:
        eff["condition"] = condition

    return eff


# ======== 一覧ページ解析 ========

def parse_list_page() -> list[WikiCardEntry]:
    """一覧ページからカード情報を抽出"""
    html = fetch_page(LIST_URL)
    soup = BeautifulSoup(html, "html.parser")

    # カード一覧テーブルを探す (50行以上の最大テーブル)
    card_table = None
    for table in soup.find_all("table"):
        rows = table.find_all("tr")
        if len(rows) > 50:
            card_table = table
            break

    if not card_table:
        print("ERROR: カード一覧テーブルが見つかりません")
        return []

    # リンクのマップを作成 (カード名→URL)
    link_map: dict[str, str] = {}
    for a in soup.find_all("a", href=re.compile(r"seesaawiki\.jp/gakumasu/d/")):
        href = a.get("href", "")
        path_match = re.search(r"/d/([^#]+)", href)
        if path_match:
            try:
                name = urllib.parse.unquote(path_match.group(1), encoding="euc-jp")
                if name not in link_map:
                    link_map[name] = href
            except Exception:
                continue

    entries = []
    rows = card_table.find_all("tr")
    for row in rows[1:]:  # ヘッダー行スキップ
        cells = row.find_all(["td", "th"])
        cell_texts = [c.get_text().strip() for c in cells]
        if len(cell_texts) < 18:
            continue

        rarity = cell_texts[0]
        if rarity not in ("SSR", "SR", "R"):
            continue

        # カード名(wiki_id) を解析
        name_id = cell_texts[2]
        m = re.match(r"(.+?)\((SP_\w+_\d+)\)", name_id)
        if not m:
            continue

        entry = WikiCardEntry()
        entry.name = m.group(1).strip()
        entry.wiki_id = m.group(2).strip()
        entry.rarity = rarity
        entry.lesson_support = cell_texts[16] if len(cell_texts) > 16 else ""
        entry.date = cell_texts[17] if len(cell_texts) > 17 else ""
        entry.detail_url = link_map.get(entry.name, "")

        entries.append(entry)

    return entries


# ======== 詳細ページ解析 ========

def parse_detail_page(url: str, debug: bool = False) -> dict | None:
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
    soup = BeautifulSoup(html, "html.parser")
    result: dict = {}

    # --- メタデータ (user-area 内のテキスト) ---
    user_area = soup.find(id="user-area") or soup.find(class_="user-area")
    if user_area is None:
        # フォールバック: body全体から探す
        user_area = soup

    body_text = user_area.get_text(separator="\n")
    lines = [l.strip() for l in body_text.split("\n") if l.strip()]

    # プラン制限
    result["plan"] = ""
    for i, line in enumerate(lines):
        if "プラン制限" in line and i + 1 < len(lines):
            plan_text = lines[i + 1]
            plan_map = {"センス": "sense", "ロジック": "logic", "アノマリー": "anomaly"}
            result["plan"] = plan_map.get(plan_text, "free")
            break

    # タイプ
    result["type"] = ""
    for i, line in enumerate(lines):
        if line == "タイプ" or line == "レッスンサポート":
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
    tables = soup.find_all("table")
    result["abilities"] = []

    if debug:
        print(f"  [DEBUG] テーブル数: {len(tables)}")

    for ti, table in enumerate(tables):
        rows = table.find_all("tr")
        if debug:
            print(f"  [DEBUG] Table {ti}: rows={len(rows)}")
        if len(rows) < 4:
            if debug:
                print(f"  [DEBUG]   -> スキップ (rows < 4)")
            continue

        row0_text = rows[0].get_text(separator=" ").strip()
        row1_text = rows[1].get_text(separator=" ").strip()

        if debug:
            print(f"  [DEBUG]   Row0: {row0_text[:100]}")
            print(f"  [DEBUG]   Row1: {row1_text[:100]}")

        if "解放条件" not in row0_text:
            if debug:
                print(f"  [DEBUG]   -> スキップ (解放条件なし)")
            continue
        if "上限解放" not in row1_text:
            if debug:
                print(f"  [DEBUG]   -> スキップ (上限解放なし)")
            continue

        if debug:
            print(f"  [DEBUG]   -> ★選択 (データ行数: {len(rows) - 3})")

        for ri, row in enumerate(rows[3:]):
            cells = row.find_all(["td", "th"])
            cell_texts = [c.get_text().strip() for c in cells]
            if debug:
                print(f"  [DEBUG]   Row[{ri+3}]: cells={len(cell_texts)} | {cell_texts}")
            if len(cell_texts) < 7:
                continue
            # 末尾5列が凸別値
            uncap_values = cell_texts[-5:]
            result["abilities"].append({
                "unlock": cell_texts[0],
                "name": cell_texts[1],
                "uncap_values": uncap_values,
            })
        break

    # --- カード画像 ---
    result["image_url"] = None
    img_tag = soup.find("img", src=re.compile(
        r"https://image\d+\.seesaawiki\.jp/g/u/gakumasu/.+\.(png|jpg|jpeg)", re.IGNORECASE
    ))
    if img_tag:
        result["image_url"] = img_tag["src"]

    return result


def guess_type_from_lesson(lesson_support: str) -> str:
    """レッスンサポート文字列からカードタイプを推定"""
    clean = lesson_support.lstrip("ｳｸｽ")
    if "ボーカル" in clean:
        return "vo"
    elif "ダンス" in clean:
        return "da"
    elif "ビジュアル" in clean:
        return "vi"
    return "vo"


def guess_plan_from_lesson(lesson_support: str) -> str:
    """レッスンサポート先頭文字からプラン制限を推定"""
    if lesson_support.startswith("ｳ"):
        return "logic"
    elif lesson_support.startswith("ｸ"):
        return "sense"
    elif lesson_support.startswith("ｽ"):
        return "anomaly"
    return "free"
