import { useCalcStore } from '../stores/calcStore';
import PlanSelector from '../components/calculator/PlanSelector';
import PlanTypeSelector from '../components/calculator/PlanTypeSelector';
import StatRoleConfig from '../components/calculator/StatRoleConfig';
import EventCountConfig from '../components/calculator/EventCountConfig';
import OwnedOnlyToggle from '../components/calculator/OwnedOnlyToggle';
import ContestModeToggle from '../components/calculator/ContestModeToggle';
import ResultDisplay from '../components/calculator/ResultDisplay';
import PatternResultList from '../components/calculator/PatternResultList';
import DeckCardList from '../components/calculator/DeckCardList';
import WeekBreakdownTable from '../components/calculator/WeekBreakdownTable';

export default function CalculatorPage() {
  const {
    executeCalculate,
    calculationResult,
    deckResults,
    errorMessage,
  } = useCalcStore();

  return (
    <div>
      <h2 className="text-xl font-bold mb-4">育成ステータス理論値計算</h2>

      {/* 設定セクション */}
      <div className="bg-white rounded-lg p-4 shadow-sm mb-4 space-y-4">
        <PlanSelector />
        <PlanTypeSelector />

        <StatRoleConfig />
        <EventCountConfig />

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
          <ResultDisplay />

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
