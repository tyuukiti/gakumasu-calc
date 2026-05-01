import type {
  StatusValues,
  SupportCard,
  CardEffect,
  TrainingPlan,
  WeekSchedule,
  TurnChoice,
  LessonConfig,
  AdditionalCounts,
  Character,
} from '../types/models';
import { additionalCountsToRecord } from '../types/models';
import type { CalculationResult, WeekBreakdown } from '../types/results';
import type { ActionType } from '../types/enums';
import { svZero, svAdd, svClone } from '../utils/statusValues';

// ---------------------------------------------------------------------------
// Helper: CardEffect.GetValue(uncapLevel) equivalent
// ---------------------------------------------------------------------------

export function getEffectValue(effect: CardEffect, uncapLevel: number): number {
  if (effect.values.length === 0) return 0;
  const idx = Math.min(
    Math.max(uncapLevel, 0),
    Math.min(4, effect.values.length - 1),
  );
  return effect.values[idx];
}

// ---------------------------------------------------------------------------
// Helper: SupportCard helper methods as standalone functions
// ---------------------------------------------------------------------------

function getInitialBonus(card: SupportCard, uncapLevel: number): StatusValues {
  let vo = 0,
    da = 0,
    vi = 0;
  const boostMul = 1 + getEventParamBoostPercent(card, uncapLevel) / 100;
  for (const e of card.effects) {
    if (e.trigger === 'equip' && e.value_type === 'flat') {
      const raw = getEffectValue(e, uncapLevel);
      const v = Math.floor(e.event_param ? raw * boostMul : raw);
      switch (e.stat) {
        case 'vo': vo += v; break;
        case 'da': da += v; break;
        case 'vi': vi += v; break;
      }
    }
  }
  return { vo, da, vi };
}

export function getEventParamBoostPercent(card: SupportCard, uncapLevel: number): number {
  let total = 0;
  for (const e of card.effects) {
    if (e.trigger === 'equip' && e.value_type === 'event_param_boost') {
      total += getEffectValue(e, uncapLevel);
    }
  }
  return total;
}

function getEffectsByTrigger(card: SupportCard, trigger: string): CardEffect[] {
  return card.effects.filter((e) => e.trigger === trigger);
}

// ---------------------------------------------------------------------------
// Exported: uncap level resolution
// ---------------------------------------------------------------------------

export function getUncapLevel(
  card: SupportCard,
  uncapLevels: Record<string, number> | undefined,
): number {
  if (uncapLevels != null && card.id in uncapLevels) {
    return uncapLevels[card.id];
  }
  return 4; // default 4 uncap
}

// ---------------------------------------------------------------------------
// Main calculation
// ---------------------------------------------------------------------------

export function calculate(
  plan: TrainingPlan,
  selectedCards: SupportCard[],
  turnChoices: TurnChoice[],
  uncapLevels?: Record<string, number>,
  additionalCounts?: AdditionalCounts,
  character?: Character | null,
): CalculationResult {
  // Step 1: base status (apply character bonus if selected)
  let baseStatus = svClone(plan.base_status);
  if (character != null) {
    baseStatus = svAdd(baseStatus, character.base_status_bonus);
  }

  // Step 2: support card equip bonus
  const supportBonus = calculateEquipBonus(selectedCards, uncapLevels);

  // Step 3: turn-by-turn calculation
  let accumulated = svZero();
  const weekDetails: WeekBreakdown[] = [];
  const triggerCounters: Record<string, number> = {};

  for (const week of plan.schedule) {
    const turnChoice = turnChoices.find((tc) => tc.week === week.week);
    const weekGain = calculateWeekGain(
      week,
      turnChoice,
      selectedCards,
      plan,
      triggerCounters,
      uncapLevels,
      character,
    );

    accumulated = svAdd(accumulated, weekGain);

    const actionName = getActionName(week, turnChoice);
    weekDetails.push({
      week: week.week,
      action_name: actionName,
      gain: weekGain,
    });
  }

  // Step 3.5: additional trigger fires (from event count templates)
  const additionalGain = fireAdditionalTriggers(
    selectedCards,
    triggerCounters,
    uncapLevels,
    additionalCounts,
  );
  if (additionalGain.vo !== 0 || additionalGain.da !== 0 || additionalGain.vi !== 0) {
    accumulated = svAdd(accumulated, additionalGain);
    weekDetails.push({
      week: 99,
      action_name: '追加イベント効果',
      gain: additionalGain,
    });
  }

  // Step 4: final values
  const finalStatus = svAdd(svAdd(baseStatus, supportBonus), accumulated);

  return {
    final_status: finalStatus,
    base_status: baseStatus,
    support_card_bonus: supportBonus,
    accumulated_gain: accumulated,
    week_details: weekDetails,
  };
}

// ---------------------------------------------------------------------------
// Private helpers
// ---------------------------------------------------------------------------

function calculateEquipBonus(
  cards: SupportCard[],
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  let bonus = svZero();
  for (const card of cards) {
    bonus = svAdd(bonus, getInitialBonus(card, getUncapLevel(card, uncapLevels)));
  }
  return bonus;
}

