import type {
  SupportCard,
  CardEffect,
  TrainingPlan,
  WeekSchedule,
  StatusValues,
  AdditionalCounts,
  TurnChoice,
  Character,
  MemoryBonus,
} from '../types/models';
import type { ActionType } from '../types/enums';
import { additionalCountsToRecord } from '../types/models';
import type { CardScore, EffectBreakdown, DeckResult, TeamBonusContributor } from '../types/results';
import { sv } from '../utils/statusValues';
import { getUncapLevel, getEffectValue, calculate, getEventParamBoostPercent } from './statusCalculation';
import { DEFAULT_STAT_CAP } from '../utils/constants';

/**
 * overflow罰則オプション。指定された場合、合計overflow が threshold を超えた時のみ
 * × 2 罰則を適用 (cap を大幅に超過するピックを抑制し、別属性カードへの差し替えを誘導)。
 * undefined の場合は罰則無し。
 */
export interface OverflowPenaltyConfig {
  threshold: number;
}

// --- Helper: WeekSchedule utilities ---

function isFixedEvent(w: WeekSchedule): boolean {
  return w.type === 'fixed_event' || w.type === 'exam' || w.type === 'audition';
}

function getLesson(w: WeekSchedule, lessonType: string) {
  return w.lessons.find((l) => l.type === lessonType) ?? undefined;
}

// --- Trigger display name ---

function triggerDisplayName(trigger: string): string {
  const map: Record<string, string> = {
    equip: '装備',
    sp_end: 'SP終了',
    lesson_end: 'レッスン終了',
    class_end: '授業終了',
    outing_end: 'お出かけ終了',
    consultation: '相談',
    activity_supply: '活動支給',
    exam_end: '試験終了',
    special_training: '特別指導',
    skill_acquire: 'スキル獲得',
    skill_ssr_acquire: 'スキル(SSR)獲得',
    skill_enhance: 'スキル強化',
    skill_delete: 'スキル削除',
    skill_custom: 'スキルカスタム',
    skill_change: 'スキルチェンジ',
    active_enhance: 'アクティブ強化',
    active_delete: 'アクティブ削除',
    mental_acquire: 'メンタル獲得',
    mental_enhance: 'メンタル強化',
    mental_delete: 'メンタル削除',
    active_acquire: 'アクティブ獲得',
    genki_acquire: '元気獲得',
    good_condition_acquire: '好調獲得',
    good_impression_acquire: '好印象獲得',
    conserve_acquire: '温存獲得',
    concentrate_acquire: '集中獲得',
    motivation_acquire: 'やる気獲得',
    fullpower_acquire: '全力獲得',
    aggressive_acquire: '強気獲得',
    p_item_acquire: 'Pアイテム獲得',
    p_drink_acquire: 'Pドリンク獲得',
    consultation_drink: '相談ドリンク交換',
    rest: '休憩',
    vo_sp_end: 'VoSP終了',
    da_sp_end: 'DaSP終了',
    vi_sp_end: 'ViSP終了',
    vo_lesson_end: 'Voレッスン終了',
    da_lesson_end: 'Daレッスン終了',
    vi_lesson_end: 'Viレッスン終了',
  };
  return map[trigger] ?? trigger;
}

// --- Build reason text ---

function buildReasonText(
  effect: CardEffect,
  triggerCounts: Record<string, number>,
  uncapLevel: number,
  card: SupportCard,
): string {
  const prefix = effect.source === 'item' ? '[アイテム] ' : '';
  const triggerName = triggerDisplayName(effect.trigger);
  const stat = effect.stat.toUpperCase();
  const val = getEffectValue(effect, uncapLevel);

  if (effect.trigger === 'equip') {
    if (effect.value_type === 'flat' && effect.event_param) {
      const boost = getEventParamBoostPercent(card, uncapLevel);
      const result = Math.floor(val * (1 + boost / 100));
      return `${prefix}${stat} 初期値+${Math.floor(val)}(+${Math.floor(boost)}%)=${result}`;
    }
    switch (effect.value_type) {
      case 'sp_rate':
        return `${prefix}${stat} SP率+${val}%`;
      case 'para_bonus':
        return `${prefix}パラボ+${val}%`;
      default:
        return `${prefix}${stat} 初期値+${Math.floor(val)}`;
    }
  }

  let fires = triggerCounts[effect.trigger] ?? 0;
  if (effect.max_count != null) {
    fires = Math.min(fires, effect.max_count);
  }

  const countInfo =
    effect.max_count != null
      ? `(${fires}/${effect.max_count}回)`
      : `(×${fires})`;

  switch (effect.value_type) {
    case 'flat':
      return `${prefix}${triggerName} ${stat}+${Math.floor(val)} ${countInfo}`;
    default:
      return `${prefix}${triggerName} ${stat}+${val}% ${countInfo}`;
  }
}

// --- Calculate flat value ---

function calculateFlatValue(
  effect: CardEffect,
  triggerCounts: Record<string, number>,
  uncapLevel: number,
  card: SupportCard,
): number {
  let val = getEffectValue(effect, uncapLevel);
  if (effect.trigger === 'equip') {
    if (effect.event_param) {
      val *= 1 + getEventParamBoostPercent(card, uncapLevel) / 100;
    }
    return val;
  }

  let fires = triggerCounts[effect.trigger] ?? 0;
  if (effect.max_count != null) {
    fires = Math.min(fires, effect.max_count);
  }

  return val * fires;
}

// --- Count triggers ---

export function countTriggers(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
  _mainStats: string[],
  turnChoices?: TurnChoice[],
): Record<string, number> {
  const counts: Record<string, number> = {};

  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => a.week - b.week);

  const totalLessons = Object.values(lessonAllocation).reduce(
    (sum, v) => sum + v,
    0,
  );
  counts['sp_end'] = Math.min(totalLessons, lessonWeeks.length);
  counts['lesson_end'] = counts['sp_end'];

  // 属性別SP終了・レッスン終了トリガー
  for (const [key, value] of Object.entries(lessonAllocation)) {
    if (value <= 0) continue;
    counts[`${key}_sp_end`] = value;
    counts[`${key}_lesson_end`] = value;
  }

  // 試験イベント数はスケジュールから確定
  for (const week of plan.schedule) {
    if (isFixedEvent(week)) {
      counts['exam_end'] = (counts['exam_end'] ?? 0) + 1;
    }
  }

  // HIFモード等、ユーザがターン選択を明示している場合は実選択ベースで集計する。
  // available_actions の優先度ベースだと「Day を 活動支給→お出かけ に変えても活動支給回数が減らない」
  // という不整合が起きるため。
  if (turnChoices != null) {
    for (const tc of turnChoices) {
      const a = tc.chosen_action as string;
      if (a === 'vo_lesson' || a === 'da_lesson' || a === 'vi_lesson') continue;
      if (a === 'vo_class' || a === 'da_class' || a === 'vi_class') {
        counts['class_end'] = (counts['class_end'] ?? 0) + 1;
      } else if (a === 'outing') {
        counts['outing_end'] = (counts['outing_end'] ?? 0) + 1;
      } else if (a === 'consultation') {
        counts['consultation'] = (counts['consultation'] ?? 0) + 1;
      } else if (a === 'activity_supply') {
        counts['activity_supply'] = (counts['activity_supply'] ?? 0) + 1;
      } else if (a === 'special_training') {
        counts['special_training'] = (counts['special_training'] ?? 0) + 1;
      }
    }
    return counts;
  }

  for (const week of plan.schedule) {
    if (isFixedEvent(week)) continue;
    if (week.lessons.length > 0) continue;

    const actions = week.available_actions;
    if (actions.includes('activity_supply')) {
      counts['activity_supply'] = (counts['activity_supply'] ?? 0) + 1;
    } else if (actions.includes('outing')) {
      counts['outing_end'] = (counts['outing_end'] ?? 0) + 1;
    } else if (actions.includes('consultation')) {
      counts['consultation'] = (counts['consultation'] ?? 0) + 1;
    } else if (actions.includes('special_training')) {
      counts['special_training'] = (counts['special_training'] ?? 0) + 1;
    } else if (
      actions.includes('vo_class') ||
      actions.includes('da_class') ||
      actions.includes('vi_class')
    ) {
      counts['class_end'] = (counts['class_end'] ?? 0) + 1;
    }
  }

  return counts;
}

// --- Estimate base stats ---

