import { useCalcStore } from '../../stores/calcStore';

const STAT_CONFIG: {
  key: 'vo' | 'da' | 'vi';
  label: string;
  color: string;
  bgColor: string;
}[] = [
  { key: 'vo', label: 'Vocal', color: 'var(--color-vo-text)', bgColor: 'var(--color-vo-bg)' },
  { key: 'da', label: 'Dance', color: 'var(--color-da-text)', bgColor: 'var(--color-da-bg)' },
  { key: 'vi', label: 'Visual', color: 'var(--color-vi-text)', bgColor: 'var(--color-vi-bg)' },
];

/**
 * HIF用 SP枚数設定。Vo/Da/Vi 各独立で SP率カード必須枚数を指定する（メイン/サブ概念なし）。
 * 状態は既存の calcStore.voSpCount / daSpCount / viSpCount を流用する。
 */
export default function SpCountConfig() {
  const voSpCount = useCalcStore((s) => s.voSpCount);
  const daSpCount = useCalcStore((s) => s.daSpCount);
  const viSpCount = useCalcStore((s) => s.viSpCount);
  const setSpCount = useCalcStore((s) => s.setSpCount);

  const spCounts = { vo: voSpCount, da: daSpCount, vi: viSpCount };

  return (
    <div className="space-y-2">
      <label className="text-sm font-semibold text-gray-700">SP枚数設定</label>
      <div className="space-y-2">
        {STAT_CONFIG.map(({ key, label, color, bgColor }) => (
          <div
            key={key}
            className="flex items-center gap-3 rounded-md px-3 py-2"
            style={{ backgroundColor: bgColor }}
          >
            <span className="w-16 text-sm font-bold" style={{ color }}>
              {label}
            </span>
            <label className="flex items-center gap-1.5 text-sm text-gray-600 ml-auto">
              <span>SP率枚数</span>
              <input
                type="number"
                min={0}
                max={6}
                className="w-14 border border-gray-300 rounded px-2 py-1 text-sm text-center bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                value={spCounts[key]}
                onChange={(e) => setSpCount(key, parseInt(e.target.value) || 0)}
              />
            </label>
          </div>
        ))}
      </div>
    </div>
  );
}
