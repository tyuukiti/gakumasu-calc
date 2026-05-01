"""カード構築・エフェクトマッチング"""
import re

from .constants import TRIGGER_MAP, parse_uncap_values
from .parsers import WikiCardEntry, guess_type_from_lesson, guess_plan_from_lesson


def classify_ability(name: str) -> tuple[str | None, str]:
    """
    アビリティ名からtriggerとvalue_typeを判定。
    Returns: (trigger, value_type)  trigger=None の場合はスキップ対象。
    """
    # 体力回復・体力消費はステータス上昇ではないためスキップ
    if "体力回復" in name or "体力消費" in name:
        return None, "flat"
    if re.search(r"初期.+上昇", name):
        return "equip", "flat"
    if "レッスンサポート発生率" in name:
        return None, "flat"
    if "SP" in name and "発生率" in name and "終了" not in name:
        return "equip", "sp_rate"
    if "イベント" in name and "パラメータ" in name:
        return "equip", "event_param_boost"
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

    # サポートイベント由来の固定値equip+flat (event_param: true)
    for ev in detail.get("support_events", []):
        v = float(ev["value"])
        effects.append({
            "trigger": "equip",
            "stat": ev["stat"],
            "values": [v, v, v, v, v],
            "value_type": "flat",
            "event_param": True,
        })

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


def classify_and_match(
    effects: list[dict],
    wiki_abilities: list[dict],
    default_stat: str = "vo",
    support_events: list[dict] | None = None,
) -> tuple[bool, list[str]]:
    """既存effectsの値をWikiアビリティで更新し、不足分を追加、不要分を削除する。
    support_events を渡すとサポートイベント由来 equip/flat (event_param: true) も同期する。
    """
    updated = False
    logs = []
    matched_effect_ids: set[int] = set()
    matched_wiki_indices: set[int] = set()

    # Phase 0: サポートイベント由来 equip/flat (event_param: true) の同期
    if support_events is not None:
        updated_se, logs_se = _sync_support_events(effects, support_events, matched_effect_ids)
        updated = updated or updated_se
        logs.extend(logs_se)

    # Phase 1: 既存effectsとWikiアビリティのマッチング・値更新
    for wi, wiki_abi in enumerate(wiki_abilities):
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
                if id(e) not in matched_effect_ids
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
                if id(e) in matched_effect_ids:
                    continue
                if (e.get("trigger") == target_trigger
                    and e.get("value_type") == target_vtype
                    and (stat is None or e.get("stat") == stat)):
                    matched_effect = e
                    break

        if matched_effect is not None:
            matched_effect_ids.add(id(matched_effect))
            matched_wiki_indices.add(wi)

            old_values = matched_effect.get("values", [])
            if old_values != values:
                matched_effect["values"] = values
                updated = True
                logs.append(f"    ✓ {target_trigger}/{target_vtype}: {old_values} → {values}")

    # Phase 3候補: Phase 2で追加する前に収集
    remove_candidates = [
        e for e in effects
        if id(e) not in matched_effect_ids and e.get("source") != "item"
    ]

    # Phase 2: Wikiにあるが既存effectsにない → 新規追加
    for wi, wiki_abi in enumerate(wiki_abilities):
        if wi in matched_wiki_indices:
            continue
        name = wiki_abi["name"]
        values = parse_uncap_values(wiki_abi["uncap_values"])
        if values is None:
            continue

        trigger, value_type = classify_ability(name)
        if trigger is None:
            continue

        abi_stat = detect_stat(name, default_stat)

        max_count = None
        mc_match = re.search(r"(\d+)回", name)
        if mc_match:
            max_count = int(mc_match.group(1))

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
        updated = True
        logs.append(f"    + 追加: {trigger}/{value_type} stat={abi_stat} values={values}")

    # Phase 3: Wikiアビリティに対応するtrigger+vtypeが存在しないエフェクトを削除
    # (同じtrigger+vtypeの別アビリティが存在する場合は残す)
    # event_param: true 付きはサポートイベント由来なので削除対象外
    wiki_trigger_vtypes: set[tuple[str, str]] = set()
    for wiki_abi in wiki_abilities:
        t, vt = classify_ability(wiki_abi["name"])
        if t is not None:
            wiki_trigger_vtypes.add((t, vt))

    if wiki_trigger_vtypes:
        for e in remove_candidates:
            if e.get("event_param"):
                continue
            key = (e.get("trigger"), e.get("value_type"))
            if key not in wiki_trigger_vtypes:
                effects.remove(e)
                updated = True
                logs.append(f"    - 削除: {e.get('trigger')}/{e.get('value_type')} stat={e.get('stat')}")

    return updated, logs


def _sync_support_events(
    effects: list[dict],
    support_events: list[dict],
    matched_effect_ids: set[int],
) -> tuple[bool, list[str]]:
    """サポートイベント由来 equip/flat (event_param: true) を effects に反映する。

    既存 effects のうち以下を「サポートイベント由来候補」とみなす:
      - event_param: true 付き equip/flat
      - または equip/flat で values が全要素同値 (旧データ互換)
    """
    updated = False
    logs: list[str] = []

    # 既存候補: event_param付き or 凸定数のequip/flat
    existing_se: dict[str, dict] = {}  # stat -> effect
    for e in effects:
        if e.get("trigger") != "equip" or e.get("value_type") != "flat":
            continue
        vals = e.get("values", [])
        is_const = bool(vals) and len(set(vals)) == 1
        if e.get("event_param") is True or is_const:
            existing_se[e.get("stat")] = e

    expected_stats: set[str] = set()
    for ev in support_events:
        stat = ev["stat"]
        v = float(ev["value"])
        expected_stats.add(stat)
        new_values = [v, v, v, v, v]
        existing = existing_se.get(stat)
        if existing is None:
            effects.append({
                "trigger": "equip",
                "stat": stat,
                "values": new_values,
                "value_type": "flat",
                "event_param": True,
            })
            matched_effect_ids.add(id(effects[-1]))
            updated = True
            logs.append(f"    + サポートイベント追加: equip/flat stat={stat} values={new_values} (event_param)")
        else:
            matched_effect_ids.add(id(existing))
            old_values = existing.get("values", [])
            if old_values != new_values:
                existing["values"] = new_values
                updated = True
                logs.append(f"    ✓ サポートイベント値: stat={stat} {old_values} → {new_values}")
            if not existing.get("event_param"):
                existing["event_param"] = True
                updated = True
                logs.append(f"    ✓ event_param 付与: stat={stat}")

    # サポートイベントから消えた既存候補を削除 (event_param: true 付きのみ)
    for stat, e in list(existing_se.items()):
        if stat in expected_stats:
            continue
        if e.get("event_param") is True:
            effects.remove(e)
            updated = True
            logs.append(f"    - サポートイベント削除: equip/flat stat={stat}")

    return updated, logs
