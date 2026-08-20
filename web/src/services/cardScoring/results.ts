import type { TrainingPlan, StatusValues, TurnChoice, Character } from '../../types/models';
import type { ActionType } from '../../types/enums';
import type { CardScore, AbilitySummaryEntry } from '../../types/results';
import { getUncapLevel, getEffectValue } from '../statusCalculation';
import { DEFAULT_STAT_CAP } from '../../utils/constants';
import type { OverflowPenaltyConfig } from './types';
import { calculateCardContribution, computeTriggerBonusInfo } from './contribution';
import { isFixedEvent, triggerDisplayName } from './helpers';

export function buildTurnChoices(
  plan: TrainingPlan,
  mainStats: string[],
): TurnChoice[] {
  const choices: TurnChoice[] = [];
  const subStat = ['vo', 'da', 'vi'].find(
    (s) => !mainStats.includes(s),
  ) as string;

  const lessonAction = (stat: string): ActionType =>
    `${stat}_lesson` as ActionType;
  const classAction = (stat: string): ActionType =>
    `${stat}_class` as ActionType;

  const main1Action = lessonAction(mainStats[0]);
  const main2Action =
    mainStats.length > 1 ? lessonAction(mainStats[1]) : main1Action;
  const subClassAction = classAction(subStat);

  let midExamWeek =
    plan.schedule.find(
      (w) => isFixedEvent(w) && w.event_name === '中間試験',
    )?.week ?? 10;
  if (midExamWeek === 0) midExamWeek = 10;

  const lessonWeeks = plan.schedule
    .filter((w) => !isFixedEvent(w) && w.lessons.length > 0)
    .sort((a, b) => a.week - b.week);

  // Before mid: alternate
  let toggle = false;
  for (const w of lessonWeeks.filter((w) => w.week < midExamWeek)) {
    choices.push({
      week: w.week,
      chosen_action: toggle ? main2Action : main1Action,
    });
    toggle = !toggle;
  }

  // After mid: main1:main2 = 2:1
  let afterCount = 0;
  for (const w of lessonWeeks.filter((w) => w.week > midExamWeek)) {
    choices.push({
      week: w.week,
      chosen_action: afterCount % 3 === 1 ? main2Action : main1Action,
    });
    afterCount++;
  }

  // Non-lesson weeks
  for (const w of plan.schedule) {
    if (isFixedEvent(w) || w.lessons.length > 0) continue;
    const actions = w.available_actions ?? [];

    const hasClass = actions.some((a) => a.includes('class'));
    if (hasClass) {
      const subClassStr = `${subStat}_class`;
      if (actions.includes(subClassStr)) {
        choices.push({ week: w.week, chosen_action: subClassAction });
      } else {
        const mainClassStr = `${mainStats[0]}_class`;
        if (actions.includes(mainClassStr)) {
          choices.push({
            week: w.week,
            chosen_action: classAction(mainStats[0]),
          });
        }
      }
    } else if (actions.includes('activity_supply')) {
      choices.push({ week: w.week, chosen_action: 'activity_supply' });
    } else if (actions.includes('outing')) {
      choices.push({ week: w.week, chosen_action: 'outing' });
    } else if (actions.includes('consultation')) {
      choices.push({ week: w.week, chosen_action: 'consultation' });
    } else if (actions.includes('special_training')) {
      choices.push({ week: w.week, chosen_action: 'special_training' });
    }
  }

  return choices;
}

// --- Select best card ---