export function estimateBaseStats(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
): StatusValues {
  let vo = 0,
    da = 0,
    vi = 0;

  // レッスンのSPパーフェクト基礎値を配分に従って加算
  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => a.week - b.week);

  // 各属性のレッスン回数分、後ろの週(高い値)から割り当て
  const weekQueue = [...lessonWeeks].sort((a, b) => b.week - a.week);

  const sortedAllocation = Object.entries(lessonAllocation).sort(
    (a, b) => b[1] - a[1],
  );

  let queueIndex = 0;
  for (const [statKey, count] of sortedAllocation) {
    for (let i = 0; i < count && queueIndex < weekQueue.length; i++) {
      const w = weekQueue[queueIndex++];
      const lesson = getLesson(w, statKey);
      if (lesson != null) {
        vo += lesson.sp_bonus.vo;
        da += lesson.sp_bonus.da;
        vi += lesson.sp_bonus.vi;
      }
    }
  }

  // 授業の基礎値（メイン属性に全額配分と仮定）
  for (const week of plan.schedule) {
    if (week.classes.length > 0) {
      // 最大値の授業を加算
      const bestClass = [...week.classes].sort(
        (a, b) =>
          b.sp_bonus.vo +
          b.sp_bonus.da +
          b.sp_bonus.vi -
          (a.sp_bonus.vo + a.sp_bonus.da + a.sp_bonus.vi),
      )[0];
      vo += bestClass.sp_bonus.vo;
      da += bestClass.sp_bonus.da;
      vi += bestClass.sp_bonus.vi;
    }

    // 固定イベント
    if (isFixedEvent(week) && week.status_gain != null) {
      vo += week.status_gain.vo;
      da += week.status_gain.da;
      vi += week.status_gain.vi;
    }
  }

  return sv(vo, da, vi);
}

// --- Calculate lesson stat totals ---

export function calculateLessonStatTotals(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
): StatusValues {
  let vo = 0,
    da = 0,
    vi = 0;

  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => b.week - a.week);

  const weekQueue = [...lessonWeeks];

  const sortedAllocation = Object.entries(lessonAllocation).sort(
    (a, b) => b[1] - a[1],
  );

  let queueIndex = 0;
  for (const [statKey, count] of sortedAllocation) {
    for (let i = 0; i < count && queueIndex < weekQueue.length; i++) {
      const w = weekQueue[queueIndex++];
      const lesson = getLesson(w, statKey);
      if (lesson != null) {
        vo += lesson.sp_bonus.vo;
        da += lesson.sp_bonus.da;
        vi += lesson.sp_bonus.vi;
      }
    }
  }

  // HIFモードの選抜試験 (基礎値+配分値) もパラボ対象になるので加算する。
  // buildPlanAndChoices で audition の status_gain には既に base+alloc が反映されている。
  for (const w of plan.schedule) {
    if (
      w.type === 'audition' &&
      (w.hif_exam_base != null || w.hif_exam_distributed != null) &&
      w.status_gain != null
    ) {
      vo += w.status_gain.vo;
      da += w.status_gain.da;
      vi += w.status_gain.vi;
    }
  }

  return sv(vo, da, vi);
}

// --- Trigger count bonus consumer pool aggregation ---

/** trigger_count_bonus の単体スコアリング用、消費側カード1枚分の per-fire 寄与情報 */
export interface TriggerBonusContributor {
  cardId: string;
  cardName: string;
  perFire: StatusValues;
}

/** trigger_count_bonus の単体スコアリング用、対象トリガーごとの集計情報 */
export interface TriggerBonusEntry {
  /** 全消費側カードの per-fire ステータス合計 (推定スコア計算に使う) */
  total: StatusValues;
  /** 消費側カード一覧 (breakdown 表示用、寄与額の降順) */
  contributors: TriggerBonusContributor[];
}

/**
 * trigger_count_bonus 効果の単体スコアリングのため、対象トリガーごとに
 * プール内全ての消費側カードの per-fire ステータスを事前計算する。
 */
function computeTriggerBonusInfo(
  pool: SupportCard[],
  uncapLevels: Record<string, number> | undefined,
): Record<string, TriggerBonusEntry> {
  // どの trigger_target を集計すべきか抽出
  const targets = new Set<string>();
  for (const card of pool) {
    for (const effect of card.effects) {
      if (effect.value_type === 'trigger_count_bonus' && effect.trigger_target) {
        targets.add(effect.trigger_target);
      }
    }
  }

  const result: Record<string, TriggerBonusEntry> = {};
  for (const target of targets) {
    const candidates: Array<TriggerBonusContributor & { total: number }> = [];
    for (const card of pool) {
      const uncap = getUncapLevel(card, uncapLevels);
      let cVo = 0, cDa = 0, cVi = 0;
      for (const effect of card.effects) {
        if (effect.trigger !== target || effect.value_type !== 'flat') continue;
        const v = Math.floor(getEffectValue(effect, uncap));
        switch (effect.stat) {
          case 'vo': cVo += v; break;
          case 'da': cDa += v; break;
          case 'vi': cVi += v; break;
          case 'all':
            cVo += v; cDa += v; cVi += v;
            break;
        }
      }
      const total = cVo + cDa + cVi;
      if (total > 0) {
        candidates.push({
          cardId: card.id,
          cardName: card.name,
          perFire: { vo: cVo, da: cDa, vi: cVi },
          total,
        });
      }
    }
    candidates.sort((a, b) => b.total - a.total);
    result[target] = {
      total: {
        vo: candidates.reduce((s, c) => s + c.perFire.vo, 0),
        da: candidates.reduce((s, c) => s + c.perFire.da, 0),
        vi: candidates.reduce((s, c) => s + c.perFire.vi, 0),
      },
      contributors: candidates.map(({ cardId, cardName, perFire }) => ({ cardId, cardName, perFire })),
    };
  }
  return result;
}

// --- Calculate card contribution ---

