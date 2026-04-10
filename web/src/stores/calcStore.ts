import { create } from 'zustand';
import type {
  TrainingPlan,
  TurnChoice,
  AdditionalCounts,
  EventCountTemplate,
  SupportCard,
} from '../types/models';
import { emptyAdditionalCounts } from '../types/models';
import type { PlanType, RoleType, ActionType } from '../types/enums';
import type { CalculationResult, DeckResult } from '../types/results';
import type { CardInventoryEntry } from '../types/inventory';
import { useAppStore } from './appStore';
import { selectMultiplePatterns } from '../services/cardScoring';
import { calculate } from '../services/statusCalculation';
import { trackEvent, startTimer, endTimer, incrementCounter, trackFunnelStep } from '../utils/analytics';

interface CalcState {
  selectedPlanId: string;
  selectedPlanType: PlanType;
  voRole: RoleType;
  daRole: RoleType;
  viRole: RoleType;
  voSpCount: number;
  daSpCount: number;
  viSpCount: number;
  additionalCounts: AdditionalCounts;
  ownedOnly: boolean;
  contestMode: boolean;
  requiredCardIds: string[];
  deckResults: DeckResult[];
  selectedPatternIndex: number;
  calculationResult: CalculationResult | null;
  errorMessage: string | null;

  // internal state for re-applying patterns
  _lastMainStats: string[];
  _lastLessonWeekCount: number;

  setSelectedPlanId: (id: string) => void;
  setSelectedPlanType: (type: PlanType) => void;
  setRole: (stat: 'vo' | 'da' | 'vi', role: RoleType) => void;
  setSpCount: (stat: 'vo' | 'da' | 'vi', count: number) => void;
  setAdditionalCount: (key: string, value: number) => void;
  applyTemplate: (template: EventCountTemplate) => void;
  setOwnedOnly: (v: boolean) => void;
  setContestMode: (v: boolean) => void;
  addRequiredCard: (cardId: string) => void;
  removeRequiredCard: (cardId: string) => void;
  executeCalculate: () => void;
  selectPattern: (index: number) => void;
}

function getCandidateCards(
  allCards: SupportCard[],
  inventory: CardInventoryEntry[],
  ownedOnly: boolean,
  contestMode: boolean,
): SupportCard[] {
  let cards = allCards;
  if (ownedOnly) {
    const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
    cards = cards.filter((c) => ownedIds.has(c.id));
  }
  if (contestMode) {
    cards = cards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item');
  }
  return cards;
}

function buildUncapLevels(
  allCards: SupportCard[],
  inventory: CardInventoryEntry[],
  ownedOnly: boolean,
): Record<string, number> {
  if (ownedOnly) {
    const levels: Record<string, number> = {};
    for (const e of inventory) {
      levels[e.card_id] = e.uncap;
    }
    return levels;
  }
  const levels: Record<string, number> = {};
  for (const c of allCards) {
    levels[c.id] = 4;
  }
  return levels;
}