export function selectBestCard(
  candidates: CardScore[],
  usedIds: Set<string>,
  currentVo: number,
  currentDa: number,
  currentVi: number,
  statCap: number = DEFAULT_STAT_CAP,
  character?: Character | null,
  overflowPenalty?: OverflowPenaltyConfig,
): CardScore | undefined {
  let best: CardScore | undefined = undefined;
  let bestGain = -Infinity;

  // キャラの para_bonus はカード貢献にも乗る (calculate 時)。greedy 予測でも同じ倍率を適用する。
  const voMul = 1 + (character?.para_bonus.vo ?? 0) / 100;
  const daMul = 1 + (character?.para_bonus.da ?? 0) / 100;
  const viMul = 1 + (character?.para_bonus.vi ?? 0) / 100;

  // overflow罰則を適用するなら現在の overflow を計算
  const overflowCurrent = overflowPenalty
    ? Math.max(0, currentVo - statCap) + Math.max(0, currentDa - statCap) + Math.max(0, currentVi - statCap)
    : 0;

  for (const cs of candidates) {
    if (usedIds.has(cs.card.id)) continue;

    const rawNewVo = currentVo + cs.raw_vo * voMul;
    const rawNewDa = currentDa + cs.raw_da * daMul;
    const rawNewVi = currentVi + cs.raw_vi * viMul;

    // キャップ適用後の実効増分 (合計stat)
    const cappedNewSum =
      Math.min(rawNewVo, statCap) + Math.min(rawNewDa, statCap) + Math.min(rawNewVi, statCap);
    const cappedCurrentSum =
      Math.min(currentVo, statCap) + Math.min(currentDa, statCap) + Math.min(currentVi, statCap);
    let gain = cappedNewSum - cappedCurrentSum;

    // overflow罰則: ピック後の合計overflowが閾値を超える場合のみ、追加overflow分を× 2 罰則
    if (overflowPenalty) {
      const overflowNew =
        Math.max(0, rawNewVo - statCap) + Math.max(0, rawNewDa - statCap) + Math.max(0, rawNewVi - statCap);
      if (overflowNew > overflowPenalty.threshold) {
        const newOverflow = Math.max(0, overflowNew - overflowCurrent);
        gain -= newOverflow * 2;
      }
    }

    if (gain > bestGain) {
      bestGain = gain;
      best = cs;
    }
  }

  return best;
}

// --- Calculate capped total ---

export function calculateCappedTotal(
  baseStats: StatusValues,
  owned: CardScore[],
  rental: CardScore | undefined,
  statCap: number,
): number {
  let vo = baseStats.vo,
    da = baseStats.da,
    vi = baseStats.vi;
  for (const cs of owned) {
    vo += cs.raw_vo;
    da += cs.raw_da;
    vi += cs.raw_vi;
  }
  if (rental != null) {
    vo += rental.raw_vo;
    da += rental.raw_da;
    vi += rental.raw_vi;
  }
  return Math.min(vo, statCap) + Math.min(da, statCap) + Math.min(vi, statCap);
}

// --- Recalculate with cap ---

/**
 * デッキ確定後の deck-aware 再計算。
 * - producer の trigger_count_bonus 効果による消費側カードへのバフ分を triggerCounts に加算
 *   (consumer の flat 効果が deck で実際に発火される回数を反映)
 * - producer 側では trigger_count_bonus を raw_* に加算しない (二重カウント回避)
 * - team_bonus_total はデッキ内 consumer のみを対象に計算される
 */