function calculateWeekGain(
  week: WeekSchedule,
  turnChoice: TurnChoice | undefined,
  cards: SupportCard[],
  plan: TrainingPlan,
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
  character: Character | null | undefined,
): StatusValues {
  // fixed event
  const isFixedEvent =
    week.type === 'fixed_event' || week.type === 'exam' || week.type === 'audition';

  if (isFixedEvent) {
    const fixedGain = week.status_gain ? svClone(week.status_gain) : svZero();
    const examTriggerGain = fireTrigger('exam_end', cards, triggerCounters, uncapLevels);
    return svAdd(fixedGain, examTriggerGain);
  }

  if (turnChoice == null) {
    return svZero();
  }

  switch (turnChoice.chosen_action as ActionType) {
    case 'vo_lesson':
      return calculateLessonGain(week, 'vo', cards, triggerCounters, uncapLevels, character);
    case 'da_lesson':
      return calculateLessonGain(week, 'da', cards, triggerCounters, uncapLevels, character);
    case 'vi_lesson':
      return calculateLessonGain(week, 'vi', cards, triggerCounters, uncapLevels, character);
    case 'vo_class':
      return calculateClassGain(week, 'vo', cards, triggerCounters, uncapLevels);
    case 'da_class':
      return calculateClassGain(week, 'da', cards, triggerCounters, uncapLevels);
    case 'vi_class':
      return calculateClassGain(week, 'vi', cards, triggerCounters, uncapLevels);
    case 'outing':
      return calculateOutingGain(week, cards, triggerCounters, uncapLevels);
    case 'consultation':
      return calculateConsultationGain(week, cards, triggerCounters, uncapLevels);
    case 'rest':
      return svZero();
    case 'activity_supply':
      return calculateSupplyGain(turnChoice, plan, cards, triggerCounters, uncapLevels);
    case 'special_training':
      return calculateSpecialTrainingGain(week, cards, triggerCounters, uncapLevels);
    default:
      return svZero();
  }
}

function calculateLessonGain(
  week: WeekSchedule,
  lessonType: string,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
  character: Character | null | undefined,
): StatusValues {
  const lesson = getLesson(week, lessonType);
  if (lesson == null) {
    return svZero();
  }

  const raw = lesson.sp_bonus;

  // Collect para_bonus% per stat
  let paraBonusVo = 0,
    paraBonusDa = 0,
    paraBonusVi = 0;

  for (const card of cards) {
    const uncap = getUncapLevel(card, uncapLevels);
    for (const e of card.effects) {
      if (e.trigger === 'equip' && e.value_type === 'para_bonus') {
        const val = getEffectValue(e, uncap);
        switch (e.stat) {
          case 'vo':
            paraBonusVo += val;
            break;
          case 'da':
            paraBonusDa += val;
            break;
          case 'vi':
            paraBonusVi += val;
            break;
          case 'all':
            paraBonusVo += val;
            paraBonusDa += val;
            paraBonusVi += val;
            break;
        }
      }
    }
  }

  // Add character para_bonus on the same level as support cards
  if (character != null) {
    paraBonusVo += character.para_bonus.vo;
    paraBonusDa += character.para_bonus.da;
    paraBonusVi += character.para_bonus.vi;
  }

  // Apply para bonus to each stat
  const vo = Math.floor(raw.vo * (1.0 + paraBonusVo / 100.0));
  const da = Math.floor(raw.da * (1.0 + paraBonusDa / 100.0));
  const vi = Math.floor(raw.vi * (1.0 + paraBonusVi / 100.0));

  let result: StatusValues = { vo, da, vi };

  // SP end trigger (generic)
  const spEndGain = fireTrigger('sp_end', cards, triggerCounters, uncapLevels);
  result = svAdd(result, spEndGain);

  // Stat-specific SP end trigger (vo_sp_end, da_sp_end, vi_sp_end)
  const statSpEndGain = fireTrigger(
    `${lessonType}_sp_end`,
    cards,
    triggerCounters,
    uncapLevels,
  );
  result = svAdd(result, statSpEndGain);

  // Lesson end trigger (generic)
  let lessonEndGain = fireTrigger('lesson_end', cards, triggerCounters, uncapLevels);

  // Stat-specific lesson end trigger (vo_lesson_end, da_lesson_end, vi_lesson_end)
  const statLessonEndGain = fireTrigger(
    `${lessonType}_lesson_end`,
    cards,
    triggerCounters,
    uncapLevels,
  );
  lessonEndGain = svAdd(lessonEndGain, statLessonEndGain);
  result = svAdd(result, lessonEndGain);

  return result;
}

function calculateClassGain(
  week: WeekSchedule,
  classType: string,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  const classConfig = getClass(week, classType);
  const baseGain = classConfig
    ? svClone(classConfig.sp_bonus)
    : week.class_effect
      ? svClone(week.class_effect)
      : svZero();

  // Class end trigger
  const classEndGain = fireTrigger('class_end', cards, triggerCounters, uncapLevels);
  return svAdd(baseGain, classEndGain);
}