export function calculateCardContribution(
  card: SupportCard,
  triggerCounts: Record<string, number>,
  _lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels?: Record<string, number>,
  triggerBonusInfo?: Record<string, TriggerBonusEntry>,
  /**
   * デッキ確定後の再計算で、producer 側の trigger_count_bonus を raw_* に加算しないためのフラグ。
   * デッキ確定後は consumer 側の flat 効果が adjustedCounts 経由で実発火回数分加算されるため、
   * producer 側でも加算すると二重カウントになる。
   */
  skipTriggerBonusSelfContribution: boolean = false,
): CardScore {
  const uncap = getUncapLevel(card, uncapLevels);
  let vo = 0,
    da = 0,
    vi = 0;
  let teamBonusTotal = 0;
  const teamBonusContributors: TeamBonusContributor[] = [];
  const breakdowns: EffectBreakdown[] = [];

  for (const effect of card.effects) {
    // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
    if (effect.value_type === 'sp_rate') continue;

    // trigger_count_bonus: 自カードは追加でステータスを得ないが、他カードのトリガー発火回数を増やす
    if (effect.value_type === 'trigger_count_bonus') {
      const target = effect.trigger_target;
      if (!target) continue;
      const perScale = getEffectValue(effect, uncap);
      const scaleCount = effect.scales_with
        ? (triggerCounts[effect.scales_with] ?? 0)
        : 1;
      let bonusFires = perScale * scaleCount;
      if (effect.max_count != null) bonusFires = Math.min(bonusFires, effect.max_count);
      bonusFires = Math.floor(bonusFires);
      if (bonusFires <= 0) continue;

      const entry = triggerBonusInfo?.[target];
      if (entry == null) continue;

      // 自カードを除外して消費側カードを集計 (producer 自身が consumer 効果を持つ場合の二重カウント防止)
      let synergyVoSum = 0, synergyDaSum = 0, synergyViSum = 0;
      const contribRows: EffectBreakdown[] = [];
      for (const c of entry.contributors) {
        if (c.cardId === card.id) continue;
        const cVo = c.perFire.vo * bonusFires;
        const cDa = c.perFire.da * bonusFires;
        const cVi = c.perFire.vi * bonusFires;
        const cTotal = cVo + cDa + cVi;
        if (cTotal <= 0) continue;
        synergyVoSum += cVo;
        synergyDaSum += cDa;
        synergyViSum += cVi;
        const perFireDesc = [
          c.perFire.vo > 0 ? `Vo+${c.perFire.vo}` : '',
          c.perFire.da > 0 ? `Da+${c.perFire.da}` : '',
          c.perFire.vi > 0 ? `Vi+${c.perFire.vi}` : '',
        ].filter(Boolean).join('/');
        const mainStat: 'vo' | 'da' | 'vi' =
          cVo >= cDa && cVo >= cVi ? 'vo' : cDa >= cVi ? 'da' : 'vi';
        contribRows.push({
          reason: `  ↳ ${c.cardName} (${perFireDesc}/回)`,
          stat: mainStat,
          value: Math.round(cTotal * 10) / 10,
        });
        teamBonusContributors.push({
          card_name: c.cardName,
          value: Math.floor(cTotal),
        });
      }
      if (contribRows.length === 0) continue;

      teamBonusTotal += synergyVoSum + synergyDaSum + synergyViSum;
      if (!skipTriggerBonusSelfContribution) {
        // 単体スコアリング時: 「想定される消費側カードへの寄与」を自スコアに加算 (ヒューリスティック)
        vo += synergyVoSum;
        da += synergyDaSum;
        vi += synergyViSum;
      }

      // ヘッダ行 (式のみ、値カラムは空表示)
      const targetName = triggerDisplayName(target);
      const scalesWith = effect.scales_with;
      const formula = scalesWith
        ? `${triggerDisplayName(scalesWith)}×${scaleCount} × ${perScale}`
        : `×${perScale}`;
      const headerSuffix = skipTriggerBonusSelfContribution ? ' → 他カードへ寄与' : '';
      breakdowns.push({
        reason: `[アイテム] ${targetName}+${bonusFires}回 (${formula})${headerSuffix}`,
        stat: 'all',
        value: 0,
      });
      breakdowns.push(...contribRows);
      continue;
    }

    if (effect.value_type === 'para_bonus') {
      // パラボは該当属性のレッスン上昇値にのみ適用
      const pct = getEffectValue(effect, uncap) / 100.0;
      let bonus = 0;
      switch (effect.stat) {
        case 'vo':
          bonus = lessonStatTotals.vo * pct;
          vo += bonus;
          break;
        case 'da':
          bonus = lessonStatTotals.da * pct;
          da += bonus;
          break;
        case 'vi':
          bonus = lessonStatTotals.vi * pct;
          vi += bonus;
          break;
        case 'all': {
          const bVo = lessonStatTotals.vo * pct;
          const bDa = lessonStatTotals.da * pct;
          const bVi = lessonStatTotals.vi * pct;
          vo += bVo;
          da += bDa;
          vi += bVi;
          bonus = bVo + bDa + bVi;
          break;
        }
      }

      if (Math.abs(bonus) < 0.01) continue;

      const reason = `パラボ(${effect.stat.toUpperCase()})+${getEffectValue(effect, uncap)}%`;
      breakdowns.push({
        reason,
        stat: effect.stat,
        value: Math.round(bonus * 10) / 10,
      });
      continue;
    }

    const value =
      effect.value_type === 'flat'
        ? calculateFlatValue(effect, triggerCounts, uncap, card)
        : 0;

    if (Math.abs(value) < 0.01) continue;

    // 内訳の理由テキスト生成
    const reason2 = buildReasonText(effect, triggerCounts, uncap, card);

    switch (effect.stat) {
      case 'vo':
        vo += value;
        break;
      case 'da':
        da += value;
        break;
      case 'vi':
        vi += value;
        break;
      case 'all':
        vo += value / 3.0;
        da += value / 3.0;
        vi += value / 3.0;
        break;
      default:
        vo += value / 3.0;
        da += value / 3.0;
        vi += value / 3.0;
        break;
    }

    breakdowns.push({
      reason: reason2,
      stat: effect.stat,
      value: Math.round(value * 10) / 10,
    });
  }

  const iVo = Math.floor(vo);
  const iDa = Math.floor(da);
  const iVi = Math.floor(vi);

  return {
    card,
    raw_vo: iVo,
    raw_da: iDa,
    raw_vi: iVi,
    team_bonus_total: Math.floor(teamBonusTotal),
    team_bonus_contributors: teamBonusContributors,
    total_value: iVo + iDa + iVi,
    breakdowns,
    is_rental: false,
    is_required: false,
  };
}

// --- Greedy fill owned slots from checkpoint ---

function greedyFillOwned(
  contributions: CardScore[],
  selectedInit: CardScore[],
  usedIdsInit: Set<string>,
  accVoInit: number,
  accDaInit: number,
  accViInit: number,
  remainingSlotsInit: Record<string, number>,
  remainingFreeInit: number,
  ownedSlots: number,
  statCap: number,
  character?: Character | null,
  overflowPenalty?: OverflowPenaltyConfig,
): {
  selected: CardScore[];
  usedIds: Set<string>;
  accVo: number;
  accDa: number;
  accVi: number;
} {
  const sel = [...selectedInit];
  const used = new Set(usedIdsInit);
  let aVo = accVoInit,
    aDa = accDaInit,
    aVi = accViInit;

  // キャラの para_bonus はカード貢献にも乗るので、accumulator 更新も同じ倍率で行う
  const voMul = 1 + (character?.para_bonus.vo ?? 0) / 100;
  const daMul = 1 + (character?.para_bonus.da ?? 0) / 100;
  const viMul = 1 + (character?.para_bonus.vi ?? 0) / 100;

  // 属性枠
  const sortedSlots = Object.entries(remainingSlotsInit).sort(
    (a, b) => b[1] - a[1],
  );
  for (const [type, count] of sortedSlots) {
    if (count <= 0) continue;
    const candidates = contributions.filter(
      (cs) =>
        (cs.card.type === type || cs.card.type === 'all' || cs.card.type === 'as') &&
        !used.has(cs.card.id),
    );
    for (let i = 0; i < count && sel.length < ownedSlots; i++) {
      const best = selectBestCard(candidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo * voMul;
      aDa += best.raw_da * daMul;
      aVi += best.raw_vi * viMul;
    }
  }

  // フリー枠
  for (let i = 0; i < remainingFreeInit && sel.length < ownedSlots; i++) {
    const freeCandidates = contributions.filter(
      (cs) => !used.has(cs.card.id),
    );
    const best = selectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
    if (best == null) break;
    sel.push(best);
    used.add(best.card.id);
    aVo += best.raw_vo * voMul;
    aDa += best.raw_da * daMul;
    aVi += best.raw_vi * viMul;
  }

  // 補充
  if (sel.length < ownedSlots) {
    const remaining = contributions.filter((cs) => !used.has(cs.card.id));
    while (sel.length < ownedSlots) {
      const best = selectBestCard(remaining, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo * voMul;
      aDa += best.raw_da * daMul;
      aVi += best.raw_vi * viMul;
    }
  }

  return { selected: sel, usedIds: used, accVo: aVo, accDa: aDa, accVi: aVi };
}

// --- Post-optimization using actual calculation ---

function meetsTypeSlots(
  cards: SupportCard[],
  cardTypeSlots: Record<string, number>,
): boolean {
  for (const [type, required] of Object.entries(cardTypeSlots)) {
    if (required <= 0) continue;
    const count = cards.filter(
      (c) => c.type === type || c.type === 'all' || c.type === 'as',
    ).length;
    if (count < required) return false;
  }
  return true;
}

/**
 * deck 確定後に SP カードが spCounts 設定を超過していたら、余剰分の保護を外す。
 * (step 1 ではレンタル枠が SP かどうか不明のため、ここで rental 込みで再評価)
 *
 * 保護が外れたカードは postOptimize で「同属性 SP のみ」のスワップ制限から解放され、
 * 非SPカード (例: いつまでも続けばいいのに) との差し替え候補になる。
 */
function unprotectExcessSpCards(
  selected: CardScore[],
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
): void {
  if (spCounts == null) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) =>
        e.trigger === 'equip' &&
        e.value_type === 'sp_rate' &&
        (e.stat === stat || e.stat === 'all'),
    );

  for (const stat of ['vo', 'da', 'vi']) {
    const need = spCounts[stat] ?? 0;
    if (need <= 0) continue;

    const spCardsForStat = selected.filter((cs) => coversStat(cs.card, stat));
    if (spCardsForStat.length <= need) continue;
    const excess = spCardsForStat.length - need;

    // 余剰分: 弱い順 (raw 総和の昇順) に保護を外す。
    // ただし rental・必須カード・既に保護されていないカードは対象外。
    const trimCandidates = spCardsForStat
      .filter(
        (cs) => !cs.is_rental && !cs.is_required && protectedIds.has(cs.card.id),
      )
      .sort(
        (a, b) =>
          a.raw_vo + a.raw_da + a.raw_vi - (b.raw_vo + b.raw_da + b.raw_vi),
      );

    for (let i = 0; i < Math.min(excess, trimCandidates.length); i++) {
      protectedIds.delete(trimCandidates[i].card.id);
    }
  }
}

