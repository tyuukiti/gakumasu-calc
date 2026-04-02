export type StatusType = 'vo' | 'da' | 'vi';

export type ActionType =
  | 'vo_lesson' | 'da_lesson' | 'vi_lesson'
  | 'vo_class' | 'da_class' | 'vi_class'
  | 'outing' | 'rest' | 'consultation'
  | 'activity_supply' | 'special_training';

export type WeekType = 'free' | 'fixed_event' | 'exam' | 'audition';

export type CardRarity = 'R' | 'SR' | 'SSR';

export type CardType = 'vo' | 'da' | 'vi' | 'all';

export type ValueType = 'flat' | 'sp_rate' | 'para_bonus' | 'percent';

export type RoleType = 'メイン1' | 'メイン2' | 'サブ';

export type PlanType = 'sense' | 'logic' | 'anomaly';

export type CardTag = 'skill' | 'produce_item' | 'exam_item' | 'none';

export const ACTION_TYPE_DISPLAY: Record<ActionType, string> = {
  vo_lesson: 'Voレッスン',
  da_lesson: 'Daレッスン',
  vi_lesson: 'Viレッスン',
  vo_class: 'Vo授業',
  da_class: 'Da授業',
  vi_class: 'Vi授業',
  outing: 'お出かけ',
  rest: '休憩',
  consultation: '相談',
  activity_supply: '活動支給',
  special_training: '特別指導',
};