function calculateOutingGain(
  week: WeekSchedule,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  const baseGain = week.outing_effect ? svClone(week.outing_effect) : svZero();

  // Outing end trigger
  const outingEndGain = fireTrigger('outing_end', cards, triggerCounters, uncapLevels);
  return svAdd(baseGain, outingEndGain);
}

function calculateConsultationGain(
  week: WeekSchedule,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  const baseGain = week.consultation_effect
    ? svClone(week.consultation_effect)
    : svZero();

  // Consultation trigger
  const consultGain = fireTrigger('consultation', cards, triggerCounters, uncapLevels);
  return svAdd(baseGain, consultGain);
}

function calculateSupplyGain(
  _turnChoice: TurnChoice,
  _plan: TrainingPlan,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  // Activity supply itself adds no status (only fires support card triggers)
  const supplyGain = fireTrigger('activity_supply', cards, triggerCounters, uncapLevels);
  return supplyGain;
}

function calculateSpecialTrainingGain(
  week: WeekSchedule,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  const baseGain = week.special_training_effect
    ? svClone(week.special_training_effect)
    : svZero();

  // Special training trigger
  const stGain = fireTrigger('special_training', cards, triggerCounters, uncapLevels);
  return svAdd(baseGain, stGain);
}

/**
 * Fire additional triggers from event count templates.
 * Each trigger in additionalCounts is fired the specified number of times,
 * respecting max_count limits already partially consumed by weekly processing.
 */
function fireAdditionalTriggers(
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
  additionalCounts?: AdditionalCounts,
): StatusValues {
  if (additionalCounts == null) return svZero();

  let gain = svZero();
  const addRec = additionalCountsToRecord(additionalCounts);

  for (const [trigger, fireCount] of Object.entries(addRec)) {
    if (fireCount <= 0) continue;
    for (let i = 0; i < fireCount; i++) {
      const triggerGain = fireTrigger(trigger, cards, triggerCounters, uncapLevels);
      gain = svAdd(gain, triggerGain);
    }
  }

  return gain;
}

/**
 * Fire all card effects matching the specified trigger and return the total
 * status gain. Effects that have exceeded their max_count are skipped.
 */
function fireTrigger(
  trigger: string,
  cards: SupportCard[],
  triggerCounters: Record<string, number>,
  uncapLevels: Record<string, number> | undefined,
): StatusValues {
  let gain = svZero();

  for (const card of cards) {
    const uncap = getUncapLevel(card, uncapLevels);
    for (const effect of getEffectsByTrigger(card, trigger)) {
      if (effect.value_type !== 'flat') continue;

      // Check fire count
      const counterKey = `${card.id}_${trigger}_${effect.stat}`;
      const count = triggerCounters[counterKey] ?? 0;

      if (effect.max_count != null && count >= effect.max_count) {
        continue;
      }

      triggerCounters[counterKey] = count + 1;

      const value = Math.floor(getEffectValue(effect, uncap));
      switch (effect.stat) {
        case 'vo':
          gain = svAdd(gain, { vo: value, da: 0, vi: 0 });
          break;
        case 'da':
          gain = svAdd(gain, { vo: 0, da: value, vi: 0 });
          break;
        case 'vi':
          gain = svAdd(gain, { vo: 0, da: 0, vi: value });
          break;
        case 'all':
          gain = svAdd(gain, { vo: value, da: value, vi: value });
          break;
      }
    }
  }

  return gain;
}

// ---------------------------------------------------------------------------
// WeekSchedule helper functions (ported from C# instance methods)
// ---------------------------------------------------------------------------

function getLesson(week: WeekSchedule, lessonType: string): LessonConfig | undefined {
  return week.lessons.find((l) => l.type === lessonType);
}

function getClass(week: WeekSchedule, classType: string): LessonConfig | undefined {
  return week.classes.find((l) => l.type === classType);
}

// ---------------------------------------------------------------------------
// Action name display
// ---------------------------------------------------------------------------

function getActionName(
  week: WeekSchedule,
  turnChoice: TurnChoice | undefined,
): string {
  const isFixedEvent =
    week.type === 'fixed_event' || week.type === 'exam' || week.type === 'audition';

  if (isFixedEvent) {
    return week.event_name ?? '固定イベント';
  }

  if (turnChoice == null) {
    return '未選択';
  }

  switch (turnChoice.chosen_action as ActionType) {
    case 'vo_lesson':
      return 'Voレッスン (SP)';
    case 'da_lesson':
      return 'Daレッスン (SP)';
    case 'vi_lesson':
      return 'Viレッスン (SP)';
    case 'vo_class':
      return 'Vo授業';
    case 'da_class':
      return 'Da授業';
    case 'vi_class':
      return 'Vi授業';
    case 'outing':
      return 'お出かけ';
    case 'rest':
      return '休憩';
    case 'consultation':
      return '相談';
    case 'activity_supply':
      return '活動支給';
    case 'special_training':
      return '特別指導';
    default:
      return '不明';
  }
}
