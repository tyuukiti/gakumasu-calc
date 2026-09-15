import { useCalcStore } from '../../stores/calcStore';
import { useAppStore } from '../../stores/appStore';
import { useHifStore } from '../../stores/hifStore';
import { getFinalCapBonus } from '../../types/hifBonus';
import { PLAN_TYPE_LABELS } from '../../services/diagnostics';
import { buildShareIntentUrl, buildShareText, type ShareTextInput } from '../../services/shareText';
import { trackEvent } from '../../utils/analytics';

interface Props {
  /** 'calc' = 通常計算タブ (初レジェンド/NIA) / 'hif' = HIFタブ。結果・編成の取得元ストアを切り替える。 */
  mode?: 'calc' | 'hif';
}

const HASHTAGS = ['学マス', 'GakumasuCalc'];

/**
 * 計算結果 (到達ステータス・選択編成) を本文にして X の投稿画面を新しいタブで開くボタン。
 * 投稿 URL は現在のタブの URL (シナリオ別ページ) を使う。
 */
export default function ShareResultButton({ mode = 'calc' }: Props = {}) {
  const buildInput = (): ShareTextInput | null => {
    const calc = useCalcStore.getState();
    const app = useAppStore.getState();
    const hif = useHifStore.getState();
    const source = mode === 'hif' ? hif : calc;
    const result = source.calculationResult;
    if (!result) return null;

    const planId = mode === 'hif' ? 'hif' : calc.selectedPlanId;
    const plan = app.plans.find((p) => p.id === planId);
    // 上限は各結果表示 (ResultDisplay / HifResultDisplay) と同じ値を使う
    const statCap =
      mode === 'hif'
        ? (plan?.status_limit ?? 3000) + getFinalCapBonus(hif.bonusLevels.finalStatLimitLevel)
        : (plan?.status_limit ?? 2800);

    const character = calc.selectedCharacterId
      ? app.characters.find((c) => c.id === calc.selectedCharacterId)
      : null;
    const pattern = source.deckResults[source.selectedPatternIndex];

    return {
      scenarioLabel: mode === 'hif' ? 'H.I.F' : (plan?.name ?? planId),
      planTypeLabel: PLAN_TYPE_LABELS[calc.selectedPlanType] ?? calc.selectedPlanType,
      characterName: character?.name ?? null,
      finalStatus: result.final_status,
      statCap,
      cards: pattern
        ? pattern.selected_cards.map((cs) => ({
            name: cs.card.name,
            uncap: cs.is_rental ? 4 : cs.uncap_level,
            isRental: cs.is_rental,
          }))
        : [],
    };
  };

  const handleShare = () => {
    const input = buildInput();
    if (!input) return;
    const text = buildShareText(input, HASHTAGS);
    const pageUrl = `${window.location.origin}${window.location.pathname}`;
    trackEvent('result_shared', { mode, has_cards: text.includes('編成:') });
    window.open(buildShareIntentUrl(text, pageUrl, HASHTAGS), '_blank', 'noopener,noreferrer');
  };

  return (
    <button
      type="button"
      onClick={handleShare}
      className="text-sm px-3 py-1.5 border border-gray-300 rounded text-gray-600 hover:bg-gray-50 cursor-pointer whitespace-nowrap"
      title="到達ステータスと編成を本文にして X の投稿画面を開きます（投稿前に編集できます）"
    >
      X で結果をシェア
    </button>
  );
}
