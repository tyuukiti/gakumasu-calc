"""
Seesaa Wiki からサポートカードのアビリティ凸別データをスクレイピングし、
YAMLデータの values を更新するスクリプト。

使い方:
  python scripts/scrape_wiki.py            # 全カード (30秒間隔)
  python scripts/scrape_wiki.py --test 3   # 最初の3枚だけテスト
  python scripts/scrape_wiki.py --dry-run  # ファイル書き込みしない
"""
import urllib.request
import urllib.parse
import re
import time
import yaml
import sys
import argparse
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

WIKI_BASE = "https://seesaawiki.jp/gakumasu"
LIST_URL = f"{WIKI_BASE}/d/%A5%B5%A5%DD%A1%BC%A5%C8%A5%AB%A1%BC%A5%C9%B0%EC%CD%F7"
DATA_DIR = Path(__file__).parent.parent / "Data" / "SupportCards"
DELAY_SECONDS = 30


def fetch_page(url: str) -> str:
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    resp = urllib.request.urlopen(req)
    raw = resp.read()
    return raw.decode("euc-jp", errors="replace")


def get_card_links() -> dict[str, str]:
    """一覧ページから {カード名: URL} を取得"""
    text = fetch_page(LIST_URL)
    raw_links = re.findall(
        r'href="(https://seesaawiki\.jp/gakumasu/d/([^"]+))"', text
    )
    result = {}
    for full_url, path in raw_links:
        try:
            name = urllib.parse.unquote(path, encoding="euc-jp")
        except Exception:
            continue
        if name not in result:
            result[name] = full_url
    return result


def extract_cells(row_html: str) -> list[str]:
    cells = re.findall(r"<t[dh][^>]*>(.*?)</t[dh]>", row_html, re.DOTALL)
    return [re.sub(r"<[^>]+>", "", c).strip() for c in cells]


def parse_uncap_table(html: str) -> list[dict] | None:
    """
    凸別サマリーテーブルを解析。

    Returns: [{unlock, name, uncap_values: [str x5]}] or None
    """
    tables = re.findall(r"<table[^>]*>(.*?)</table>", html, re.DOTALL)

    for table in tables:
        rows = re.findall(r"<tr[^>]*>(.*?)</tr>", table, re.DOTALL)
        if len(rows) < 4:
            continue

        row0_text = " ".join(extract_cells(rows[0]))
        row1_text = " ".join(extract_cells(rows[1]))

        # サマリーテーブル: Row 0 に「解放条件」、Row 1 に「上限解放」がある
        if "解放条件" not in row0_text:
            continue
        if "上限解放" not in row1_text:
            continue

        abilities = []
        for row in rows[3:]:
            cells = extract_cells(row)
            if len(cells) < 7:
                continue

            # cells: [解放条件, アビリティ名, 初期値, LvMax値, 0凸, 1凸, 2凸, 3凸, 4凸]
            uncap_values = []
            for i in range(4, 9):
                uncap_values.append(cells[i].strip() if i < len(cells) else "")

            abilities.append({
                "unlock": cells[0].strip(),
                "name": cells[1].strip(),
                "uncap_values": uncap_values,
            })

        return abilities

    return None


def parse_value(s: str) -> float | None:
    s = s.strip().replace("%", "").replace(",", "")
    if not s:
        return None
    try:
        return float(s)
    except ValueError:
        return None


