import { Link } from 'react-router-dom';
import { useCalcStore } from '../../stores/calcStore';
import { useHifStore } from '../../stores/hifStore';
import { useAppStore } from '../../stores/appStore';
import { getFinalCapBonus } from '../../types/hifBonus';
import { PLAN_TYPE_LABELS } from '../../services/diagnostics';
import { trackEvent } from '../../utils/analytics';

interface Props {
  /** 'calc' = 初レジェンド/NIA タブ / 'hif' = HIFタブ。共有状態・結果の取得元ストアを切り替える。 */
  mode: 'calc' | 'hif';
}

/**
 * 共有 URL から復元した結果を表示中であることを示すバナー。
 * 開いた人がスクロールせずに結果 (到達ステータス・編成) と条件を把握でき、
 * そのまま「この条件で自分の手持ちで計算」へ進めるようにする。
 */
export default function SharedResultBanner({ mode }: Props) {
  const calcShareView = useCalcStore((s) => s.shareView);
  const hifShareView = useHifStore((s) => s.shareView);
  const calcResult = useCalcStore((s) => s.calculationResult);
  const hifResult = useHifStore((s) => s.calculationResult);
  const calcDecks = useCalcStore((s) => s.deckResults);
  const hifDecks = useHifStore((s) => s.deckResults);
  const selectedPlanId = useCalcStore((s) => s.selectedPlanId);
  const selectedPlanType = useCalcStore((s) => s.selectedPlanType);
  const selectedCharacterId = useCalcStore((s) => s.selectedCharacterId);
  const uncap3BonusEnabled = useCalcStore((s) => s.uncap3BonusEnabled);
  const step4BonusEnabled = useCalcStore((s) => s.step4BonusEnabled);
  const voSpCount = useCalcStore((s) => s.voSpCount);
  const daSpCount = useCalcStore((s) => s.daSpCount);
  const viSpCount = useCalcStore((s) => s.viSpCount);
  const selectedTemplateName = useCalcStore((s) => s.selectedTemplateName);
  const additionalCounts = useCalcStore((s) => s.additionalCounts);
  const memoryBonuses = useCalcStore((s) => s.memoryBonuses);
  const requiredCardIds = useCalcStore((s) => s.requiredCardIds);
  const excludedCardIds = useCalcStore((s) => s.excludedCardIds);
  const bonusLevels = useHifStore((s) => s.bonusLevels);
  const plans = useAppStore((s) => s.plans);
  const characters = useAppStore((s) => s.characters);
  const inventory = useAppStore((s) => s.inventory);

  const shareView = mode === 'hif' ? hifShareView : calcShareView;
  const result = mode === 'hif' ? hifResult : calcResult;
  const deck = (mode === 'hif' ? hifDecks : calcDecks)[0];
  if (!shareView || !result || !deck) return null;

  const planId = mode === 'hif' ? 'hif' : selectedPlanId;
  const plan = plans.find((p) => p.id === planId);
  const statCap =
    mode === 'hif'
      ? (plan?.status_limit ?? 3000) + getFinalCapBonus(bonusLevels.finalStatLimitLevel)
      : (plan?.status_limit ?? 2800);
  const cap = (v: number) => Math.min(v, statCap);
  const { vo, da, vi } = result.final_status;
  const total = cap(vo) + cap(da) + cap(vi);
  const mark = (v: number) => (v >= statCap ? `${cap(v)} (MAX)` : `${cap(v)}`);

  const character = selectedCharacterId ? characters.find((c) => c.id === selectedCharacterId) : null;
  const characterLabel = character
    ? `${character.name}${character.uncap3_bonus ? `（3凸${uncap3BonusEnabled ? 'ON' : 'OFF'}）` : ''}${
        character.step4_bonus ? `（STEP4 ${step4BonusEnabled ? 'ON' : 'OFF'}）` : ''
      }`
    : 'キャラ補正なし';
  const conditions = [
    mode === 'hif' ? 'H.I.F' : (plan?.name ?? planId),
    PLAN_TYPE_LABELS[selectedPlanType] ?? selectedPlanType,
    characterLabel,
  ];

  // 共有元から引き継いだ入力条件の要約 (数値に効く項目を一覧して「持ってきている」ことを示す)
  const eventCountTotal = Object.values(additionalCounts).reduce((s, v) => s + (v > 0 ? v : 0), 0);
  const eventLabel = selectedTemplateName
    ? `テンプレ「${selectedTemplateName}」`
    : eventCountTotal > 0
      ? `個別設定（計 ${eventCountTotal} 回）`
      : 'なし';
  const memoryCount = memoryBonuses.filter(
    (m) => m.vo.value !== 0 || m.da.value !== 0 || m.vi.value !== 0,
  ).length;
  const details = [
    `SP枚数 Vo${voSpCount} / Da${daSpCount} / Vi${viSpCount}`,
    `イベント回数: ${eventLabel}`,
    `持ち込みメモリー: ${memoryCount > 0 ? `${memoryCount}枚` : 'なし'}`,
  ];
  if (requiredCardIds.length > 0) details.push(`必須カード ${requiredCardIds.length}枚`);
  if (excludedCardIds.length > 0) details.push(`除外カード ${excludedCardIds.length}枚`);
  if (mode === 'hif') {
    details.push(
      `HIFボーナス Vo Lv${bonusLevels.voUpLevel} / Da Lv${bonusLevels.daUpLevel} / Vi Lv${bonusLevels.viUpLevel} / 本戦上限 Lv${bonusLevels.finalStatLimitLevel}`,
    );
  }
  const sharedAt =
    shareView.sharedAt > 0
      ? new Date(shareView.sharedAt * 1000).toLocaleString('ja-JP', { dateStyle: 'medium', timeStyle: 'short' })
      : null;

  const hasInventory = inventory.some((e) => e.owned);

  const handleOwnCalc = () => {
    trackEvent('shared_result_own_calc', { plan_id: planId, has_inventory: hasInventory });
    if (mode === 'hif') {
      const hif = useHifStore.getState();
      hif.exitShareView(true);
      hif.executeCalculate();
    } else {
      const calc = useCalcStore.getState();
      calc.exitShareView(true);
      calc.executeCalculate();
    }
  };

  return (
    <div className="bg-blue-50 border border-blue-200 rounded-lg p-4 mb-4 space-y-3">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="space-y-1">
          <p className="text-sm font-semibold text-blue-900">共有された計算結果を表示しています</p>
          <p className="text-xs text-blue-800">
            編成と条件は共有元のもので、あなたの所持カードは反映されていません。
            {sharedAt && <span className="text-blue-600">（{sharedAt} 共有）</span>}
          </p>
          <p className="text-xs text-gray-600">{conditions.join(' / ')}</p>
          <p className="text-xs text-gray-500">{details.join('　')}</p>
        </div>
        <button
          type="button"
          onClick={handleOwnCalc}
          className="shrink-0 px-4 py-2 bg-[var(--color-accent)] text-white rounded text-sm font-bold hover:opacity-90 cursor-pointer whitespace-nowrap"
          title="共有元の条件 (日程・育成タイプ・キャラなど) を引き継いで、自分の所持カードで最適編成を計算します"
        >
          {hasInventory ? 'この条件で自分の手持ちで計算' : 'この条件で計算する（全カード4凸）'}
        </button>
      </div>

      <div className="text-sm font-mono font-bold text-gray-800">
        Vo {mark(vo)} / Da {mark(da)} / Vi {mark(vi)}
        <span className="ml-3 text-[var(--color-accent)]">合計 {total}</span>
      </div>
      <p className="text-xs text-gray-700 break-words">
        編成:{' '}
        {deck.selected_cards
          .map((cs) =>
            cs.is_rental
              ? `${cs.card.name}(レンタル)`
              : cs.uncap_level === 4
                ? cs.card.name
                : `${cs.card.name}(${cs.uncap_level}凸)`,
          )
          .join(' / ')}
      </p>

      {!hasInventory && (
        <p className="text-xs text-blue-800">
          所持カードを
          <Link to="/inventory" className="underline hover:opacity-80">
            所持管理
          </Link>
          で登録すると、自分の手持ちでの最適編成を計算できます。
        </p>
      )}
      <p className="text-[11px] text-blue-700">
        カードデータの更新により、共有時と数値が異なる場合があります。
      </p>
    </div>
  );
}
