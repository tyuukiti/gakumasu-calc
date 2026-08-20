import type { SupportCard, CardEffect, WeekSchedule } from '../../types/models';
import { getEffectValue, getEventParamBoostPercent } from '../statusCalculation';

// --- Helper: WeekSchedule utilities ---

export function isFixedEvent(w: WeekSchedule): boolean {
  return w.type === 'fixed_event' || w.type === 'exam' || w.type === 'audition';
}

export function getLesson(w: WeekSchedule, lessonType: string) {
  return w.lessons.find((l) => l.type === lessonType) ?? undefined;
}

// --- Trigger display name ---

export function triggerDisplayName(trigger: string): string {
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
    rest: '休む',
    vo_sp_end: 'VoSP終了',
    da_sp_end: 'DaSP終了',
    vi_sp_end: 'ViSP終了',
    vo_lesson_end: 'Voレッスン終了',
    da_lesson_end: 'Daレッスン終了',
    vi_lesson_end: 'Viレッスン終了',
    vo_normal_end: 'Vo通常終了',
    da_normal_end: 'Da通常終了',
    vi_normal_end: 'Vi通常終了',
  };
  return map[trigger] ?? trigger;
}

// --- Build reason text ---

export function buildReasonText(
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

export function calculateFlatValue(
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