function autoAssignTurnChoices(
  plan: TrainingPlan,
  mainStats: string[],
): TurnChoice[] {
  const subStat = ['vo', 'da', 'vi'].find((s) => !mainStats.includes(s)) ?? 'vi';
  const subClassAction: ActionType = `${subStat}_class` as ActionType;

  const choices: TurnChoice[] = [];

  // Categorize weeks
  const lessonWeeks: { week: number; available_actions: string[] }[] = [];
  const otherWeeks: { week: number; available_actions: string[]; type: string }[] = [];

  for (const week of plan.schedule) {
    const isFixed = week.type === 'fixed_event' || week.type === 'exam' || week.type === 'audition';
    if (isFixed) {
      // Fixed events don't need a TurnChoice
      continue;
    }

    const hasLesson = week.available_actions.some((a) =>
      a === 'vo_lesson' || a === 'da_lesson' || a === 'vi_lesson',
    );

    if (hasLesson) {
      lessonWeeks.push({ week: week.week, available_actions: week.available_actions });
    } else {
      otherWeeks.push({ week: week.week, available_actions: week.available_actions, type: week.type });
    }
  }

  // Find mid-exam week
  const midExamWeek = plan.schedule.find(
    (w) => (w.type === 'fixed_event' || w.type === 'exam') && w.event_name === '中間試験',
  )?.week ?? 10;

  if (mainStats.length >= 2) {
    const main1Action: ActionType = `${mainStats[0]}_lesson` as ActionType;
    const main2Action: ActionType = `${mainStats[1]}_lesson` as ActionType;

    const sortedLessons = [...lessonWeeks].sort((a, b) => a.week - b.week);
    const beforeMid = sortedLessons.filter((w) => w.week < midExamWeek);
    const afterMid = sortedLessons.filter((w) => w.week > midExamWeek);

    // Before mid: alternate main1 / main2
    let toggle = false;
    for (const w of beforeMid) {
      const action = toggle ? main2Action : main1Action;
      const fallback = toggle ? main1Action : main2Action;
      if (w.available_actions.includes(action)) {
        choices.push({ week: w.week, chosen_action: action });
      } else if (w.available_actions.includes(fallback)) {
        choices.push({ week: w.week, chosen_action: fallback });
      }
      toggle = !toggle;
    }

    // After mid: 2:1 ratio (main1, main2, main1, main1, main2, main1...)
    let afterCount = 0;
    for (const w of afterMid) {
      const action = (afterCount % 3 === 1) ? main2Action : main1Action;
      const fallback = action === main2Action ? main1Action : main2Action;
      if (w.available_actions.includes(action)) {
        choices.push({ week: w.week, chosen_action: action });
      } else if (w.available_actions.includes(fallback)) {
        choices.push({ week: w.week, chosen_action: fallback });
      }
      afterCount++;
    }
  } else if (mainStats.length === 1) {
    const onlyAction: ActionType = `${mainStats[0]}_lesson` as ActionType;
    for (const w of lessonWeeks) {
      if (w.available_actions.includes(onlyAction)) {
        choices.push({ week: w.week, chosen_action: onlyAction });
      }
    }
  }

  // Non-lesson weeks: class (sub) > activity_supply > outing > consultation > special_training
  for (const w of otherWeeks) {
    const hasClass = w.available_actions.some((a) =>
      a === 'vo_class' || a === 'da_class' || a === 'vi_class',
    );

    if (hasClass && w.available_actions.includes(subClassAction)) {
      choices.push({ week: w.week, chosen_action: subClassAction });
    } else if (w.available_actions.includes('activity_supply')) {
      choices.push({ week: w.week, chosen_action: 'activity_supply' });
    } else if (w.available_actions.includes('outing')) {
      choices.push({ week: w.week, chosen_action: 'outing' });
    } else if (w.available_actions.includes('consultation')) {
      choices.push({ week: w.week, chosen_action: 'consultation' });
    } else if (w.available_actions.includes('special_training')) {
      choices.push({ week: w.week, chosen_action: 'special_training' });
    } else if (hasClass) {
      // Sub class not available, pick a main class
      const mainClassAction: ActionType = `${mainStats[0] ?? 'vo'}_class` as ActionType;
      if (w.available_actions.includes(mainClassAction)) {
        choices.push({ week: w.week, chosen_action: mainClassAction });
      } else if (w.available_actions.length > 0) {
        choices.push({ week: w.week, chosen_action: w.available_actions[0] as ActionType });
      }
    } else if (w.available_actions.length > 0) {
      choices.push({ week: w.week, chosen_action: w.available_actions[0] as ActionType });
    }
  }

  return choices;
}

function applySelectedPatternImpl(
  state: CalcState,
  index: number,
): Partial<CalcState> {
  if (index < 0 || index >= state.deckResults.length) {
    return { selectedPatternIndex: index };
  }

  const { plans } = useAppStore.getState();
  const plan = plans.find((p) => p.id === state.selectedPlanId);
  if (!plan) return { selectedPatternIndex: index, errorMessage: 'プランが見つかりません' };

  const pattern = state.deckResults[index];
  const mainStats = state._lastMainStats;

  // Auto-assign turn choices
  const turnChoices = autoAssignTurnChoices(plan, mainStats);

  // Build uncap levels
  const { cards: allCards, inventory } = useAppStore.getState();
  const uncapLevels = buildUncapLevels(allCards, inventory, state.ownedOnly);

  // Rental cards are 4 uncap
  for (const cs of pattern.selected_cards) {
    if (cs.is_rental) {
      uncapLevels[cs.card.id] = 4;
    }
  }

  const selectedCards = pattern.selected_cards.map((cs) => cs.card);
  const result = calculate(plan, selectedCards, turnChoices, uncapLevels);

  return {
    selectedPatternIndex: index,
    calculationResult: result,
    errorMessage: null,
  };
}

