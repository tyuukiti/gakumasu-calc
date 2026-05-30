import {
  useHifStore,
  HIF_OVERFLOW_PENALTY_THRESHOLD_MIN,
  HIF_OVERFLOW_PENALTY_THRESHOLD_MAX,
} from '../../stores/hifStore';

/**
 * HIFモード: MAX大幅超過時にカード再抽選を促すオプション。
 * 所持カードのみ/コンテストモードと同じ行に並べる用のコンパクトUI。
 */
export default function HifOverflowPenaltyToggle() {
  const overflowPenalty = useHifStore((s) => s.overflowPenalty);
  const setEnabled = useHifStore((s) => s.setOverflowPenaltyEnabled);
  const setThreshold = useHifStore((s) => s.setOverflowPenaltyThreshold);

  return (
    <div className="flex items-center gap-2 select-none">
      <label
        className="flex items-center gap-2 cursor-pointer"
        title={`合計overflow (Vo+Da+Vi のキャップ超過量) が閾値を超えた時のみ、カード差替を促す × 2 罰則を適用 (${HIF_OVERFLOW_PENALTY_THRESHOLD_MIN}–${HIF_OVERFLOW_PENALTY_THRESHOLD_MAX})`}
      >
        <input
          type="checkbox"
          checked={overflowPenalty.enabled}
          onChange={(e) => setEnabled(e.target.checked)}
          className="w-4 h-4 accent-[var(--color-accent)] rounded"
        />
        <span className="text-sm text-gray-700">MAX大幅超過時に再抽選</span>
      </label>
      <input
        type="number"
        min={HIF_OVERFLOW_PENALTY_THRESHOLD_MIN}
        max={HIF_OVERFLOW_PENALTY_THRESHOLD_MAX}
        step={10}
        value={overflowPenalty.threshold}
        disabled={!overflowPenalty.enabled}
        onChange={(e) => setThreshold(Number(e.target.value))}
        className="w-16 px-2 py-0.5 border border-gray-300 rounded font-mono text-sm text-right disabled:opacity-40 disabled:cursor-not-allowed"
        title="閾値 (Vo+Da+Vi 合計のキャップ超過量)"
      />
    </div>
  );
}