/**
 * デッキ確定後、SP カードが spCounts 設定の枚数に「満たない」場合に、プール内の
 * 余剰 SP カードと差し替えて要求枚数を満たす。
 *
 * ユーザ指定の優先順位「必須カード > SP枚数 > 編成パターン」を保証するための最終強制パス。
 * - 必須カード (is_required) は絶対に外さない
 * - 既に別属性のSP要件を満たしているカードも外さない
 * - 所持枠は所持プール(cardContributions)のSPカードで、レンタル枠はレンタルプールのSPカードで補充
 * - 補充のために編成パターン(cardTypeSlots)を崩すことは許容する (SP枚数 > 編成パターン)
 */
function enforceSpCounts(
  selected: CardScore[],
  cardContributions: CardScore[],
  rentalPool: SupportCard[] | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
): void {
  if (spCounts == null) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) =>
        e.trigger === 'equip' &&
        e.value_type === 'sp_rate' &&
        (e.stat === stat || e.stat === 'all'),
    );

  // このカードが「まだ要求枚数 > 0 のいずれかの属性」のSPをカバーしているか
  // (= 外すと別属性のSP要件を壊しうるカードか)
  const coversAnyNeededSp = (card: SupportCard): boolean =>
    (['vo', 'da', 'vi'] as const).some(
      (s) => (spCounts[s] ?? 0) > 0 && coversStat(card, s),
    );

  const rawTotal = (cs: CardScore): number => cs.raw_vo + cs.raw_da + cs.raw_vi;

  for (const stat of ['vo', 'da', 'vi']) {
    const need = spCounts[stat] ?? 0;
    if (need <= 0) continue;

    let current = selected.filter((cs) => coversStat(cs.card, stat)).length;
    if (current >= need) continue;

    // 所持プールから、この属性のSPを持ち、まだデッキに居ないカード (寄与降順)
    const inDeck = () => new Set(selected.map((s) => s.card.id));
    const ownedSpCandidates = cardContributions
      .filter((cs) => coversStat(cs.card, stat) && !inDeck().has(cs.card.id))
      .sort((a, b) => rawTotal(b) - rawTotal(a));

    // 1) 所持枠を所持SPカードで補充
    while (current < need && ownedSpCandidates.length > 0) {
      // 外せる犠牲カード: 非レンタル・非必須・他のSP要件を満たしていない、寄与の弱い順
      // 同属性のカードを優先的に外して編成バランスへの影響を抑える
      const removable = selected.filter(
        (cs) => !cs.is_rental && !cs.is_required && !coversAnyNeededSp(cs.card),
      );
      if (removable.length === 0) break;
      removable.sort((a, b) => {
        const at = a.card.type === stat ? 0 : 1;
        const bt = b.card.type === stat ? 0 : 1;
        if (at !== bt) return at - bt;
        return rawTotal(a) - rawTotal(b);
      });
      const victim = removable[0];
      const sp = ownedSpCandidates.shift()!;
      const idx = selected.findIndex((cs) => cs.card.id === victim.card.id);
      selected[idx] = sp;
      protectedIds.delete(victim.card.id);
      protectedIds.add(sp.card.id);
      current++;
    }

    // 2) まだ不足 → レンタル枠をこの属性のレンタルSPカードに差し替え
    if (current < need && rentalPool != null) {
      const rentalIdx = selected.findIndex((cs) => cs.is_rental);
      if (
        rentalIdx >= 0 &&
        !coversStat(selected[rentalIdx].card, stat) &&
        !coversAnyNeededSp(selected[rentalIdx].card)
      ) {
        const used = inDeck();
        const rentalSp = rentalPool
          .filter((c) => coversStat(c, stat) && !used.has(c.id))
          .map((c) =>
            calculateCardContribution(
              c,
              triggerCounts,
              lessonAllocation,
              lessonStatTotals,
              { ...(uncapLevels ?? {}), [c.id]: 4 },
              triggerBonusInfo,
            ),
          )
          .sort((a, b) => rawTotal(b) - rawTotal(a));
        if (rentalSp.length > 0) {
          const best: CardScore = { ...rentalSp[0], is_rental: true };
          selected[rentalIdx] = best;
          protectedIds.add(best.card.id);
          current++;
        }
      }
    }
  }
}

/**
 * postOptimize はレンタル枠 (is_rental) を絶対にスワップしないため、所持カードが
 * postOptimize で入れ替わった後に「レンタル枠が最適でなくなる」ケースを補正できない。
 *
 * 例: レンタル選出時点で お城(Vo) が所持枠を占有 → レンタルに ほっぺた(Vi) が選ばれる。
 * その後 postOptimize が 所持の お城 を 自分と向き合う(Da) に差し替えると お城 が枠から外れるが、
 * レンタルは ほっぺた のまま固定される。本来は お城 をレンタルに据えた方が合計が高い。
 *
 * このパスは postOptimize 後に、実際の計算(calculate)でレンタル枠を再評価し、
 * レンタルプール内の最良カードに差し替える。タイプ枠・SP枚数の制約は維持する。
 */
function optimizeRentalCard(
  selected: CardScore[],
  rentalPool: SupportCard[] | undefined,
  planType: string | undefined,
  triggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
  triggerBonusInfo: Record<string, TriggerBonusEntry> | undefined,
  protectedIds: Set<string>,
  spCounts: Record<string, number> | undefined,
  plan: TrainingPlan,
  additionalCounts: AdditionalCounts | undefined,
  statCap: number,
  character: Character | null,
  memoryBonuses: MemoryBonus[] | null,
  cardTypeSlots: Record<string, number> | undefined,
  turnChoices: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  if (rentalPool == null) return;
  const rentalIdx = selected.findIndex((cs) => cs.is_rental);
  if (rentalIdx < 0) return;
  const current = selected[rentalIdx];
  if (current.is_required) return;

  const coversStat = (card: SupportCard, stat: string): boolean =>
    card.effects.some(
      (e) => e.trigger === 'equip' && e.value_type === 'sp_rate' && (e.stat === stat || e.stat === 'all'),
    );
  const meetsSpCounts = (cards: SupportCard[]): boolean => {
    if (spCounts == null) return true;
    for (const [stat, need] of Object.entries(spCounts)) {
      if (need <= 0) continue;
      if (cards.filter((c) => coversStat(c, stat)).length < need) return false;
    }
    return true;
  };

  // 評価用: レンタル候補を 4凸 として実際の計算で合計を求める (postOptimize と同一ロジック)
  const evaluateFull = (cards: SupportCard[], rentalCardId: string): number => {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const cs of selected) {
      if (cs.is_rental) uc[cs.card.id] = 4;
    }
    uc[rentalCardId] = 4;
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null)
      .final_status;
    let total = Math.min(fs.vo, statCap) + Math.min(fs.da, statCap) + Math.min(fs.vi, statCap);
    if (overflowPenalty) {
      const overflow =
        Math.max(0, fs.vo - statCap) + Math.max(0, fs.da - statCap) + Math.max(0, fs.vi - statCap);
      if (overflow > overflowPenalty.threshold) total -= overflow * 2;
    }
    return total;
  };

  const ownedIds = new Set(
    selected.filter((_, i) => i !== rentalIdx).map((s) => s.card.id),
  );
  let pool = rentalPool.filter((c) => !ownedIds.has(c.id));
  if (planType != null && planType !== '') {
    pool = pool.filter(
      (c) => c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free',
    );
  }

  const currentCards = selected.map((s) => s.card);
  let bestTotal = evaluateFull(currentCards, current.card.id);
  let bestCard: SupportCard | null = null;

  // 全プールに calculate を回すと重いので、素の寄与上位のみ実評価する。
  const rentalUncap: Record<string, number> = {};
  for (const c of pool) rentalUncap[c.id] = 4;
  const ranked = pool
    .map((c) => ({
      card: c,
      score: (() => {
        const cs = calculateCardContribution(c, triggerCounts, lessonAllocation, lessonStatTotals, rentalUncap, triggerBonusInfo);
        return cs.raw_vo + cs.raw_da + cs.raw_vi;
      })(),
    }))
    .sort((a, b) => b.score - a.score)
    .slice(0, 40)
    .map((x) => x.card);

  for (const cand of ranked) {
    const testCards = [...currentCards];
    testCards[rentalIdx] = cand;
    if (cardTypeSlots != null && !meetsTypeSlots(testCards, cardTypeSlots)) continue;
    if (!meetsSpCounts(testCards)) continue;
    const total = evaluateFull(testCards, cand.id);
    if (total > bestTotal) {
      bestTotal = total;
      bestCard = cand;
    }
  }

  if (bestCard != null) {
    const cs = calculateCardContribution(
      bestCard,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      { ...(uncapLevels ?? {}), [bestCard.id]: 4 },
      triggerBonusInfo,
    );
    selected[rentalIdx] = { ...cs, is_rental: true, is_required: false };
    protectedIds.delete(current.card.id);
  }
}

