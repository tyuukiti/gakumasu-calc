import { useCalcStore } from '../../stores/calcStore';
import { trackEvent } from '../../utils/analytics';

export default function ContestModeToggle() {
  const contestMode = useCalcStore((s) => s.contestMode);
  const setContestMode = useCalcStore((s) => s.setContestMode);

  return (
    <label className="flex items-center gap-2 cursor-pointer select-none" title="スキルカード・コンテストアイテムのサポカを除外">
      <input
        type="checkbox"
        checked={contestMode}
        onChange={(e) => {
          trackEvent('contest_mode_toggled', { enabled: e.target.checked });
          setContestMode(e.target.checked);
        }}
        className="w-4 h-4 accent-[var(--color-accent)] rounded"
      />
      <span className="text-sm text-gray-700">コンテストモード</span>
    </label>
  );
}
