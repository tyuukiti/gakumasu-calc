"""YAML・同期記録・画像マッピングのI/O"""
import re
import yaml

from .constants import DATA_DIR, SYNCED_FILE, MAPPING_FILE


def load_yaml_cards(filepath: str) -> list[dict]:
    """YAMLファイルからカードリストを読み込む"""
    with open(filepath, "r", encoding="utf-8") as f:
        data = yaml.safe_load(f)
    return data.get("support_cards", [])


def write_yaml_with_flow_values(filepath: str, cards: list[dict]):
    """カードリストをYAMLに書き出す (valuesはフロースタイル)"""
    lines = ["support_cards:"]
    for card in cards:
        lines.append(f"- id: {card['id']}")
        lines.append(f"  name: {card['name']}")
        lines.append(f"  rarity: {card['rarity']}")
        lines.append(f"  type: {card['type']}")
        lines.append(f"  plan: {card['plan']}")
        if card.get("tag"):
            lines.append(f"  tag: {card['tag']}")
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
            if eff.get("source"):
                lines.append(f"    source: {eff['source']}")
            if eff.get("event_param") is True:
                lines.append(f"    event_param: true")
    with open(filepath, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


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
    """画像マッピングに1行追記"""
    with open(MAPPING_FILE, "a", encoding="utf-8") as f:
        f.write(f"{wiki_id}\t{card_name}\t{filename}\n")


def next_card_id(existing_cards: list[dict], rarity: str) -> str:
    """既存カードの最大IDの次の連番を返す"""
    prefix = f"SP_{rarity.upper()}_"
    max_num = 0
    for card in existing_cards:
        m = re.match(rf"SP_{rarity.upper()}_(\d+)", card["id"])
        if m:
            max_num = max(max_num, int(m.group(1)))
    return f"{prefix}{max_num + 1:04d}"