def match_and_update(card: dict, wiki_abilities: list[dict]) -> tuple[bool, list[str]]:
    """
    カードのeffectsをWikiデータで更新する。

    マッチング戦略:
    - Wiki「初期XX上昇」→ 既存 equip/flat の中で values[4] が最大のもの
    - Wiki「XXSP率」→ 既存 equip/sp_rate
    - Wiki「パラメータ上昇を...増加」→ 既存 equip/para_bonus
    - Wiki トリガー系 → 既存の同トリガー/同value_typeのeffect

    Returns: (updated, log_messages)
    """
    effects = card.get("effects", [])
    updated = False
    logs = []

    for wiki_abi in wiki_abilities:
        name = wiki_abi["name"]
        raw_values = wiki_abi["uncap_values"]

        # 数値をパース (空セルは直前の有効値で補完)
        values = [parse_value(v) for v in raw_values]
        for idx in range(len(values)):
            if values[idx] is None:
                values[idx] = values[idx - 1] if idx > 0 and values[idx - 1] is not None else 0.0
        if all(v == 0.0 for v in values):
            continue
        values = [float(v) for v in values]

        # ステータス判定
        stat = None
        for s, kw in [("vo", "ボーカル"), ("da", "ダンス"), ("vi", "ビジュアル")]:
            if kw in name:
                stat = s
                break

        # --- トリガー + value_type 判定 ---
        target_trigger = None
        target_vtype = "flat"
        match_strategy = "default"

        # 初期ステ上昇アビリティ
        if re.search(r"初期.+上昇", name):
            target_trigger = "equip"
            target_vtype = "flat"
            match_strategy = "equip_flat_ability"

        # レッスンサポート発生率 (ステ計算には不要、スキップ)
        elif "レッスンサポート発生率" in name:
            continue

        # SP発生率 (「SP」+「発生率」だが「終了時」を含まない)
        elif "SP" in name and "発生率" in name and "終了" not in name:
            target_trigger = "equip"
            target_vtype = "sp_rate"

        # パラメータ上昇増加
        elif "イベント" in name and "パラメータ" in name:
            target_trigger = "equip"
            target_vtype = "para_bonus"

        # トリガー系
        else:
            trigger_map = [
                ("活動支給", "activity_supply"),
                ("差し入れ", "activity_supply"),
                ("スキルカード（SSR）獲得", "skill_ssr_acquire"),
                ("スキルカード(SSR)獲得", "skill_ssr_acquire"),
                ("スキル強化", "skill_enhance"),
                ("スキル削除", "skill_delete"),
                ("スキルカスタム", "skill_custom"),
                ("スキルチェンジ", "skill_change"),
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
                ("授業終了", "class_end"),
                ("お出かけ終了", "outing_end"),
                ("相談Pドリンク", "consultation_drink"),
                ("相談選択", "consultation"),
                ("試験終了", "exam_end"),
                ("特別指導", "special_training"),
                ("Pアイテム獲得", "p_item_acquire"),
                ("Pドリンク獲得", "p_drink_acquire"),
                ("休憩選択", "rest"),
                ("休憩", "rest"),
                ("元気効果", "genki_acquire"),
                ("元気カード獲得", "genki_acquire"),
                ("元気獲得", "genki_acquire"),
                ("好調効果", "good_condition_acquire"),
                ("好調カード獲得", "good_condition_acquire"),
                ("好印象効果", "good_impression_acquire"),
                ("好印象カード獲得", "good_impression_acquire"),
                ("温存効果", "conserve_acquire"),
                ("温存カード獲得", "conserve_acquire"),
                ("メンタルスキルカード獲得", "mental_acquire"),
                ("メンタル獲得", "mental_acquire"),
                ("メンタル強化", "mental_enhance"),
                ("集中効果", "good_condition_acquire"),
                ("やる気効果", "good_impression_acquire"),
                ("根気効果", "conserve_acquire"),
            ]
            for keyword, trig in trigger_map:
                if keyword in name:
                    target_trigger = trig
                    break

        if target_trigger is None:
            logs.append(f"    ? 分類不能: {name[:50]}")
            continue

        # --- 既存effectとのマッチング ---
        matched_effect = None

        if match_strategy == "equip_flat_ability":
            # equip/flat が複数ある場合、イベントステータス(通常20)ではなく
            # アビリティ値(大きい方)にマッチ
            candidates = [
                e for e in effects
                if e.get("trigger") == "equip"
                and e.get("value_type") == "flat"
                and (stat is None or e.get("stat") == stat)
            ]
            if len(candidates) >= 2:
                # values[4] が最大のもの = アビリティ
                candidates.sort(key=lambda e: e.get("values", [0])[min(4, len(e.get("values", [0]))-1)], reverse=True)
                matched_effect = candidates[0]
            elif len(candidates) == 1:
                matched_effect = candidates[0]
        else:
            for e in effects:
                if (e.get("trigger") == target_trigger
                    and e.get("value_type") == target_vtype
                    and (stat is None or e.get("stat") == stat)):
                    matched_effect = e
                    break

        if matched_effect is None:
            logs.append(f"    ? マッチなし: {name[:40]} → {target_trigger}/{target_vtype}")
            continue

        # 値の更新
        old_values = matched_effect.get("values", [])
        if old_values != values:
            matched_effect["values"] = values
            updated = True
            logs.append(f"    ✓ {target_trigger}/{target_vtype}: {old_values} → {values}")
        else:
            logs.append(f"    - {target_trigger}/{target_vtype}: 変更なし")

    return updated, logs