export const useCalcStore = create<CalcState>((set, get) => ({
  selectedPlanId: '',
  selectedPlanType: 'sense',
  voRole: 'サブ',
  daRole: 'サブ',
  viRole: 'サブ',
  voSpCount: 0,
  daSpCount: 0,
  viSpCount: 0,
  additionalCounts: emptyAdditionalCounts(),
  ownedOnly: false,
  contestMode: false,
  requiredCardIds: [],
  deckResults: [],
  selectedPatternIndex: 0,
  calculationResult: null,
  errorMessage: null,
  _lastMainStats: [],
  _lastLessonWeekCount: 0,

  setSelectedPlanId: (id) =>
    set({
      selectedPlanId: id,
      deckResults: [],
      calculationResult: null,
      errorMessage: null,
      selectedPatternIndex: 0,
    }),

  setSelectedPlanType: (type) => set({ selectedPlanType: type }),

  setRole: (stat, role) => {
    switch (stat) {
      case 'vo':
        set({ voRole: role });
        break;
      case 'da':
        set({ daRole: role });
        break;
      case 'vi':
        set({ viRole: role });
        break;
    }
  },

  setSpCount: (stat, count) => {
    const val = Math.max(0, count);
    switch (stat) {
      case 'vo':
        set({ voSpCount: val });
        break;
      case 'da':
        set({ daSpCount: val });
        break;
      case 'vi':
        set({ viSpCount: val });
        break;
    }
  },

  setAdditionalCount: (key, value) => {
    const state = get();
    set({
      additionalCounts: {
        ...state.additionalCounts,
        [key]: Math.max(0, value),
      },
    });
  },

  applyTemplate: (template) => {
    const counts = emptyAdditionalCounts();
    for (const [key, value] of Object.entries(template.counts)) {
      if (key in counts) {
        (counts as Record<string, number>)[key] = value;
      }
    }
    set({ additionalCounts: counts });
  },

  setOwnedOnly: (v) => set({ ownedOnly: v }),
  setContestMode: (v) => set({ contestMode: v }),

  addRequiredCard: (cardId) => {
    const state = get();
    if (state.requiredCardIds.length >= 4) return;
    if (state.requiredCardIds.includes(cardId)) return;
    set({ requiredCardIds: [...state.requiredCardIds, cardId] });
  },

  removeRequiredCard: (cardId) => {
    const state = get();
    set({ requiredCardIds: state.requiredCardIds.filter((id) => id !== cardId) });
  },

  executeCalculate: () => {
    try {
      const state = get();
      const { cards: allCards, plans, inventory } = useAppStore.getState();

      const plan = plans.find((p) => p.id === state.selectedPlanId);
      if (!plan) {
        set({ errorMessage: '育成プランを選択してください' });
        trackEvent('calculation_error', { error_message: '育成プランを選択してください' });
        return;
      }

      // Build mainStats
      const mainStats: string[] = [];
      if (state.voRole === 'メイン1') mainStats.push('vo');
      if (state.daRole === 'メイン1') mainStats.push('da');
      if (state.viRole === 'メイン1') mainStats.push('vi');
      if (state.voRole === 'メイン2') mainStats.push('vo');
      if (state.daRole === 'メイン2') mainStats.push('da');
      if (state.viRole === 'メイン2') mainStats.push('vi');

      // Find sub stat
      const subStat = ['vo', 'da', 'vi'].find((s) => !mainStats.includes(s));
      if (!subStat || mainStats.length !== 2) {
        set({ errorMessage: 'メイン1とメイン2に異なる属性を1つずつ設定してください' });
        trackEvent('calculation_error', { error_message: 'メイン1とメイン2に異なる属性を1つずつ設定してください' });
        return;
      }

      const lessonWeekCount = plan.schedule.filter((w) => (w.lessons?.length ?? 0) > 0).length;

      // SP counts
      const spCounts: Record<string, number> = {};
      if (state.voSpCount > 0) spCounts['vo'] = state.voSpCount;
      if (state.daSpCount > 0) spCounts['da'] = state.daSpCount;
      if (state.viSpCount > 0) spCounts['vi'] = state.viSpCount;

      // Candidate cards
      const candidateCards = getCandidateCards(allCards, inventory, state.ownedOnly, state.contestMode);
      const uncapLevels = buildUncapLevels(allCards, inventory, state.ownedOnly);

      // Rental pool: if ownedOnly, all cards are rental candidates (contest mode filter applied)
      let rentalPool: SupportCard[] | undefined;
      if (state.ownedOnly) {
        rentalPool = state.contestMode
          ? allCards.filter((c) => c.tag !== 'skill' && c.tag !== 'exam_item')
          : allCards;
      }

      // 必須カードはコンテストモード等のフィルタを回避して候補に含める
      const requiredCardIds = state.requiredCardIds.length > 0 ? state.requiredCardIds : undefined;
      if (requiredCardIds != null) {
        const requiredIdSet = new Set(requiredCardIds);
        const candidateIdSet = new Set(candidateCards.map((c) => c.id));

        if (state.ownedOnly) {
          // 所持済み必須カードを candidateCards に追加
          const ownedIdSet = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && ownedIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }

          // 全必須カードを rentalPool に追加（未所持必須カードの検索用）
          if (rentalPool != null) {
            const rentalIdSet = new Set(rentalPool.map((c) => c.id));
            for (const card of allCards) {
              if (requiredIdSet.has(card.id) && !rentalIdSet.has(card.id)) {
                rentalPool.push(card);
              }
            }
          }
        } else {
          // 全カード4凸モード: 必須カードを candidateCards に追加
          for (const card of allCards) {
            if (requiredIdSet.has(card.id) && !candidateIdSet.has(card.id)) {
              candidateCards.push(card);
            }
          }
        }
      }

      // 必須カードバリデーション
      if (requiredCardIds != null && state.ownedOnly) {
        const ownedIds = new Set(inventory.filter((e) => e.owned).map((e) => e.card_id));
        const notOwnedCount = requiredCardIds.filter((id) => !ownedIds.has(id)).length;
        if (notOwnedCount > 1) {
          set({ errorMessage: '未所持の必須カードは最大1枚です（レンタル枠使用）' });
          trackEvent('calculation_error', { error_message: '未所持の必須カードは最大1枚です' });
          return;
        }
      }

      startTimer('calculation');

      const patterns = selectMultiplePatterns(
        plan,
        candidateCards,
        mainStats,
        subStat,
        lessonWeekCount,
        spCounts,
        state.selectedPlanType,
        state.additionalCounts,
        uncapLevels,
        rentalPool,
        requiredCardIds,
      );

      // Find best pattern
      let bestIndex = 0;
      let bestTotal = -Infinity;
      for (let i = 0; i < patterns.length; i++) {
        if (patterns[i].total_value > bestTotal) {
          bestTotal = patterns[i].total_value;
          bestIndex = i;
        }
      }

      set({
        deckResults: patterns,
        _lastMainStats: mainStats,
        _lastLessonWeekCount: lessonWeekCount,
        errorMessage: null,
      });

      // Apply best pattern
      if (patterns.length > 0) {
        const calcTimeMs = endTimer('calculation');
        const sessionCalcCount = incrementCounter('calculation');

        const updates = applySelectedPatternImpl(
          { ...get(), deckResults: patterns, _lastMainStats: mainStats, _lastLessonWeekCount: lessonWeekCount },
          bestIndex,
        );
        set(updates as Partial<CalcState>);

        // 計算結果の詳細トラッキング
        const finalResult = updates.calculationResult;
        trackEvent('calculation_executed', {
          plan_id: state.selectedPlanId,
          plan_type: state.selectedPlanType,
          main_stats: mainStats.join(','),
          sub_stat: subStat,
          owned_only: state.ownedOnly,
          contest_mode: state.contestMode,
          patterns_count: patterns.length,
          calc_time_ms: calcTimeMs,
          session_calc_count: sessionCalcCount,
          result_vo: finalResult?.final_status.vo ?? 0,
          result_da: finalResult?.final_status.da ?? 0,
          result_vi: finalResult?.final_status.vi ?? 0,
          result_total: finalResult
            ? finalResult.final_status.vo + finalResult.final_status.da + finalResult.final_status.vi
            : 0,
          best_pattern_label: patterns[bestIndex]?.label ?? '',
          candidate_cards_count: candidateCards.length,
        });
        trackFunnelStep('calculator', 3, 'calculation_done');
      } else {
        set({ errorMessage: '有効な編成パターンが見つかりませんでした' });
        trackEvent('calculation_error', { error_message: '有効な編成パターンが見つかりませんでした' });
      }
    } catch (e) {
      set({ errorMessage: `計算エラー: ${(e as Error).message}` });
      trackEvent('calculation_error', { error_message: (e as Error).message });
    }
  },

  selectPattern: (index) => {
    const state = get();
    const pattern = state.deckResults[index];
    if (pattern) {
      trackEvent('pattern_selected', {
        pattern_index: index,
        pattern_label: pattern.label,
        pattern_total_value: pattern.total_value,
      });
    }
    const updates = applySelectedPatternImpl(state, index);
    set(updates as Partial<CalcState>);
  },
}));
