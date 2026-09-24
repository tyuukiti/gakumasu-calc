export type { OverflowPenaltyConfig } from './types';
export type { TriggerBonusContributor, TriggerBonusEntry } from './contribution';
export {
  countTriggers,
  estimateBaseStats,
  calculateLessonStatTotals,
  calculateCardContribution,
  computeTriggerBonusInfo,
} from './contribution';
export {
  buildTurnChoices,
  buildAbilitySummary,
  generateLabel,
  recomputeBreakdownsDeckAware,
  recalculateWithCap,
} from './results';
export { selectOptimalDeck } from './selection';
export { selectMultiplePatterns, selectMultiplePatternsHif } from './patterns';
