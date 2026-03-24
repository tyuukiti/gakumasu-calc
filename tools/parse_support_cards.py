#!/usr/bin/env python3
"""Parse サポカ.txt and generate YAML files for support cards."""

import re
import os
import yaml


INPUT_FILE = os.path.join(os.path.dirname(__file__), "..", "サポカ.txt")
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "..", "Data", "SupportCards")

# Type mapping: strip first char, match remainder
TYPE_MAP = {
    "Vo": "vo",
    "Da": "da",
    "Vi": "vi",
    "As": "as",
}

# Plan mapping: strip first char, match remainder
PLAN_MAP = {
    "アノマリー": "anomaly",
    "センス": "sense",
    "ロジック": "logic",
    "フリー": "free",
}

# Trigger patterns: (regex_on_joined_text, trigger_name)
# Order matters - more specific patterns first
TRIGGER_PATTERNS = [
    # SP rate
    (r"(?:すべての)?SP率\+(\d+(?:\.\d+)?)％", "sp_rate_trigger"),
    # Para bonus
    (r"(Vo|Da|Vi)パラボ\+(\d+(?:\.\d+)?)％", "para_bonus"),
    # Initial value
    (r"(Vo|Da|Vi)初期値\+(\d+)", "initial_value"),
    # Attribute-specific SP end
    (r"(Vo|Da|Vi)SP終了時(Vo|Da|Vi)\+(\d+)", "attr_sp_end"),
    # SP end with deck condition
    (r"SP終了時デッキ(\d+)枚以上(Vo|Da|Vi)\+(\d+)", "sp_end_deck"),
    # SP end (generic)
    (r"SP終了時(Vo|Da|Vi)\+(\d+)", "sp_end"),
    # Lesson-specific end
    (r"(Vo|Da|Vi)レス終了時(Vo|Da|Vi)\+(\d+)", "lesson_end"),
    # Class/business end
    (r"授業営業終了時(Vo|Da|Vi)\+(\d+)", "class_end"),
    # Outing end
    (r"(?:お出かけ|おでかけ)終了時(Vo|Da|Vi)\+(\d+)", "outing_end"),
    # Consultation selection
    (r"相談選択時(Vo|Da|Vi)\+(\d+)", "consultation"),
    # Activity supply / gift
    (r"活動支給差し入れ選択時(Vo|Da|Vi)\+(\d+)", "activity_supply"),
    # Exam/audition end
    (r"試験・オデ終了時(Vo|Da|Vi)\+(\d+)", "exam_end"),
    # Special training start
    (r"特別指導開始時(Vo|Da|Vi)\+(\d+)", "special_training"),
    # Skill (SSR) acquire
    (r"スキル（SSR）獲得時(Vo|Da|Vi)\+(\d+)", "skill_ssr_acquire"),
    # Skill enhance
    (r"スキル強化時(Vo|Da|Vi)\+(\d+)", "skill_enhance"),
    # Active enhance
    (r"アクティブ強化時(Vo|Da|Vi)\+(\d+)", "active_enhance"),
    # Mental enhance
    (r"メンタル強化時(Vo|Da|Vi)\+(\d+)", "mental_enhance"),
    # Skill delete
    (r"スキル削除時(Vo|Da|Vi)\+(\d+)", "skill_delete"),
    # Active delete
    (r"アクティブ削除時(Vo|Da|Vi)\+(\d+)", "active_delete"),
    # Mental delete
    (r"メンタル削除時(Vo|Da|Vi)\+(\d+)", "mental_delete"),
    # Skill custom
    (r"スキルカスタム時(Vo|Da|Vi)\+(\d+)", "skill_custom"),
    # Skill change
    (r"スキルチェンジ時(Vo|Da|Vi)\+(\d+)", "skill_change"),
    # Mental acquire
    (r"メンタル獲得時(Vo|Da|Vi)\+(\d+)", "mental_acquire"),
    # Genki card acquire
    (r"元気カード獲得時(Vo|Da|Vi)\+(\d+)", "genki_acquire"),
    # Good condition card acquire
    (r"好調カード獲得時(Vo|Da|Vi)\+(\d+)", "good_condition_acquire"),
    # Good impression card acquire
    (r"好印象カード獲得時(Vo|Da|Vi)\+(\d+)", "good_impression_acquire"),
    # Conserve card acquire
    (r"温存カード獲得時(Vo|Da|Vi)\+(\d+)", "conserve_acquire"),
    # Aggressive card acquire
    (r"強気カード獲得時(Vo|Da|Vi)\+(\d+)", "aggressive_acquire"),
    # P-item acquire
    (r"Pアイテム獲得時(Vo|Da|Vi)\+(\d+)", "p_item_acquire"),
    # P-drink acquire
    (r"Pドリンク獲得時(Vo|Da|Vi)\+(\d+)", "p_drink_acquire"),
    # Consultation + drink exchange
    (r"相談Pドリンク交換後(Vo|Da|Vi)\+(\d+)", "consultation_drink"),
    # Rest selection
    (r"休む選択時(Vo|Da|Vi)\+(\d+)", "rest"),
    # Active acquire
    (r"アクティブ獲得時(Vo|Da|Vi)\+(\d+)", "active_acquire"),
    # Normal lesson end (Vo通常終了時 etc)
    (r"(Vo|Da|Vi)通常終了時(Vo|Da|Vi)\+(\d+)", "normal_lesson_end"),
    # Generic stat bonus: just Vo+N / Da+N / Vi+N (as equip bonus)
    # This is a fallback - only matches if nothing else did
]