function postOptimize(
  selected: CardScore[],
  candidates: CardScore[],
  protectedIds: Set<string>,
  plan: TrainingPlan,
  mainStats: string[],
  uncapLevels?: Record<string, number>,
  additionalCounts?: AdditionalCounts,
  statCap?: number,
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  cardTypeSlots?: Record<string, number>,
  turnChoicesOverride?: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): void {
  // HIFモードのようにユーザが明示的にターン選択している場合は、合成 turnChoices ではなく
  // 実選択を使って評価する。これをやらないと postOptimize の評価が実際のデッキ計算と食い違う。
  const turnChoices = turnChoicesOverride ?? buildTurnChoices(plan, mainStats);
  const cap = statCap ?? plan.status_limit ?? DEFAULT_STAT_CAP;

  function evaluateFull(cards: SupportCard[]): { total: number; vo: number; da: number; vi: number } {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const cs of selected) {
      if (cs.is_rental) uc[cs.card.id] = 4;
    }
    // 最終表示値と一致させるため、キャラ補正・メモリーボーナスを含めて評価する
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts, character ?? null, memoryBonuses ?? null)
      .final_status;
    const cappedVo = Math.min(fs.vo, cap);
    const cappedDa = Math.min(fs.da, cap);
    const cappedVi = Math.min(fs.vi, cap);
    let total = cappedVo + cappedDa + cappedVi;
    // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則を適用
    if (overflowPenalty) {
      const overflow = Math.max(0, fs.vo - cap) + Math.max(0, fs.da - cap) + Math.max(0, fs.vi - cap);
      if (overflow > overflowPenalty.threshold) {
        total -= overflow * 2;
      }
    }
    return { total, vo: fs.vo, da: fs.da, vi: fs.vi };
  }

  let improved: boolean;
  do {
    improved = false;
    const currentCards = selected.map((c) => c.card);
    let currentEval = evaluateFull(currentCards);

    for (let si = 0; si < selected.length; si++) {
      const ownedCard = selected[si];
      if (ownedCard.is_rental) continue;
      // 必須カードは無条件でスワップ不可
      if (ownedCard.is_required) continue;

      const hasSpRate = (card: SupportCard) =>
        card.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate');
      const getSpRateStat = (card: SupportCard): string | undefined =>
        card.effects.find((e) => e.trigger === 'equip' && e.value_type === 'sp_rate')?.stat;
      const ownedIsProtectedSp =
        protectedIds.has(ownedCard.card.id) && hasSpRate(ownedCard.card);
      const ownedIsProtectedNonSp =
        protectedIds.has(ownedCard.card.id) && !ownedIsProtectedSp;
      // 非SPの保護カードはスキップ
      if (ownedIsProtectedNonSp) continue;

      const ownedType = ownedCard.card.type;
      const ownedSpStat = ownedIsProtectedSp ? getSpRateStat(ownedCard.card) : undefined;

      for (const candidate of candidates) {
        if (selected.some((c) => c.card.id === candidate.card.id)) continue;

        // SP率で保護されたカードは、同じ属性のSP率を持つ候補とのみ交換可能
        // (ユーザ指定の spCounts 分布を postOptimize で崩さないため)
        if (ownedIsProtectedSp) {
          const candStat = getSpRateStat(candidate.card);
          if (candStat == null || candStat !== ownedSpStat) continue;
        }

        const testCards = [...currentCards];
        testCards[si] = candidate.card;

        // タイプ制約: cardTypeSlots の最低要件 (例: Da 2枚以上) を満たすスワップのみ許可
        if (
          candidate.card.type !== ownedType &&
          candidate.card.type !== 'all' &&
          candidate.card.type !== 'as' &&
          ownedType !== 'all' &&
          ownedType !== 'as' &&
          cardTypeSlots != null &&
          !meetsTypeSlots(testCards, cardTypeSlots)
        ) {
          continue;
        }

        const testEval = evaluateFull(testCards);
        // 合計値が同点の場合、raw_total (キャップ前の素の寄与) が大きいカードを優先。
        // 両方がキャップを張り付かせる場合に「より強いSSR」を採用するためのタイブレーカ。
        const candRawTotal = candidate.raw_vo + candidate.raw_da + candidate.raw_vi;
        const ownedRawTotal = ownedCard.raw_vo + ownedCard.raw_da + ownedCard.raw_vi;
        const isImprovement =
          testEval.total > currentEval.total ||
          (testEval.total === currentEval.total && candRawTotal > ownedRawTotal);
        if (isImprovement) {
          selected[si] = candidate;
          // SP率保護を新カードに引き継ぐ
          if (ownedIsProtectedSp) {
            protectedIds.delete(ownedCard.card.id);
            protectedIds.add(candidate.card.id);
          }
          currentEval = testEval;
          improved = true;
          break;
        }
      }
      if (improved) break;
    }
  } while (improved);
}