def load_all_cards() -> dict[str, dict]:
    """全YAMLファイルのカードを {name: {card, file}} で返す"""
    all_cards = {}
    for yf in DATA_DIR.glob("*_cards.yaml"):
        with open(yf, "r", encoding="utf-8") as f:
            data = yaml.safe_load(f)
        for card in data.get("support_cards", []):
            all_cards[card["name"]] = {"card": card, "file": str(yf)}
    return all_cards


def save_cards_by_file(all_cards: dict[str, dict]):
    """ファイルごとにカードを保存"""
    by_file: dict[str, list] = {}
    for info in all_cards.values():
        filepath = info["file"]
        if filepath not in by_file:
            by_file[filepath] = []
        by_file[filepath].append(info["card"])

    for filepath, cards in by_file.items():
        # 元のカード順を維持するため、既存ファイルのID順で並べ直す
        with open(filepath, "r", encoding="utf-8") as f:
            original = yaml.safe_load(f)
        original_ids = [c["id"] for c in original.get("support_cards", [])]
        id_order = {id_: i for i, id_ in enumerate(original_ids)}
        cards.sort(key=lambda c: id_order.get(c["id"], 999))

        # フロースタイルの values 配列を使ってYAMLを書き出す
        write_yaml_with_flow_values(filepath, cards)
        print(f"  保存: {filepath}")


def write_yaml_with_flow_values(filepath: str, cards: list[dict]):
    """values 配列をフロースタイル [a, b, c, d, e] で書き出す"""
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

            # values をフロースタイルで
            vals = eff.get("values", [])
            val_strs = []
            for v in vals:
                if v == int(v):
                    val_strs.append(str(int(v)))
                else:
                    val_strs.append(str(v))
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


def main():
    parser = argparse.ArgumentParser(description="Wiki凸別データスクレイピング")
    parser.add_argument("--test", type=int, default=0, help="テスト枚数 (0=全件)")
    parser.add_argument("--dry-run", action="store_true", help="ファイル書き込みしない")
    parser.add_argument("--delay", type=int, default=DELAY_SECONDS, help="リクエスト間隔(秒)")
    args = parser.parse_args()

    print("=== サポートカード凸別データ スクレイピング ===")
    print(f"  データ: {DATA_DIR}")
    print(f"  間隔: {args.delay}秒")
    if args.dry_run:
        print("  [DRY-RUN モード]")
    print()

    # 1. カードリンク取得
    print("一覧ページを取得中...")
    wiki_links = get_card_links()
    print(f"  Wikiリンク: {len(wiki_links)} 件")

    # 2. 既存YAML読み込み
    all_cards = load_all_cards()
    print(f"  既存カード: {len(all_cards)} 枚")

    # 3. マッチング
    matched = []
    for name, info in all_cards.items():
        if name in wiki_links:
            matched.append((name, wiki_links[name], info))

    print(f"  マッチ: {len(matched)} 枚")
    unmatched = [n for n in all_cards if n not in wiki_links]
    if unmatched:
        print(f"  未マッチ: {len(unmatched)} 枚")
        for n in unmatched:
            print(f"    - {n}")
    print()

    # テスト制限
    if args.test > 0:
        matched = matched[: args.test]
        print(f"  テストモード: {len(matched)} 枚のみ処理")
        print()

    # 4. 各カード詳細ページをスクレイピング
    update_count = 0
    error_count = 0

    for i, (name, url, info) in enumerate(matched):
        print(f"[{i + 1}/{len(matched)}] {name}")

        try:
            html = fetch_page(url)
            abilities = parse_uncap_table(html)

            if not abilities:
                print("  ⚠ テーブルが見つかりません")
                error_count += 1
                continue

            was_updated, logs = match_and_update(info["card"], abilities)
            for log in logs:
                print(log)

            if was_updated:
                update_count += 1
                print("  → 更新あり")
            else:
                print("  → 変更なし")

        except Exception as e:
            print(f"  ✗ エラー: {e}")
            error_count += 1

        if i < len(matched) - 1:
            print(f"  ({args.delay}秒待機...)")
            time.sleep(args.delay)

    # 5. 保存
    print()
    if update_count > 0 and not args.dry_run:
        print(f"ファイル保存中...")
        save_cards_by_file(all_cards)
    elif args.dry_run:
        print(f"[DRY-RUN] 書き込みスキップ")

    print(f"\n=== 完了 ===")
    print(f"  更新: {update_count} 枚")
    print(f"  エラー: {error_count} 枚")


if __name__ == "__main__":
    main()