# Max count pattern
MAX_COUNT_RE = re.compile(r"[（(](\d+)回(?:のみ)?[）)]")


def parse_type(raw):
    """Parse type field like 'ｻVi' -> 'vi'"""
    for key, val in TYPE_MAP.items():
        if key in raw:
            return val
    return None


def parse_plan(raw):
    """Parse plan field like 'ｴアノマリー' -> 'anomaly'"""
    for key, val in PLAN_MAP.items():
        if key in raw:
            return val
    return None


def strip_code_chars(text):
    """Remove leading half-width katakana code characters."""
    # Half-width katakana range: U+FF65 to U+FF9F
    i = 0
    while i < len(text) and ('\uff65' <= text[i] <= '\uff9f'):
        i += 1
    return text[i:]


def parse_ability_text(text):
    """Parse a single ability text block and return effect dict or None."""
    # Strip code chars and normalize
    cleaned = strip_code_chars(text)
    # Remove all whitespace/newlines for pattern matching
    flat = cleaned.replace('\n', '').replace('\r', '').replace(' ', '').replace('\u3000', '')

    # Skip empty or '--' abilities
    if not flat or flat == '--' or flat.startswith('--'):
        return None

    # Skip P-point related
    if 'Pポイント' in flat and not any(s in flat for s in ['Vo+', 'Da+', 'Vi+']):
        return None

    # Skip health recovery only
    if '体力回復' in flat and not any(s in flat for s in ['Vo+', 'Da+', 'Vi+']):
        return None

    # Skip 最大体力
    if '最大体力' in flat and not any(s in flat for s in ['Vo+', 'Da+', 'Vi+']):
        return None

    # Extract max_count before pattern matching
    max_count_match = MAX_COUNT_RE.search(flat)
    max_count = int(max_count_match.group(1)) if max_count_match else None

    # Try each trigger pattern
    for pattern, trigger_name in TRIGGER_PATTERNS:
        m = re.search(pattern, flat)
        if not m:
            continue

        if trigger_name == "sp_rate_trigger":
            # SP rate: すべてのSP率+N% or (stat)SP率+N%
            value = float(m.group(1))
            # Check if there's a stat prefix
            stat_match = re.search(r"(Vo|Da|Vi)SP率", flat)
            if stat_match:
                stat = stat_match.group(1).lower()
            elif "すべての" in flat:
                stat = "all"
            else:
                stat = "all"
            effect = {
                "trigger": "equip",
                "stat": stat,
                "value": value if value != int(value) else int(value),
                "value_type": "sp_rate",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "para_bonus":
            stat = m.group(1).lower()
            value = float(m.group(2))
            effect = {
                "trigger": "equip",
                "stat": stat,
                "value": value if value != int(value) else int(value),
                "value_type": "para_bonus",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "initial_value":
            stat = m.group(1).lower()
            value = int(m.group(2))
            effect = {
                "trigger": "equip",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "attr_sp_end":
            attr = m.group(1).lower()
            stat = m.group(2).lower()
            value = int(m.group(3))
            effect = {
                "trigger": f"{attr}_sp_end",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "sp_end_deck":
            deck_count = int(m.group(1))
            stat = m.group(2).lower()
            value = int(m.group(3))
            effect = {
                "trigger": "sp_end",
                "stat": stat,
                "value": value,
                "value_type": "flat",
                "condition": f"deck>={deck_count}",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "lesson_end":
            lesson_attr = m.group(1).lower()
            stat = m.group(2).lower()
            value = int(m.group(3))
            effect = {
                "trigger": f"{lesson_attr}_lesson_end",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "normal_lesson_end":
            lesson_attr = m.group(1).lower()
            stat = m.group(2).lower()
            value = int(m.group(3))
            effect = {
                "trigger": f"{lesson_attr}_normal_end",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "mental_enhance":
            stat = m.group(1).lower()
            value = int(m.group(2))
            effect = {
                "trigger": "mental_enhance",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        elif trigger_name == "mental_delete":
            stat = m.group(1).lower()
            value = int(m.group(2))
            effect = {
                "trigger": "active_delete",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

        else:
            # Generic 2-group pattern: (stat, value)
            groups = m.groups()
            if len(groups) == 2:
                stat = groups[0].lower()
                value_str = groups[1]
                try:
                    value = float(value_str)
                    value = value if value != int(value) else int(value)
                except ValueError:
                    continue
                effect = {
                    "trigger": trigger_name,
                    "stat": stat,
                    "value": value,
                    "value_type": "flat",
                }
                if max_count:
                    effect["max_count"] = max_count
                return effect

    # Fallback: simple stat bonus like Vo+20, Da+15 etc (equip bonus)
    stat_match = re.search(r"(Vo|Da|Vi)\+(\d+)", flat)
    if stat_match:
        # Make sure this isn't part of a complex trigger we already handle
        # Check it's a simple equip bonus (no trigger keyword)
        trigger_keywords = [
            "終了時", "選択時", "獲得時", "開始時", "強化時", "削除時",
            "カスタム時", "チェンジ時", "交換後", "SP率", "パラボ", "初期値",
        ]
        has_trigger = any(kw in flat for kw in trigger_keywords)
        if not has_trigger:
            stat = stat_match.group(1).lower()
            value = int(stat_match.group(2))
            effect = {
                "trigger": "equip",
                "stat": stat,
                "value": value,
                "value_type": "flat",
            }
            if max_count:
                effect["max_count"] = max_count
            return effect

    return None


def split_into_cards(lines):
    """Split file lines into card groups. Each group starts with SSR/SR/R line."""
    cards_raw = []
    current = None
    rarity_re = re.compile(r"^(SSR|SR|R)\t")
    rarity_only_re = re.compile(r"^(SSR|SR|R)$")
    prefix_re = re.compile(r"^（(?:イベ|コイン|交換)）\t")

    i = 0
    while i < len(lines):
        line = lines[i]

        # Check for rarity-only line (SSR\n（イベ）\t...)
        if rarity_only_re.match(line):
            if current is not None:
                cards_raw.append(current)
            # Next line should have the prefix + card data
            if i + 1 < len(lines) and prefix_re.match(lines[i + 1]):
                next_line = lines[i + 1]
                prefix_match = re.match(r"^（(イベ|コイン|交換)）\t(.*)$", next_line)
                if prefix_match:
                    prefix_label = prefix_match.group(1)
                    rest = prefix_match.group(2)
                    # Reconstruct as: SSR\tname\ttype\tplan\t...
                    # rest = name\ttype\tplan\t...
                    parts = rest.split('\t')
                    name = f"（{prefix_label}）{parts[0]}" if parts else ""
                    reconstructed = f"{line}\t{name}"
                    if len(parts) > 1:
                        reconstructed += "\t" + "\t".join(parts[1:])
                    current = {"rarity": line.strip(), "header": reconstructed, "lines": []}
                    i += 2
                    continue
            # Fallback
            current = {"rarity": line.strip(), "header": line, "lines": []}
            i += 1
            continue

        if rarity_re.match(line):
            if current is not None:
                cards_raw.append(current)
            rarity = line.split('\t')[0]
            current = {"rarity": rarity, "header": line, "lines": []}
            i += 1
            continue

        if current is not None:
            current["lines"].append(line)

        i += 1

    if current is not None:
        cards_raw.append(current)

    return cards_raw


def parse_card(card_raw, card_id):
    """Parse a single card's raw data into a card dict."""
    header = card_raw["header"]
    rarity = card_raw["rarity"]
    extra_lines = card_raw["lines"]

    # Parse header: rarity\tname\ttype\tplan\t[abilities...]
    header_parts = header.split('\t')

    if len(header_parts) < 4:
        return None

    name = header_parts[1]
    type_raw = header_parts[2]
    plan_raw = header_parts[3]

    card_type = parse_type(type_raw)
    card_plan = parse_plan(plan_raw)

    if not card_type or not card_plan:
        return None

    # Build tab-separated columns from header (cols 4+) and continuation lines
    # The full content is: header line + extra lines joined by \n
    # Then split by \t to get columns
    full_text = header
    if extra_lines:
        full_text += '\n' + '\n'.join(extra_lines)

    # Split the full text by tabs to get all columns
    all_columns = full_text.split('\t')

    # Abilities start from column index 4 (0-based)
    ability_columns = all_columns[4:] if len(all_columns) > 4 else []

    # Parse each ability column
    effects = []
    for ability_text in ability_columns:
        # Clean up the text (may have newlines within)
        ability_text = ability_text.strip()
        if not ability_text or ability_text == '--':
            continue

        effect = parse_ability_text(ability_text)
        if effect is not None:
            effects.append(effect)

    card = {
        "id": card_id,
        "name": name,
        "rarity": rarity,
        "type": card_type,
        "plan": card_plan,
    }
    if effects:
        card["effects"] = effects

    return card


def format_yaml_value(value):
    """Format a value for YAML output."""
    if isinstance(value, float):
        return str(value)
    return value


def write_yaml(cards, filepath):
    """Write cards to YAML file with clean formatting."""

    class CustomDumper(yaml.SafeDumper):
        pass

    def str_representer(dumper, data):
        if '\n' in data:
            return dumper.represent_scalar('tag:yaml.org,2002:str', data, style='|')
        return dumper.represent_scalar('tag:yaml.org,2002:str', data)

    CustomDumper.add_representer(str, str_representer)

    output = {"support_cards": cards}

    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with open(filepath, 'w', encoding='utf-8') as f:
        yaml.dump(output, f, Dumper=CustomDumper, allow_unicode=True,
                  default_flow_style=False, sort_keys=False)

    print(f"  Wrote {len(cards)} cards to {filepath}")


def main():
    # Read input file
    input_path = os.path.abspath(INPUT_FILE)
    print(f"Reading: {input_path}")

    with open(input_path, 'r', encoding='utf-8') as f:
        content = f.read()

    lines = content.split('\n')

    # Skip header lines (lines before first card)
    # Find first card line
    start_idx = 0
    for i, line in enumerate(lines):
        if re.match(r'^(SSR|SR|R)\t', line) or re.match(r'^(SSR|SR|R)$', line):
            start_idx = i
            break

    lines = lines[start_idx:]

    # Split into card groups
    cards_raw = split_into_cards(lines)
    print(f"Found {len(cards_raw)} card groups")

    # Parse cards and separate by rarity
    ssr_cards = []
    sr_cards = []
    r_cards = []

    counters = {"SSR": 0, "SR": 0, "R": 0}

    for card_raw in cards_raw:
        rarity = card_raw["rarity"]
        counters[rarity] = counters.get(rarity, 0) + 1
        card_id = f"{rarity.lower()}_{counters[rarity]:03d}"

        card = parse_card(card_raw, card_id)
        if card is None:
            print(f"  WARNING: Could not parse card #{counters[rarity]} (rarity={rarity})")
            continue

        if rarity == "SSR":
            ssr_cards.append(card)
        elif rarity == "SR":
            sr_cards.append(card)
        elif rarity == "R":
            r_cards.append(card)

    # Write output files
    output_dir = os.path.abspath(OUTPUT_DIR)
    print(f"\nOutput directory: {output_dir}")

    if ssr_cards:
        write_yaml(ssr_cards, os.path.join(output_dir, "ssr_cards.yaml"))
    if sr_cards:
        write_yaml(sr_cards, os.path.join(output_dir, "sr_cards.yaml"))
    if r_cards:
        write_yaml(r_cards, os.path.join(output_dir, "r_cards.yaml"))

    print(f"\nTotal: {len(ssr_cards)} SSR, {len(sr_cards)} SR, {len(r_cards)} R cards")


if __name__ == "__main__":
    main()
