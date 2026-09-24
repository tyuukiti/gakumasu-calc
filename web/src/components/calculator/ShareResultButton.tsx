import { useEffect, useState } from 'react';
import type { CalculationResult } from '../../types/results';
import { useCalcStore } from '../../stores/calcStore';
import { useAppStore } from '../../stores/appStore';
import { useHifStore } from '../../stores/hifStore';
import { getFinalCapBonus } from '../../types/hifBonus';
import { PLAN_TYPE_LABELS } from '../../services/diagnostics';
import { buildShareIntentUrl, buildShareText, type ShareTextInput } from '../../services/shareText';
import { buildSharePayload } from '../../services/sharePayload';
import { buildShareUrl, encodeSharePayload, isShareEncodingSupported } from '../../services/shareState';
import { trackEvent } from '../../utils/analytics';

interface Props {
  /** 'calc' = 通常計算タブ (初レジェンド/NIA) / 'hif' = HIFタブ。結果・編成の取得元ストアを切り替える。 */
  mode?: 'calc' | 'hif';
}

const HASHTAGS = ['学マス', 'GakumasuCalc'];

/**
 * 計算結果 (到達ステータス・選択編成) を本文にして X の投稿画面を新しいタブで開くボタン。
 * 投稿 URL は現在のタブの URL (シナリオ別ページ) に、結果を再現する共有トークン (#s=...) を付けたもの。
 * リンクを開いた人には共有元と同じ結果がそのまま表示される。
 */
export default function ShareResultButton({ mode = 'calc' }: Props = {}) {
  const calcResult = useCalcStore((s) => s.calculationResult);
  const hifResult = useHifStore((s) => s.calculationResult);
  const result = mode === 'hif' ? hifResult : calcResult;

  // 共有トークンは結果が変わるたびに事前に作っておく。クリック時に await すると
  // window.open がポップアップブロックに掛かるため、同期的に開けるようにする。
  // どの結果に対するトークンかを一緒に持ち、結果が変わった直後の古いトークンは使わない。
  const [prepared, setPrepared] = useState<{ result: CalculationResult; token: string } | null>(null);
  useEffect(() => {
    if (!result || !isShareEncodingSupported()) return;
    const payload = buildSharePayload(mode, useCalcStore.getState(), useHifStore.getState());
    if (!payload) return;
    let cancelled = false;
    encodeSharePayload(payload)
      .then((token) => {
        if (!cancelled) setPrepared({ result, token });
      })
      .catch(() => {
        /* 生成に失敗したらページ URL のみで共有する */
      });
    return () => {
      cancelled = true;
    };
  }, [mode, result]);
  const token = prepared && prepared.result === result ? prepared.token : null;

  const buildInput = (): ShareTextInput | null => {
    const calc = useCalcStore.getState();
    const app = useAppStore.getState();
    const hif = useHifStore.getState();
    const source = mode === 'hif' ? hif : calc;
    const current = source.calculationResult;
    if (!current) return null;

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
      finalStatus: current.final_status,
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
    const shareUrl = token ? buildShareUrl(pageUrl, token) : pageUrl;
    trackEvent('result_shared', {
      mode,
      has_cards: text.includes('編成:'),
      has_result_link: token != null,
    });
    window.open(buildShareIntentUrl(text, shareUrl, HASHTAGS), '_blank', 'noopener,noreferrer');
  };

  return (
    <button
      type="button"
      onClick={handleShare}
      className="text-sm px-3 py-1.5 border border-gray-300 rounded text-gray-600 hover:bg-gray-50 cursor-pointer whitespace-nowrap"
      title="到達ステータスと編成を本文にして X の投稿画面を開きます（投稿前に編集できます）。リンクを開いた人にはこの結果がそのまま表示されます"
    >
      X で結果をシェア
    </button>
  );
}
