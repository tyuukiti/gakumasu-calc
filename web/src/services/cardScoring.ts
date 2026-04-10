import type {
  SupportCard,
  CardEffect,
  TrainingPlan,
  WeekSchedule,
  StatusValues,
  AdditionalCounts,
  TurnChoice,
} from '../types/models';
import type { ActionType } from '../types/enums';
import { additionalCountsToRecord } from '../types/models';
import type { CardScore, EffectBreakdown, DeckResult } from '../types/results';
import { sv } from '../utils/statusValues';
import { getUncapLevel, getEffectValue, calculate } from './statusCalculation';
import { DEFAULT_STAT_CAP } from '../utils/constants';

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
    skill_ssr_acquire: 'スキル(SSR)獲得',
    skill_enhance: 'スキル強化',
    skill_delete: 'スキル削除',
    skill_custom: 'スキルカスタム',
    skill_change: 'スキルチェンジ',
    active_enhance: 'アクティブ強化',
    active_delete: 'アクティブ削除',
    mental_acquire: 'メンタル獲得',
    mental_enhance: 'メンタル強化',
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
): string {
  const prefix = effect.source === 'item' ? '[アイテム] ' : '';
  const triggerName = triggerDisplayName(effect.trigger);
  const stat = effect.stat.toUpperCase();
  const val = getEffectValue(effect, uncapLevel);

  if (effect.trigger === 'equip') {
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
): number {
  const val = getEffectValue(effect, uncapLevel);
  if (effect.trigger === 'equip') return val;

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

  for (const week of plan.schedule) {
    if (isFixedEvent(week)) {
      counts['exam_end'] = (counts['exam_end'] ?? 0) + 1;
      continue;
    }

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

  return sv(vo, da, vi);
}

// --- Calculate card contribution ---

export function calculateCardContribution(
  card: SupportCard,
  triggerCounts: Record<string, number>,
  _lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels?: Record<string, number>,
): CardScore {
  const uncap = getUncapLevel(card, uncapLevels);
  let vo = 0,
    da = 0,
    vi = 0;
  const breakdowns: EffectBreakdown[] = [];

  for (const effect of card.effects) {
    // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
    if (effect.value_type === 'sp_rate') continue;

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
        ? calculateFlatValue(effect, triggerCounts, uncap)
        : 0;

    if (Math.abs(value) < 0.01) continue;

    // 内訳の理由テキスト生成
    const reason2 = buildReasonText(effect, triggerCounts, uncap);

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

  // 属性枠
  const sortedSlots = Object.entries(remainingSlotsInit).sort(
    (a, b) => b[1] - a[1],
  );
  for (const [type, count] of sortedSlots) {
    if (count <= 0) continue;
    const candidates = contributions.filter(
      (cs) =>
        (cs.card.type === type || cs.card.type === 'all') &&
        !used.has(cs.card.id),
    );
    for (let i = 0; i < count && sel.length < ownedSlots; i++) {
      const best = selectBestCard(candidates, used, aVo, aDa, aVi, statCap);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo;
      aDa += best.raw_da;
      aVi += best.raw_vi;
    }
  }

  // フリー枠
  for (let i = 0; i < remainingFreeInit && sel.length < ownedSlots; i++) {
    const freeCandidates = contributions.filter(
      (cs) => !used.has(cs.card.id),
    );
    const best = selectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap);
    if (best == null) break;
    sel.push(best);
    used.add(best.card.id);
    aVo += best.raw_vo;
    aDa += best.raw_da;
    aVi += best.raw_vi;
  }

  // 補充
  if (sel.length < ownedSlots) {
    const remaining = contributions.filter((cs) => !used.has(cs.card.id));
    while (sel.length < ownedSlots) {
      const best = selectBestCard(remaining, used, aVo, aDa, aVi, statCap);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo;
      aDa += best.raw_da;
      aVi += best.raw_vi;
    }
  }

  return { selected: sel, usedIds: used, accVo: aVo, accDa: aDa, accVi: aVi };
}

// --- Post-optimization using actual calculation ---

function postOptimize(
  selected: CardScore[],
  candidates: CardScore[],
  protectedIds: Set<string>,
  plan: TrainingPlan,
  mainStats: string[],
  uncapLevels?: Record<string, number>,
  additionalCounts?: AdditionalCounts,
): void {
  const turnChoices = buildTurnChoices(plan, mainStats);

  function evaluate(cards: SupportCard[]): number {
    const uc: Record<string, number> = { ...(uncapLevels ?? {}) };
    for (const cs of selected) {
      if (cs.is_rental) uc[cs.card.id] = 4;
    }
    const fs = calculate(plan, cards, turnChoices, uc, additionalCounts)
      .final_status;
    return fs.vo + fs.da + fs.vi;
  }

  let improved: boolean;
  do {
    improved = false;
    const currentCards = selected.map((c) => c.card);
    let currentTotal = evaluate(currentCards);

    for (let si = 0; si < selected.length; si++) {
      const ownedCard = selected[si];
      if (ownedCard.is_rental) continue;
      const hasSpRate = (card: SupportCard) =>
        card.effects.some((e) => e.trigger === 'equip' && e.value_type === 'sp_rate');
      const ownedIsProtectedSp =
        protectedIds.has(ownedCard.card.id) && hasSpRate(ownedCard.card);
      const ownedIsProtectedNonSp =
        protectedIds.has(ownedCard.card.id) && !ownedIsProtectedSp;
      // 必須カードなど非SPの保護カードはスキップ
      if (ownedIsProtectedNonSp) continue;

      for (const candidate of candidates) {
        if (selected.some((c) => c.card.id === candidate.card.id)) continue;
        // タイプ分布を維持: 同じタイプ同士、または all タイプとの交換のみ許可
        if (
          candidate.card.type !== ownedCard.card.type &&
          candidate.card.type !== 'all' &&
          ownedCard.card.type !== 'all'
        )
          continue;
        // SP率で保護されたカードは、SP率持ちの候補とのみ交換可能
        if (ownedIsProtectedSp && !hasSpRate(candidate.card)) continue;

        const testCards = [...currentCards];
        testCards[si] = candidate.card;

        const testTotal = evaluate(testCards);
        if (testTotal > currentTotal) {
          selected[si] = candidate;
          // SP率保護を新カードに引き継ぐ
          if (ownedIsProtectedSp) {
            protectedIds.delete(ownedCard.card.id);
            protectedIds.add(candidate.card.id);
          }
          currentTotal = testTotal;
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
): CardScore | undefined {
  let best: CardScore | undefined = undefined;
  let bestGain = -Infinity;

  for (const cs of candidates) {
    if (usedIds.has(cs.card.id)) continue;

    // キャップ適用後の実効増分
    const newVo = Math.min(currentVo + cs.raw_vo, statCap);
    const newDa = Math.min(currentDa + cs.raw_da, statCap);
    const newVi = Math.min(currentVi + cs.raw_vi, statCap);

    const cappedVo = Math.min(currentVo, statCap);
    const cappedDa = Math.min(currentDa, statCap);
    const cappedVi = Math.min(currentVi, statCap);

    const gain = newVo - cappedVo + (newDa - cappedDa) + (newVi - cappedVi);

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
): DeckResult {
  const statCap = plan.status_limit;
  const triggerCounts = countTriggers(plan, lessonAllocation, mainStats);

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

  // 全カードの属性別寄与を事前計算
  const cardContributions = eligible.map((card) =>
    calculateCardContribution(
      card,
      triggerCounts,
      lessonAllocation,
      lessonStatTotals,
      uncapLevels,
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
    ),
  );

  // 属性枠ごとに選択 (上限考慮)
  let selected: CardScore[] = [];
  let usedIds = new Set<string>();

  // 現在の累積ステータス (ベース + 選択済みカード)
  let accVo = baseStats.vo,
    accDa = baseStats.da,
    accVi = baseStats.vi;

  // 属性枠・フリー枠の残数を管理するローカルコピー
  const remainingSlots: Record<string, number> = { ...cardTypeSlots };
  let remainingFree = freeSlots;

  // ステップ0: 必須カードを強制挿入
  let requiredRentalCard: CardScore | undefined = undefined;
  const protectedIds = new Set<string>();

  if (requiredCardIds != null && requiredCardIds.length > 0) {
    // spCounts のローカルコピー（必須カードでSP率を消費するため）
    const spCountsCopy: Record<string, number> = spCounts != null ? { ...spCounts } : {};

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
        accVo += contribution.raw_vo;
        accDa += contribution.raw_da;
        accVi += contribution.raw_vi;

        // スロット消費
        if (card.type !== 'all' && card.type in remainingSlots && remainingSlots[card.type] > 0) {
          remainingSlots[card.type]--;
        } else if (card.type === 'all') {
          // "all" タイプ: 最大残数の属性枠を消費
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
          for (const key of Object.keys(spCountsCopy)) {
            if ((card.type === key || card.type === 'all') && spCountsCopy[key] > 0) {
              spCountsCopy[key]--;
              break;
            }
          }
        }
      }
    }

    // spCounts のローカル参照を更新（元のオブジェクトは変更しない）
    spCounts = spCountsCopy;
  }

  // ステップ1: SP率カードをユーザ指定枚数分、先に確保
  if (spCounts != null) {
    for (const [stat, need] of Object.entries(spCounts)) {
      if (need <= 0) continue;

      // この属性のSP率を持つカードをステータス寄与順で選ぶ
      const spCandidates = cardContributions.filter(
        (cs) =>
          (cs.card.type === stat || cs.card.type === 'all') &&
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
        );
        if (best == null) break;

        selected.push(best);
        usedIds.add(best.card.id);
        protectedIds.add(best.card.id); // SP率カードはポスト最適化でスワップしない
        accVo += best.raw_vo;
        accDa += best.raw_da;
        accVi += best.raw_vi;

        // SP率カードが属性枠にカウントされるか、フリー枠を消費するか判定
        if (stat in remainingSlots && remainingSlots[stat] > 0) {
          remainingSlots[stat]--;
        } else {
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
      accVo += requiredRentalCard.raw_vo;
      accDa += requiredRentalCard.raw_da;
      accVi += requiredRentalCard.raw_vi;
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

    const allRentalContributions = new Map<string, CardScore>();
    for (const card of filteredRentalPool) {
      const cs = calculateCardContribution(
        card,
        triggerCounts,
        lessonAllocation,
        lessonStatTotals,
        rentalUncap,
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
      if (protectedIds.has(ownedCard.card.id)) continue;

      const rentalVersion = allRentalContributions.get(ownedCard.card.id);
      if (rentalVersion == null) continue;

      const rentalGain =
        rentalVersion.raw_vo + rentalVersion.raw_da + rentalVersion.raw_vi;
      const ownedGain =
        ownedCard.raw_vo + ownedCard.raw_da + ownedCard.raw_vi;
      if (rentalGain <= ownedGain) continue;

      const swapAccVo = accVo - ownedCard.raw_vo;
      const swapAccDa = accDa - ownedCard.raw_da;
      const swapAccVi = accVi - ownedCard.raw_vi;

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
      if (protectedIds.has(rentalCandidate.card.id)) continue;

      const excludedUsedIds = new Set(checkpointUsedIds);
      excludedUsedIds.add(rentalCandidate.card.id);

      const candidateFill = greedyFillOwned(
        cardContributions,
        checkpointSelected,
        excludedUsedIds,
        checkpointAccVo + rentalCandidate.raw_vo,
        checkpointAccDa + rentalCandidate.raw_da,
        checkpointAccVi + rentalCandidate.raw_vi,
        checkpointRemainingSlots,
        checkpointRemainingFree,
        ownedSlots,
        statCap,
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
      accVo = baseStats.vo;
      accDa = baseStats.da;
      accVi = baseStats.vi;
      for (const s of selected) {
        accVo += s.raw_vo;
        accDa += s.raw_da;
        accVi += s.raw_vi;
      }
    }

    let finalRental: CardScore | undefined = bestOverallRental;
    if (finalRental != null) {
      finalRental = { ...finalRental, is_rental: true };
      selected.push(finalRental);
      usedIds.add(finalRental.card.id);
      accVo += finalRental.raw_vo;
      accDa += finalRental.raw_da;
      accVi += finalRental.raw_vi;
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
      );
      if (best == null) break;

      selected.push(best);
      usedIds.add(best.card.id);
      accVo += best.raw_vo;
      accDa += best.raw_da;
      accVi += best.raw_vi;
    }
  }

  // ポスト最適化: 実際の計算結果を使ってカードスワップを試行
  if (rentalPool != null) {
    postOptimize(
      selected,
      cardContributions,
      protectedIds,
      plan,
      mainStats,
      uncapLevels,
      additionalCounts,
    );
  }

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