function buildTurnChoices(
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

function selectBestCard(
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

function calculateCappedTotal(
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
function recomputeBreakdownsDeckAware(
  selected: CardScore[],
  baseTriggerCounts: Record<string, number>,
  lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels: Record<string, number> | undefined,
): void {
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
  if (Object.keys(deckBonuses).length === 0) return;

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
}

function recalculateWithCap(
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

// --- Select optimal deck ---

export function selectOptimalDeck(
  plan: TrainingPlan,
  allCards: SupportCard[],
  lessonAllocation: Record<string, number>,
  cardTypeSlots: Record<string, number>,
  mainStats: string[],
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  freeSlots: number = 0,
  requiredCardIds?: string[],
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  /**
   * HIFモードなど、ユーザが明示的にターン選択を指定している場合の override。
   * 渡された場合 postOptimize の評価でもこの選択を使う (デフォルトの buildTurnChoices ではなく)
   */
  turnChoicesOverride?: TurnChoice[],
  /**
   * HIFモードでMAX大幅超過時のみ再抽選を促す optional オプション。
   * 渡された場合、selectBestCard / postOptimize の評価で × 2 overflow罰則が条件付きで適用される。
   */
  overflowPenalty?: OverflowPenaltyConfig,
): DeckResult {
  const statCap = plan.status_limit;
  const triggerCounts = countTriggers(plan, lessonAllocation, mainStats, turnChoicesOverride);

  if (additionalCounts != null) {
    const addRec = additionalCountsToRecord(additionalCounts);
    for (const [key, value] of Object.entries(addRec)) {
      if (value > 0) {
        triggerCounts[key] = (triggerCounts[key] ?? 0) + value;
      }
    }
  }

  // 育成タイプでフィルタ
  let eligible = allCards;
  if (planType != null && planType !== '') {
    eligible = allCards.filter(
      (c) =>
        c.plan == null || c.plan === '' || c.plan === planType || c.plan === 'free',
    );
  }

  // レッスン・イベント等のカード無しベースステータスを推定
  const baseStats = estimateBaseStats(plan, lessonAllocation);

  // レッスンの属性別合計SpBonusを事前計算
  const lessonStatTotals = calculateLessonStatTotals(plan, lessonAllocation);

  // trigger_count_bonus 効果 (Pアイテム由来でドリンク等を追加生成する効果) の単体スコアリング用情報
  // 「もしこのカードが選ばれた場合、追加で発火する trigger_target は他カードに何ポイント寄与するか」
  // を見積もるため、対象トリガーを持つ消費側カードの per-fire 値を集計しておく
  const triggerBonusInfo = computeTriggerBonusInfo(eligible, uncapLevels);

  // 全カードの属性別寄与を事前計算
  const cardContributions = eligible.map((card) =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    ),
  );

  // 全カードプール (フィルタ外も補充用に)
  const allContributions = allCards.map((card) =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
      triggerBonusInfo,
    ),
  );

  // 属性枠ごとに選択 (上限考慮)
  let selected: CardScore[] = [];
  let usedIds = new Set<string>();

  // 現在の累積ステータス (ベース + 選択済みカード)
  // キャラ補正を含めることで、cap-aware なカード選出が character の偏りを反映できるようにする
  let accVo = baseStats.vo,
    accDa = baseStats.da,
    accVi = baseStats.vi;
  if (character != null) {
    accVo += character.base_status_bonus.vo;
    accDa += character.base_status_bonus.da;
    accVi += character.base_status_bonus.vi;
    // para_bonus はレッスン上昇値に対する%補正 (近似)
    accVo += lessonStatTotals.vo * (character.para_bonus.vo / 100);
    accDa += lessonStatTotals.da * (character.para_bonus.da / 100);
    accVi += lessonStatTotals.vi * (character.para_bonus.vi / 100);
  }
  // キャラの para_bonus はカード貢献にも乗る。accumulator 更新でも同じ倍率を適用する
  const accVoMul = 1 + (character?.para_bonus.vo ?? 0) / 100;
  const accDaMul = 1 + (character?.para_bonus.da ?? 0) / 100;
  const accViMul = 1 + (character?.para_bonus.vi ?? 0) / 100;

  // 属性枠・フリー枠の残数を管理するローカルコピー
  const remainingSlots: Record<string, number> = { ...cardTypeSlots };
  let remainingFree = freeSlots;

  // ステップ0: 必須カードを強制挿入
  let requiredRentalCard: CardScore | undefined = undefined;
  const protectedIds = new Set<string>();

  // ステップ1のSP率先取り用に「必須カードで消費した分を減算した」残り必要枚数。
  // unprotectExcessSpCards / enforceSpCounts では必須カードを含む元の spCounts(総数)で
  // 判定する必要があるため、減算後のカウントはこのローカル変数にのみ反映し、
  // spCounts 自体は上書きしない (上書きすると SP枚数の最終保証が必須カード分だけ過小評価される)。
  const spCountsForFill: Record<string, number> = spCounts != null ? { ...spCounts } : {};

  if (requiredCardIds != null && requiredCardIds.length > 0) {

    for (const cardId of requiredCardIds) {
      // allCards から探す、見つからなければ rentalPool からも探す
      const card = allCards.find((c) => c.id === cardId)
        ?? rentalPool?.find((c) => c.id === cardId);
      if (card == null || usedIds.has(cardId)) continue;

      // 所持判定: rentalPool が null なら全カード所持扱い、そうでなければ eligible に含まれるか
      const isOwned = rentalPool == null || eligible.some((c) => c.id === cardId);

      // 凸数: 所持なら uncapLevels、未所持なら4凸
      const reqUncap: Record<string, number> = { ...(uncapLevels ?? {}) };
      if (!isOwned) {
        reqUncap[cardId] = 4;
      } else if (!(cardId in reqUncap)) {
        reqUncap[cardId] = 4;
      }

      const contribution = calculateCardContribution(
        card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        reqUncap,
        triggerBonusInfo,
      );
      contribution.is_required = true;

      if (!isOwned && rentalPool != null) {
        // 未所持 → レンタル枠として保留（selected に入れない）
        contribution.is_rental = true;
        requiredRentalCard = contribution;
        usedIds.add(cardId);
        protectedIds.add(cardId);
      } else {
        // 所持 → 所持枠として追加
        selected.push(contribution);
        usedIds.add(cardId);
        protectedIds.add(cardId);
        accVo += contribution.raw_vo * accVoMul;
        accDa += contribution.raw_da * accDaMul;
        accVi += contribution.raw_vi * accViMul;

        // スロット消費 ("as" は "all" と同等に扱う)
        const isAllLike = card.type === 'all' || card.type === 'as';
        if (!isAllLike && card.type in remainingSlots && remainingSlots[card.type] > 0) {
          remainingSlots[card.type]--;
        } else if (isAllLike) {
          // "all"/"as" タイプ: 最大残数の属性枠を消費
          const maxSlotKey = Object.entries(remainingSlots)
            .sort((a, b) => b[1] - a[1])[0];
          if (maxSlotKey && maxSlotKey[1] > 0) {
            remainingSlots[maxSlotKey[0]]--;
          } else {
            remainingFree = Math.max(0, remainingFree - 1);
          }
        } else {
          remainingFree = Math.max(0, remainingFree - 1);
        }

        // SP率カード判定: 必須カードがSP率エフェクトを持つなら spCounts を減算
        const spEffect = card.effects.find(
          (e) => e.trigger === 'equip' && e.value_type === 'sp_rate',
        );
        if (spEffect != null) {
          for (const key of Object.keys(spCountsForFill)) {
            if (
              (card.type === key || card.type === 'all' || card.type === 'as') &&
              spCountsForFill[key] > 0
            ) {
              spCountsForFill[key]--;
              break;
            }
          }
        }
      }
    }
  }

  // ステップ1: SP率カードをユーザ指定枚数分、先に確保
  const spCardSlotStat: Record<string, string> = {}; // cardId -> 消費したスロットのstat key
  const spCardUsedFree = new Set<string>(); // フリー枠を消費したcardId
  if (spCounts != null) {
    // 必須カードで消費済みの分を差し引いた残り枚数のみ先取りする
    for (const [stat, need] of Object.entries(spCountsForFill)) {
      if (need <= 0) continue;

      // この属性のSP率を持つカードをステータス寄与順で選ぶ ("as" は "all" と同等)
      const spCandidates = cardContributions.filter(
        (cs) =>
          (cs.card.type === stat || cs.card.type === 'all' || cs.card.type === 'as') &&
          !usedIds.has(cs.card.id) &&
          cs.card.effects.some(
            (e) => e.trigger === 'equip' && e.value_type === 'sp_rate',
          ),
      );

      for (let i = 0; i < need; i++) {
        const best = selectBestCard(
          spCandidates,
          usedIds,
          accVo,
          accDa,
          accVi,
          statCap,
          character,
          overflowPenalty,
        );
        if (best == null) break;

        selected.push(best);
        usedIds.add(best.card.id);
        protectedIds.add(best.card.id); // SP率カードはポスト最適化でスワップしない
        accVo += best.raw_vo * accVoMul;
        accDa += best.raw_da * accDaMul;
        accVi += best.raw_vi * accViMul;

        // SP率カードが属性枠にカウントされるか、フリー枠を消費するか判定
        if (stat in remainingSlots && remainingSlots[stat] > 0) {
          spCardSlotStat[best.card.id] = stat;
          remainingSlots[stat]--;
        } else {
          spCardUsedFree.add(best.card.id);
          remainingFree = Math.max(0, remainingFree - 1);
        }
      }
    }
  }

  // レンタルモード: 所持5枠 + レンタル1枠
  const ownedSlots = rentalPool != null ? 5 : 6;

  // チェックポイント保存（レンタルパターンC用）
  const checkpointSelected = [...selected];
  const checkpointUsedIds = new Set(usedIds);
  const checkpointAccVo = accVo,
    checkpointAccDa = accDa,
    checkpointAccVi = accVi;
  const checkpointRemainingSlots = { ...remainingSlots };
  const checkpointRemainingFree = remainingFree;

  // ステップ2: グリーディに所持枠を埋める
  // レンタル必須カードがある場合はそのステータスを事前加算して補完的なカードを選ぶ
  {
    const fillAccVo = accVo + (requiredRentalCard?.raw_vo ?? 0);
    const fillAccDa = accDa + (requiredRentalCard?.raw_da ?? 0);
    const fillAccVi = accVi + (requiredRentalCard?.raw_vi ?? 0);
    const fill = greedyFillOwned(
      cardContributions,
      selected,
      usedIds,
      fillAccVo,
      fillAccDa,
      fillAccVi,
      remainingSlots,
      remainingFree,
      ownedSlots,
      statCap,
      character,
      overflowPenalty,
    );
    selected = fill.selected;
    usedIds = fill.usedIds;
    // 事前加算分を差し引いて実際の累積ステータスを得る
    accVo = fill.accVo - (requiredRentalCard?.raw_vo ?? 0);
    accDa = fill.accDa - (requiredRentalCard?.raw_da ?? 0);
    accVi = fill.accVi - (requiredRentalCard?.raw_vi ?? 0);
  }

  // レンタル1枠: 全カードプールから4凸で最良の1枚を選択
  if (rentalPool != null && selected.length < 6) {
    if (requiredRentalCard != null) {
      // 必須カードがレンタル枠を使用 → Pattern A/B をスキップ
      selected.push(requiredRentalCard);
      usedIds.add(requiredRentalCard.card.id);
      accVo += requiredRentalCard.raw_vo * accVoMul;
      accDa += requiredRentalCard.raw_da * accDaMul;
      accVi += requiredRentalCard.raw_vi * accViMul;
    } else {
    const rentalUncap: Record<string, number> = {};
    for (const c of rentalPool) {
      rentalUncap[c.id] = 4;
    }

    // レンタル候補: 所持で選ばれたカードも含めて全カードから計算
    const filteredRentalPool =
      planType != null && planType !== ''
        ? rentalPool.filter(
            (c) =>
              c.plan == null ||
              c.plan === '' ||
              c.plan === planType ||
              c.plan === 'free',
          )
        : rentalPool;

    // ユーザが4凸所持のカードはレンタル枠に置いても upgrade 恩恵がゼロ
    // (owned 4凸 = rental 4凸 で同値)。レンタル枠は本来「未所持/低凸カードを4凸として
    // 借りる」用途なので、4凸所持カードを意図的に rental に置くのは枠の浪費。→ 除外。
    // ただし全候補が4凸所持で空になる場合はフォールバックで除外しない。
    const isUserOwned4Star = (cardId: string): boolean =>
      (uncapLevels?.[cardId] ?? 0) >= 4;
    const rentalPoolForCandidates = (() => {
      const filtered = filteredRentalPool.filter((c) => !isUserOwned4Star(c.id));
      return filtered.length > 0 ? filtered : filteredRentalPool;
    })();

    const allRentalContributions = new Map<string, CardScore>();
    for (const card of rentalPoolForCandidates) {
      const cs = calculateCardContribution(
        card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        rentalUncap,
        triggerBonusInfo,
      );
      allRentalContributions.set(cs.card.id, cs);
    }

    // パターンA: 従来通り、未使用カードからレンタルを選択
    const unusedRentalCandidates = [...allRentalContributions.values()].filter(
      (cs) => !usedIds.has(cs.card.id),
    );
    const defaultRental = selectBestCard(
      unusedRentalCandidates,
      usedIds,
      accVo,
      accDa,
      accVi,
      statCap,
      character,
      overflowPenalty,
    );
    const defaultTotal = calculateCappedTotal(
      baseStats,
      selected,
      defaultRental,
      statCap,
    );

    // 最良の結果を追跡
    let bestOverallTotal = defaultTotal;
    let bestOverallRental: CardScore | undefined = defaultRental;
    let bestOverallSelected: CardScore[] | undefined = undefined;

    // パターンB: 所持カードXをレンタルX(4凸)に昇格し、空いた所持枠に代替カードを入れる
    for (const ownedCard of selected) {
      if (ownedCard.is_required) continue;

      const rentalVersion = allRentalContributions.get(ownedCard.card.id);
      if (rentalVersion == null) continue;

      const rentalGain =
        rentalVersion.raw_vo + rentalVersion.raw_da + rentalVersion.raw_vi;
      const ownedGain =
        ownedCard.raw_vo + ownedCard.raw_da + ownedCard.raw_vi;
      if (rentalGain <= ownedGain) continue;

      const swapAccVo = accVo - ownedCard.raw_vo * accVoMul;
      const swapAccDa = accDa - ownedCard.raw_da * accDaMul;
      const swapAccVi = accVi - ownedCard.raw_vi * accViMul;

      const swapUsedIds = new Set<string>(usedIds);
      const replacementCandidates = cardContributions.filter(
        (cs) => !swapUsedIds.has(cs.card.id),
      );
      const replacement = selectBestCard(
        replacementCandidates,
        swapUsedIds,
        swapAccVo,
        swapAccDa,
        swapAccVi,
        statCap,
        character,
        overflowPenalty,
      );

      if (replacement == null) continue;

      const swapSelected = selected
        .filter((s) => s.card.id !== ownedCard.card.id)
        .concat([replacement]);
      const swapTotal = calculateCappedTotal(
        baseStats,
        swapSelected,
        rentalVersion,
        statCap,
      );

      if (swapTotal > bestOverallTotal) {
        bestOverallTotal = swapTotal;
        bestOverallRental = rentalVersion;
        bestOverallSelected = swapSelected;
      }
    }

    // パターンC: 各レンタル候補に対して所持カードを最適に再選択
    // レンタルのステータスを事前加算し、補完的な所持カードが選ばれるようにする
    for (const rentalCandidate of allRentalContributions.values()) {
      // 必須カードのみスキップ（SP保護カードは許可）
      const existingOwned = checkpointSelected.find(
        (cs) => cs.card.id === rentalCandidate.card.id,
      );
      if (existingOwned?.is_required) continue;

      // チェックポイントに含まれるカード（SP保護等）→除外してスロット復元
      let localSelected = checkpointSelected;
      let localAccVo = checkpointAccVo;
      let localAccDa = checkpointAccDa;
      let localAccVi = checkpointAccVi;
      let localRemainingSlots = checkpointRemainingSlots;
      let localRemainingFree = checkpointRemainingFree;

      if (existingOwned != null) {
        localSelected = checkpointSelected.filter(
          (cs) => cs.card.id !== rentalCandidate.card.id,
        );
        localAccVo -= existingOwned.raw_vo;
        localAccDa -= existingOwned.raw_da;
        localAccVi -= existingOwned.raw_vi;
        localRemainingSlots = { ...checkpointRemainingSlots };
        if (existingOwned.card.id in spCardSlotStat) {
          localRemainingSlots[spCardSlotStat[existingOwned.card.id]]++;
        } else if (spCardUsedFree.has(existingOwned.card.id)) {
          localRemainingFree++;
        }
      }

      const excludedUsedIds = new Set(checkpointUsedIds);
      excludedUsedIds.add(rentalCandidate.card.id);

      const candidateFill = greedyFillOwned(
        cardContributions,
        localSelected,
        excludedUsedIds,
        localAccVo + rentalCandidate.raw_vo * accVoMul,
        localAccDa + rentalCandidate.raw_da * accDaMul,
        localAccVi + rentalCandidate.raw_vi * accViMul,
        localRemainingSlots,
        localRemainingFree,
        ownedSlots,
        statCap,
        character,
        overflowPenalty,
      );

      const candidateTotal = calculateCappedTotal(
        baseStats,
        candidateFill.selected,
        rentalCandidate,
        statCap,
      );

      if (candidateTotal > bestOverallTotal) {
        bestOverallTotal = candidateTotal;
        bestOverallRental = rentalCandidate;
        bestOverallSelected = candidateFill.selected;
      }
    }

    // 最良の結果を適用
    if (bestOverallSelected != null) {
      selected = bestOverallSelected;
      usedIds = new Set(selected.map((s) => s.card.id));
      // accumulator はキャラ補正込みのスケールで再構築
      accVo = baseStats.vo;
      accDa = baseStats.da;
      accVi = baseStats.vi;
      if (character != null) {
        accVo += character.base_status_bonus.vo + lessonStatTotals.vo * (character.para_bonus.vo / 100);
        accDa += character.base_status_bonus.da + lessonStatTotals.da * (character.para_bonus.da / 100);
        accVi += character.base_status_bonus.vi + lessonStatTotals.vi * (character.para_bonus.vi / 100);
      }
      for (const s of selected) {
        accVo += s.raw_vo * accVoMul;
        accDa += s.raw_da * accDaMul;
        accVi += s.raw_vi * accViMul;
      }
    }

    let finalRental: CardScore | undefined = bestOverallRental;
    if (finalRental != null) {
      finalRental = { ...finalRental, is_rental: true };
      selected.push(finalRental);
      usedIds.add(finalRental.card.id);
      accVo += finalRental.raw_vo * accVoMul;
      accDa += finalRental.raw_da * accDaMul;
      accVi += finalRental.raw_vi * accViMul;
    }
    } // end else (requiredRentalCard == null)
  }

  // レンタルなしで6枠未満なら全カードから補充
  if (rentalPool == null && selected.length < 6) {
    const fallback = allContributions.filter(
      (cs) => !usedIds.has(cs.card.id),
    );

    while (selected.length < 6) {
      const best = selectBestCard(
        fallback,
        usedIds,
        accVo,
        accDa,
        accVi,
        statCap,
        character,
        overflowPenalty,
      );
      if (best == null) break;

      selected.push(best);
      usedIds.add(best.card.id);
      accVo += best.raw_vo * accVoMul;
      accDa += best.raw_da * accDaMul;
      accVi += best.raw_vi * accViMul;
    }
  }

  // レンタル含む deck 確定後、SP カードが spCounts 設定を超過しているなら
  // 余剰分の保護を外す → postOptimize で非SPカードへの差し替えを許可する。
  // (step 1 でレンタルが SP かどうかは未確定のため、ここで補正)
  unprotectExcessSpCards(selected, protectedIds, spCounts);

  // ポスト最適化: 実際の計算結果を使ってカードスワップを試行
  // (常時実行: trigger_count_bonus のような synergy 効果を greedy 単独では拾えないため)
  postOptimize(
    selected,
    cardContributions,
    protectedIds,
    plan,
    mainStats,
    uncapLevels,
    additionalCounts,
    statCap,
    character ?? null,
    memoryBonuses ?? null,
    cardTypeSlots,
    turnChoicesOverride,
    overflowPenalty,
  );

  // レンタル枠の再最適化: postOptimize は is_rental を絶対スワップしないため、
  // 所持カードが入れ替わった後にレンタル枠が最適でなくなるケースを実計算で補正する。
  optimizeRentalCard(
    selected,
    rentalPool,
    planType,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    protectedIds,
    spCounts,
    plan,
    additionalCounts,
    statCap,
    character ?? null,
    memoryBonuses ?? null,
    cardTypeSlots,
    turnChoicesOverride ?? buildTurnChoices(plan, mainStats),
    overflowPenalty,
  );

  // SP枚数の強制保証: postOptimize 後、SP カードが要求枚数に満たない場合は
  // プール内の余剰 SP カードで補充する (優先順位 必須カード > SP枚数 > 編成パターン)。
  // postOptimize は total を最大化するため非SPカードを優先しうるので、必ずこの後に実行する。
  enforceSpCounts(
    selected,
    cardContributions,
    rentalPool,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
    triggerBonusInfo,
    protectedIds,
    spCounts,
  );

  // デッキ確定後の breakdown 再計算: producer の trigger_count_bonus を deck-aware に反映
  // - producer: trigger_count_bonus を raw_* に加算しない (consumer 側が adjustedCounts 経由で実発火数を加算するため)
  // - consumer: triggerCounts[target] が producer の bonus 分増加 → flat 効果が正しい回数で発火
  recomputeBreakdownsDeckAware(
    selected,
    triggerCounts,
    lessonAllocation,
    lessonStatTotals,
    uncapLevels,
  );

  // キャップ適用後の実効値でTotalValueを再計算
  recalculateWithCap(selected, baseStats, statCap);

  selected.sort((a, b) => b.total_value - a.total_value);

  const totalValue = selected.reduce((sum, c) => sum + c.total_value, 0);

  return {
    label: generateLabel(cardTypeSlots, freeSlots),
    selected_cards: selected,
    total_value: totalValue,
  };
}

// --- Select multiple patterns ---

export function selectMultiplePatterns(
  plan: TrainingPlan,
  allCards: SupportCard[],
  mainStats: string[],
  subStat: string,
  totalLessonWeeks: number,
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  requiredCardIds?: string[],
): DeckResult[] {
  const results: DeckResult[] = [];

  if (mainStats.length < 2) return results;

  const main1 = mainStats[0];
  const main2 = mainStats[1];

  // SP率カードの必要枚数を属性別に集計
  const spMain1 = spCounts?.[main1] ?? 0;
  const spMain2 = spCounts?.[main2] ?? 0;
  // spSub is available for future use
  void (spCounts?.[subStat] ?? 0);

  // カード枚数パターン (メイン1:メイン2:フリー枠 = 合計6枚)
  const patterns: [number, number, number][] = [
    [3, 2, 1],
    [2, 3, 1],
    [3, 3, 0],
    [2, 2, 2],
    [0, 0, 5], // フリー5 + サブ1 (サブはcardTypeSlotsで指定)
  ];

  for (const [m1, m2, free] of patterns) {
    // レンタルモード(所持5+レンタル1)では、フリー枠なし6枚パターンは
    // 属性枠が所持枠(5)を超えるため [3,2,1] / [2,3,1] と重複する → スキップ
    if (rentalPool != null && free === 0 && m1 + m2 > 5) continue;

    // SP枚数を満たせないパターンはスキップ (フリー枠でSP率カードを吸収できる場合はOK)
    const spShortage =
      Math.max(0, spMain1 - m1) + Math.max(0, spMain2 - m2);
    if (spShortage > free) continue;

    // カード枚数
    const cardTypeSlots: Record<string, number> = {};
    if (m1 > 0) cardTypeSlots[main1] = m1;
    if (m2 > 0) cardTypeSlots[main2] = m2;
    let freeSlots = free;

    // フリー5パターン: サブ属性1枚を固定枠に追加
    if (m1 === 0 && m2 === 0) {
      cardTypeSlots[subStat] = 1;
      freeSlots = 5;
    }

    // レッスン配分: メイン1のレッスン回数が多い
    const lessonAllocation: Record<string, number> = {
      [main1]: 0,
      [main2]: 0,
      [subStat]: 0,
    };
    const remaining = totalLessonWeeks;
    lessonAllocation[main1] += remaining - Math.floor(remaining / 2);
    lessonAllocation[main2] += Math.floor(remaining / 2);

    const result = selectOptimalDeck(
      plan,
      allCards,
      lessonAllocation,
      cardTypeSlots,
      mainStats,
      spCounts,
      planType,
      additionalCounts,
      uncapLevels,
      rentalPool,
      freeSlots,
      requiredCardIds,
    );
    results.push(result);
  }

  return results;
}

// --- HIF mode: 属性別3枚パターン + オールフリー ---

/**
 * HIFモード専用のパターン選出。メイン/サブの概念を捨て、
 * Vo/Da/Vi 各属性で「3枚 + フリー2」と「オールフリー」の合計4パターンを生成する。
 *
 * lessonAllocation はユーザが選んだスケジュールから集計した実際のレッスン回数を渡す。
 */
export function selectMultiplePatternsHif(
  plan: TrainingPlan,
  allCards: SupportCard[],
  mainStats: string[],
  lessonAllocation: Record<string, number>,
  spCounts?: Record<string, number>,
  planType?: string,
  additionalCounts?: AdditionalCounts,
  uncapLevels?: Record<string, number>,
  rentalPool?: SupportCard[],
  requiredCardIds?: string[],
  character?: Character | null,
  memoryBonuses?: MemoryBonus[] | null,
  turnChoicesOverride?: TurnChoice[],
  overflowPenalty?: OverflowPenaltyConfig,
): DeckResult[] {
  const results: DeckResult[] = [];

  // HIFパターン: 属性別2枚+フリー3 と オールフリー
  const patterns: Array<{ stat: 'vo' | 'da' | 'vi' | null; count: number; free: number }> = [
    { stat: 'vo', count: 2, free: 3 },
    { stat: 'da', count: 2, free: 3 },
    { stat: 'vi', count: 2, free: 3 },
    { stat: null, count: 0, free: 5 }, // オールフリー
  ];

  for (const p of patterns) {
    const cardTypeSlots: Record<string, number> = {};
    if (p.stat != null && p.count > 0) {
      cardTypeSlots[p.stat] = p.count;
    }

    // SP率必要枚数の検査: フリー枠でも吸収できるかチェック
    let spShortage = 0;
    for (const stat of ['vo', 'da', 'vi'] as const) {
      const required = spCounts?.[stat] ?? 0;
      const provided = cardTypeSlots[stat] ?? 0;
      spShortage += Math.max(0, required - provided);
    }
    if (spShortage > p.free) continue;

    const result = selectOptimalDeck(
      plan,
      allCards,
      lessonAllocation,
      cardTypeSlots,
      mainStats,
      spCounts,
      planType,
      additionalCounts,
      uncapLevels,
      rentalPool,
      p.free,
      requiredCardIds,
      character ?? null,
      memoryBonuses ?? null,
      turnChoicesOverride,
      overflowPenalty,
    );
    results.push(result);
  }

  return results;
}
