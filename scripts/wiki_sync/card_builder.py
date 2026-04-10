"""カード構築・エフェクトマッチング"""
import re

from .constants import TRIGGER_MAP, parse_uncap_values
from .parsers import WikiCardEntry, guess_type_from_lesson, guess_plan_from_lesson


def classify_ability(name: str) -> tuple[str | None, str]:
    """
    アビリティ名からtriggerとvalue_typeを判定。
    Returns: (trigger, value_type)  trigger=None の場合はスキップ対象。
    """
    if re.search(r"初期.+上昇", name):
        return "equip", "flat"
    if "レッスンサポート発生率" in name:
        return None, "flat"
    if "SP" in name and "発生率" in name and "終了" not in name:
        return "equip", "sp_rate"
    if "イベント" in name and "パラメータ" in name:
        return None, "flat"
    if "Pポイント" in name:
        return None, "flat"
    if "パラメータボーナス" in name or "レッスンボーナス" in name:
        return "equip", "para_bonus"

    for keyword, trig in TRIGGER_MAP:
        if keyword in name:
            return trig, "flat"

    return None, "flat"


def detect_stat(name: str, default: str) -> str:
    """アビリティ名からステータス種別を判定"""
    for s, kw in [("vo", "ボーカル"), ("da", "ダンス"), ("vi", "ビジュアル")]:
        if kw in name:
            return s
    return default


def build_new_card(
    card_id: str,
    entry: WikiCardEntry,
    detail: dict,
    abilities: list[dict],
    tag: str | None = None,
    debug: bool = False,
) -> dict:
    """Wikiデータから新規カードYAMLエントリを構築"""
    card_type = detail.get("type") or guess_type_from_lesson(entry.lesson_support)
    plan = detail.get("plan") or guess_plan_from_lesson(entry.lesson_support)
    default_stat = "all" if card_type == "as" else card_type

    effects = []
    for abi in abilities:
        name = abi["name"]
        values = parse_uncap_values(abi["uncap_values"])
        if values is None:
            continue

        abi_stat = detect_stat(name, default_stat)
        trigger, value_type = classify_ability(name)

        if trigger is None:
            if debug:
                print(f"  [DEBUG] ⚠ 未分類スキップ: '{name}' values={values}")
            continue

        if debug:
            print(f"  [DEBUG] 分類: '{name}' -> trigger={trigger}, vtype={value_type}, stat={abi_stat}, values={values}")

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

    card: dict = {
        "id": card_id,
        "name": entry.name,
        "rarity": entry.rarity,
        "type": card_type,
        "plan": plan,
        "effects": effects,
    }
    if tag:
        card["tag"] = tag
    return card


def classify_and_match(effects: list[dict], wiki_abilities: list[dict]) -> tuple[bool, list[str]]:
    """既存effectsの値をWikiアビリティで更新する"""
    updated = False
    logs = []
    matched_ids: set[int] = set()

    for wiki_abi in wiki_abilities:
        name = wiki_abi["name"]
        values = parse_uncap_values(wiki_abi["uncap_values"])
        if values is None:
            continue

        stat = None
        for s, kw in [("vo", "ボーカル"), ("da", "ダンス"), ("vi", "ビジュアル")]:
            if kw in name:
                stat = s
                break

        target_trigger, target_vtype = classify_ability(name)
        if target_trigger is None:
            continue

        # equip/flat は複数候補がある場合、最大値の高い方を優先マッチ
        matched_effect = None
        if target_trigger == "equip" and target_vtype == "flat":
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
