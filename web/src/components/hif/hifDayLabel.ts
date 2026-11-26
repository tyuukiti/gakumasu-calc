/**
 * HIF の週番号から表示ラベルを構築。選抜試験は「Day X」、本戦は「本戦N日目 / 本戦R1 / 本戦インターバル / 本戦R2」。
 * ScheduleConfig (日程表) と HifPouchConfig (ポーチのプレビュー) で共用する。
 */
export function hifDayLabel(week: number): string {
  if (week <= 20) return `Day ${week}`;
  if (week === 27) return '本戦R1';
  if (week === 29) return '本戦R2';
  if (week === 28) return '本戦インターバル';
  return `本戦${week - 20}日目`;
}
