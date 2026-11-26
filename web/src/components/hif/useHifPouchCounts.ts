import { useMemo } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useHifStore, buildHifTurnChoices } from '../../stores/hifStore';
import { computeHifPouchCounts, type HifPouchCounts } from '../../services/hifPouch';

/**
 * 現在のポーチ選択と日程 (scheduleChoices) からポーチの発動回数を集計する。
 * HifPouchConfig のプレビューと EventCountConfig の「（＋ポーチ N）」表示が同じ値を使うための共通フック。
 * 計算本体 (hifStore.executeCalculate) も同じ buildHifTurnChoices + computeHifPouchCounts を使う。
 */
export function useHifPouchCounts(): HifPouchCounts {
  const hifPouch = useAppStore((s) => s.hifPouch);
  const plans = useAppStore((s) => s.plans);
  const scheduleChoices = useHifStore((s) => s.scheduleChoices);
  const pouch = useHifStore((s) => s.pouch);
  return useMemo(() => {
    const plan = plans.find((p) => p.id === 'hif');
    const turnChoices = plan ? buildHifTurnChoices(plan, scheduleChoices) : [];
    return computeHifPouchCounts(hifPouch, pouch, turnChoices);
  }, [hifPouch, plans, scheduleChoices, pouch]);
}
