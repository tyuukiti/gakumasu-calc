import { useCalcStore } from '../../stores/calcStore';
import { useAppStore } from '../../stores/appStore';

const STAT_CONFIG = [
  { key: 'vo' as const, label: 'Vocal', color: 'var(--color-vo)', bgColor: 'var(--color-vo-bg)' },
  { key: 'da' as const, label: 'Dance', color: 'var(--color-da)', bgColor: 'var(--color-da-bg)' },
  { key: 'vi' as const, label: 'Visual', color: 'var(--color-vi)', bgColor: 'var(--color-vi-bg)' },
];

export default function ResultDisplay() {
  const result = useCalcStore((s) => s.calculationResult);
  const resultBase = useCalcStore((s) => s.calculationResultWithoutCharacter);
  const selectedPlanId = useCalcStore((s) => s.selectedPlanId);
  const plans = useAppStore((s) => s.plans);

  if (!result) return null;

  const plan = plans.find((p) => p.id === selectedPlanId);
  const statCap = plan?.status_limit ?? 2800;

  const { final_status } = result;
  const total = final_status.vo + final_status.da + final_status.vi;
  const hasCharBonus = resultBase != null;
  const totalBase = resultBase
    ? resultBase.final_status.vo + resultBase.final_status.da + resultBase.final_status.vi
    : total;
  const totalDelta = total - totalBase;

  return (
    <div className="space-y-3">
      <h3 className="text-sm font-semibold text-gray-700">計算結果</h3>
      {STAT_CONFIG.map(({ key, label, color, bgColor }) => {
        const value = final_status[key];
        const valueBase = resultBase ? resultBase.final_status[key] : value;
        const delta = value - valueBase;
        const atCap = value >= statCap;
        const widthPercent = Math.min((value / statCap) * 100, 100);
        const widthBasePercent = Math.min((valueBase / statCap) * 100, 100);
        return (
          <div key={key} className="flex items-center gap-3">
            <span className="w-14 text-sm font-bold" style={{ color }}>{label}</span>
            <div
              className="relative flex-1 h-7 rounded-full overflow-hidden"
              style={{ backgroundColor: bgColor }}
            >
              {/* 合計バー（暗色） — 差分が右端に暗く見える */}
              <div
                className="absolute top-0 left-0 h-full transition-all duration-500 ease-out"
                style={{
                  width: `${widthPercent}%`,
                  backgroundColor: color,
                  filter: 'brightness(0.65)',
                }}
              />
              {/* 補正なしバー（通常色）を上に重ねる */}
              <div
                className="absolute top-0 left-0 h-full transition-all duration-500 ease-out"
                style={{
                  width: `${widthBasePercent}%`,
                  backgroundColor: color,
                }}
              />
            </div>
            <div className="w-20 text-right">
              <span
                className={`block text-sm font-mono font-bold ${atCap ? 'text-red-500' : ''}`}
              >
                {value}
              </span>
              {hasCharBonus && delta !== 0 && (
                <span className="block text-[10px] font-mono text-gray-500">
                  {delta > 0 ? '+' : ''}
                  {delta}
                </span>
              )}
            </div>
            {atCap && <span className="text-xs text-red-400 w-8">MAX</span>}
          </div>
        );
      })}
      <div className="flex items-center gap-3 pt-1 border-t border-gray-200">
        <span className="w-14 text-sm font-bold text-gray-600">合計</span>
        <div className="flex-1" />
        <div className="w-20 text-right">
          <span className="block text-lg font-mono font-bold text-[var(--color-accent)]">
            {total}
          </span>
          {hasCharBonus && totalDelta !== 0 && (
            <span className="block text-[10px] font-mono text-gray-500">
              補正なし: {totalBase}
            </span>
          )}
        </div>
        {STAT_CONFIG.some(({ key }) => final_status[key] >= statCap) && (
          <span className="w-8" />
        )}
      </div>
    </div>
  );
}