export function recomputeBreakdownsDeckAware(
  selected: CardScore[],
  baseTriggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
): Record<string, number> {
  // レンタル枠は所持凸数に依らず常に4凸として評価する
  const effectiveUncapLevels: Record<string, number> = { ...(uncapLevels ?? {}) };
  for (const cs of selected) {
    if (cs.is_rental) effectiveUncapLevels[cs.card.id] = 4;
  }

  // 1. デッキ内 producer の trigger_count_bonus 集計
  const deckBonuses: Record<string, number> = {};
  for (const cs of selected) {
    const uncap = getUncapLevel(cs.card, effectiveUncapLevels);
    for (const effect of cs.card.effects) {
      if (effect.value_type !== 'trigger_count_bonus') continue;
      const target = effect.trigger_target;
      if (!target) continue;
      const perScale = getEffectValue(effect, uncap);
      const scaleCount = effect.scales_with
        ? (baseTriggerCounts[effect.scales_with] ?? 0)
        : 1;
      let bonus = perScale * scaleCount;
      if (effect.max_count != null) bonus = Math.min(bonus, effect.max_count);
      bonus = Math.floor(bonus);
      if (bonus > 0) deckBonuses[target] = (deckBonuses[target] ?? 0) + bonus;
    }
  }
  if (Object.keys(deckBonuses).length === 0) return baseTriggerCounts;

  // 2. adjustedCounts = base + producer-derived bonus
  const adjustedCounts: Record<string, number> = { ...baseTriggerCounts };
  for (const [k, v] of Object.entries(deckBonuses)) {
    adjustedCounts[k] = (adjustedCounts[k] ?? 0) + v;
  }

  // 3. デッキ内カードのみで triggerBonusInfo を計算 (parens 表示が実デッキ反映に)
  const deckCards = selected.map((cs) => cs.card);
  const deckTriggerBonusInfo = computeTriggerBonusInfo(deckCards, effectiveUncapLevels);

  // 4. 各 selected card を再計算 (skipTriggerBonusSelfContribution=true)
  for (let i = 0; i < selected.length; i++) {
    const cs = selected[i];
    const recomputed = calculateCardContribution(
      cs.card,
      adjustedCounts,
      lessonAllocation,
      lessonStatTotals,
      effectiveUncapLevels,
      deckTriggerBonusInfo,
      true,
    );
    selected[i] = {
      ...recomputed,
      is_rental: cs.is_rental,
      is_required: cs.is_required,
    };
  }

  return adjustedCounts;
}

/**
 * アビリティまとめ (行動別) を構築する。
 *
 * 選択カードの flat 効果 (trigger !== 'equip') を (行動トリガー × 属性) でグループ化し、
 * 発動回数を掛けて合算する。「どの行動を取るとパラメが伸びるか」の比較用。
 * - 値は calculateCardContribution / 各カード内訳と同じ生寄与 (cap前・キャラパラボ前)
 * - 装備 (初期値/SP率)・パラボ・trigger_count_bonus は行動選択で変動しないため除外
 * - レンタル枠は4凸として評価 (内訳パネルと同じ)
 * - triggerCounts は recomputeBreakdownsDeckAware が返す adjustedCounts (trigger_count_bonus 反映済み)
 *
 * max_count でカードごとに実効発動回数が異なる稀ケースでは、total は各カードの
 * 実効回数で正確に合算し、表示の発動回数 N は当該トリガーの発動回数を用いる
 * (per_fire × N が total と一致しない場合があるが total が権威値)。
 */
