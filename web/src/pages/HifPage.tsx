import { useAppStore } from '../stores/appStore';
import { useHifStore } from '../stores/hifStore';
import ScheduleConfig from '../components/hif/ScheduleConfig';
import HifBulkSettings from '../components/hif/HifBulkSettings';
import HifSchedulePresets from '../components/hif/HifSchedulePresets';
import HifBonusConfig from '../components/hif/HifBonusConfig';
import SpCountConfig from '../components/hif/SpCountConfig';
import HifResultDisplay from '../components/hif/HifResultDisplay';
import HifPatternResultList from '../components/hif/HifPatternResultList';
import HifDeckCardList from '../components/hif/HifDeckCardList';
import PlanTypeSelector from '../components/calculator/PlanTypeSelector';
import EventCountConfig from '../components/calculator/EventCountConfig';
import OwnedOnlyToggle from '../components/calculator/OwnedOnlyToggle';
import ContestModeToggle from '../components/calculator/ContestModeToggle';
import RequiredCardSelector from '../components/calculator/RequiredCardSelector';
import CharacterSelector from '../components/calculator/CharacterSelector';
import MemoryBonusInput from '../components/calculator/MemoryBonusInput';

export default function HifPage() {
  const { plans } = useAppStore();
  const { executeCalculate, errorMessage, calculationResult, deckResults } = useHifStore();

  const hifPlan = plans.find((p) => p.id === 'hif');

  if (!hifPlan) {
    return (
      <div>
        <h2 className="text-xl font-bold mb-4">HIF (Hatsuboshi IDOL FESTIVAL)</h2>
        <div className="bg-yellow-50 border border-yellow-300 rounded-lg p-4">
          <p className="text-sm text-yellow-800">
            HIFプランデータの読み込みに失敗しました。Data/Plans/hif.yaml を確認してください。
          </p>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h2 className="text-xl font-bold mb-4">HIF (Hatsuboshi IDOL FESTIVAL)</h2>

      <div className="bg-white rounded-lg p-4 shadow-sm mb-4 space-y-4">
        {/* 育成タイプ */}
        <PlanTypeSelector />

        {/* 一括設定 (囲いの外) */}
        <HifBulkSettings />

        {/* スケジュール調整: プリセット + 個別調整 (育成タイプの次に配置) */}
        <div className="space-y-3 bg-white rounded-md p-3 border border-gray-200">
          <HifSchedulePresets />
          <ScheduleConfig />
        </div>

        {/* HIFボーナス (囲いの外、SP枚数設定の上) */}
        <HifBonusConfig />

        {/* SP枚数設定（Vo/Da/Vi 独立） */}
        <SpCountConfig />

        {/* イベント回数 (HIFテンプレートで絞り込み) */}
        <EventCountConfig planIdOverride="hif" />

        {/* 必須カード */}
        <RequiredCardSelector />

        {/* キャラ選択 */}
        <CharacterSelector />

        {/* 持ち込みメモリー */}
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
            <HifResultDisplay />
          </div>

          {deckResults.length > 0 && (
            <div className="bg-white rounded-lg p-4 shadow-sm">
              <HifPatternResultList />
              <div className="mt-4">
                <HifDeckCardList />
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
