import { useEffect } from 'react';
import { useCalcStore, SCHEDULE_PLAN_IDS } from '../stores/calcStore';
import PlanSelector from '../components/calculator/PlanSelector';
import PlanTypeSelector from '../components/calculator/PlanTypeSelector';
import StatRoleConfig from '../components/calculator/StatRoleConfig';
import ScheduleBulkSettings from '../components/calculator/ScheduleBulkSettings';
import SchedulePresets from '../components/calculator/SchedulePresets';
import SchedulePicker from '../components/calculator/SchedulePicker';
import SpCountConfig from '../components/hif/SpCountConfig';
import EventCountConfig from '../components/calculator/EventCountConfig';
import RequiredCardSelector from '../components/calculator/RequiredCardSelector';
import ExcludedCardSelector from '../components/calculator/ExcludedCardSelector';
import CharacterSelector from '../components/calculator/CharacterSelector';
import MemoryBonusInput from '../components/calculator/MemoryBonusInput';
import OwnedOnlyToggle from '../components/calculator/OwnedOnlyToggle';
import ContestModeToggle from '../components/calculator/ContestModeToggle';
import ResultDisplay from '../components/calculator/ResultDisplay';
import DiagnosticCopyButton from '../components/calculator/DiagnosticCopyButton';
import PatternResultList from '../components/calculator/PatternResultList';
import DeckCardList from '../components/calculator/DeckCardList';
import WeekBreakdownTable from '../components/calculator/WeekBreakdownTable';

interface CalculatorPageProps {
  /** 指定するとそのプランに固定し、プラン選択ドロップダウンを隠す */
  fixedPlanId?: string;
  /** 見出しに表示するシナリオ名 */
  heading?: string;
}

export default function CalculatorPage({ fixedPlanId, heading }: CalculatorPageProps) {
  const {
    executeCalculate,
    calculationResult,
    deckResults,
    errorMessage,
    setSelectedPlanId,
    selectedPlanId,
  } = useCalcStore();

  // タブでプランを固定する場合、マウント時/タブ切替時に選択プランを設定する
  useEffect(() => {
    if (fixedPlanId) setSelectedPlanId(fixedPlanId);
  }, [fixedPlanId, setSelectedPlanId]);

  const planId = fixedPlanId ?? selectedPlanId;
  const isSchedulePlan = SCHEDULE_PLAN_IDS.has(planId);

  return (
    <div>
      <h2 className="text-xl font-bold mb-4">
        {heading ? `${heading} 育成ステータス理論値計算` : '育成ステータス理論値計算'}
      </h2>

      {/* 設定セクション */}
      <div className="bg-white rounded-lg p-4 shadow-sm mb-4 space-y-4">
        {!fixedPlanId && <PlanSelector />}
        <PlanTypeSelector />

        {isSchedulePlan ? (
          <>
            <ScheduleBulkSettings planId={planId} />
            {/* スケジュール調整: プリセット + 個別調整 */}
            <div className="space-y-3 bg-white rounded-md p-3 border border-gray-200">
              <SchedulePresets planId={planId} />
              <SchedulePicker planId={planId} />
            </div>
            {/* SP枚数設定（Vo/Da/Vi 独立。属性設定の撤去で失われる入力を補う） */}
            <SpCountConfig />
          </>
        ) : (
          <StatRoleConfig />
        )}
        <EventCountConfig />
        <RequiredCardSelector />
        <ExcludedCardSelector />
        <CharacterSelector />
        <MemoryBonusInput />

        <div className="flex items-center gap-4">
          <OwnedOnlyToggle />
          <ContestModeToggle />
          <button
            onClick={executeCalculate}
            className="px-6 py-2 bg-[var(--color-accent)] text-white rounded font-bold hover:opacity-90 cursor-pointer"
          >
            計算実行
          </button>
        </div>

        {errorMessage && (
          <p className="text-red-500 text-sm">{errorMessage}</p>
        )}
      </div>

      {/* 結果セクション */}
      {calculationResult && (
        <div className="space-y-4">
          <div className="bg-white rounded-lg p-4 shadow-sm">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between sm:gap-3">
              <div className="flex-1 order-2 sm:order-1">
                <ResultDisplay />
              </div>
              <div className="self-end order-1 sm:order-2 sm:self-auto">
                <DiagnosticCopyButton />
              </div>
            </div>
          </div>

          {deckResults.length > 0 && (
            <div className="bg-white rounded-lg p-4 shadow-sm">
              <h3 className="font-bold mb-3">編成パターン</h3>
              <PatternResultList />
              <div className="mt-4">
                <DeckCardList />
              </div>
            </div>
          )}

          <div className="bg-white rounded-lg p-4 shadow-sm">
            <WeekBreakdownTable />
          </div>
        </div>
      )}
    </div>
  );
}