export function buildAbilitySummary(
  selected: CardScore[],
  triggerCounts: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): AbilitySummaryEntry[] {
  // レンタル枠は内訳パネルと同様に常に4凸として評価する
  const effectiveUncap: Record<string, number> = { ...(uncapLevels ?? {}) };
  for (const cs of selected) {
    if (cs.is_rental) effectiveUncap[cs.card.id] = 4;
  }

  interface Acc {
    trigger: string;
    stat: string;
    fires: number;
    perFireTotal: number;
    parts: number[];
    total: number;
    capValues: Set<number>; // 行動回数を下回って実際に効いている max_count の集合 (上限表記用)
  }
  const groups = new Map<string, Acc>();

  for (const cs of selected) {
    const uncap = getUncapLevel(cs.card, effectiveUncap);
    for (const effect of cs.card.effects) {
      if (effect.value_type !== 'flat') continue;
      if (effect.trigger === 'equip') continue;

      const perFire = getEffectValue(effect, uncap);
      if (Math.abs(perFire) < 0.01) continue;

      // 行動を1回も取っていなくても、編成カードの行動アビリティは ×0回 として出す。
      // (triggerFires=0 / effFires=0 を許容。total は 0 になる)
      const triggerFires = triggerCounts[effect.trigger] ?? 0;
      const effFires =
        effect.max_count != null ? Math.min(triggerFires, effect.max_count) : triggerFires;

      const key = `${effect.trigger}|${effect.stat}`;
      let acc = groups.get(key);
      if (acc == null) {
        acc = {
          trigger: effect.trigger,
          stat: effect.stat,
          fires: triggerFires,
          perFireTotal: 0,
          parts: [],
          total: 0,
          capValues: new Set<number>(),
        };
        groups.set(key, acc);
      }
      acc.perFireTotal += perFire;
      acc.parts.push(Math.round(perFire * 10) / 10);
      acc.total += perFire * effFires;
      // 上限が行動回数を実際に下回って効いている場合のみ「上限N回」を表示
      if (effect.max_count != null && triggerFires > effect.max_count) {
        acc.capValues.add(effect.max_count);
      }
    }
  }

  const entries: AbilitySummaryEntry[] = [];
  for (const acc of groups.values()) {
    acc.parts.sort((a, b) => b - a);
    // 複数カードで上限値が異なる稀ケースは最も厳しい(最小)上限を表示
    const maxCount = acc.capValues.size > 0 ? Math.min(...acc.capValues) : null;
    entries.push({
      trigger: acc.trigger,
      trigger_name: triggerDisplayName(acc.trigger),
      stat: acc.stat,
      per_fire: Math.round(acc.perFireTotal * 10) / 10,
      parts: acc.parts,
      fires: acc.fires,
      max_count: maxCount,
      total: Math.round(acc.total * 10) / 10,
    });
  }

  // 同一トリガーをまとめ、グループ合計 (= その行動で得られる総パラメ) の降順に並べる。
  // グループ内は Vo→Da→Vi→All の順 (同じ行動の Vo/Da/Vi がバラけて読みづらいのを防ぐ)。
  const groupTotal = new Map<string, number>();
  for (const e of entries) groupTotal.set(e.trigger, (groupTotal.get(e.trigger) ?? 0) + e.total);
  const statRank = (s: string): number =>
    s === 'vo' ? 0 : s === 'da' ? 1 : s === 'vi' ? 2 : s === 'all' ? 3 : 4;
  entries.sort((a, b) => {
    const gb = groupTotal.get(b.trigger)!;
    const ga = groupTotal.get(a.trigger)!;
    if (gb !== ga) return gb - ga;
    if (a.trigger !== b.trigger) return a.trigger < b.trigger ? -1 : 1;
    return statRank(a.stat) - statRank(b.stat);
  });
  return entries;
}

export function recalculateWithCap(
  selected: CardScore[],
  baseStats: StatusValues,
  statCap: number = DEFAULT_STAT_CAP,
): void {
  let accVo = baseStats.vo,
    accDa = baseStats.da,
    accVi = baseStats.vi;

  for (const cs of selected) {
    const prevTotal =
      Math.min(accVo, statCap) +
      Math.min(accDa, statCap) +
      Math.min(accVi, statCap);

    accVo += cs.raw_vo;
    accDa += cs.raw_da;
    accVi += cs.raw_vi;

    const newTotal =
      Math.min(accVo, statCap) +
      Math.min(accDa, statCap) +
      Math.min(accVi, statCap);

    cs.total_value = newTotal - prevTotal;
  }
}

// --- Generate label ---

export function generateLabel(
  cardTypeSlots: Record<string, number>,
  freeSlots: number = 0,
): string {
  const nameMap: Record<string, string> = {
    vo: 'Vocal',
    da: 'Dance',
    vi: 'Visual',
  };

  const parts: string[] = [];
  const sorted = Object.entries(cardTypeSlots).sort((a, b) => b[1] - a[1]);
  for (const [key, value] of sorted) {
    if (value > 0) {
      const name = nameMap[key] ?? key;
      parts.push(`${name} ${value}`);
    }
  }
  if (freeSlots > 0) {
    parts.push(`フリー ${freeSlots}`);
  }
  return parts.join(' / ') + ' 編成';
}

