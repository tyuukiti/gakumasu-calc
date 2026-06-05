import { useHifStore } from '../../stores/hifStore';
import { useAppStore } from '../../stores/appStore';
import { getFinalCapBonus } from '../../types/hifBonus';

const STAT_CONFIG = [
  { key: 'vo' as const, label: 'Vocal', color: 'var(--color-vo)', bgColor: 'var(--color-vo-bg)' },
  { key: 'da' as const, label: 'Dance', color: 'var(--color-da)', bgColor: 'var(--color-da-bg)' },
  { key: 'vi' as const, label: 'Visual', color: 'var(--color-vi)', bgColor: 'var(--color-vi-bg)' },
];

export default function HifResultDisplay() {
  const result = useHifStore((s) => s.calculationResult);
  const resultBase = useHifStore((s) => s.calculationResultWithoutCharacter);
  const bonusLevels = useHifStore((s) => s.bonusLevels);
  const plans = useAppStore((s) => s.plans);

  if (!result) return null;

  const plan = plans.find((p) => p.id === 'hif');
  // 本戦パラメータ上限増加 (finalStatLimitLevel) を加算した動的キャップを使う
  const statCap = (plan?.status_limit ?? 3000) + getFinalCapBonus(bonusLevels.finalStatLimitLevel);

  const { final_status } = result;
  // 合計は cap 適用後の実効値で表示する (algorithm の選出基準と一致させる)。
  // cap を超えた分は実ゲームでは捨てられるので、生の足し算では誤解を招く。
  const cap = (v: number) => Math.min(v, statCap);
  const total = cap(final_status.vo) + cap(final_status.da) + cap(final_status.vi);
  const totalUncapped = final_status.vo + final_status.da + final_status.vi;
  const totalOverflow = totalUncapped - total;
  const hasCharBonus = resultBase != null;
  const totalBase = resultBase
    ? cap(resultBase.final_status.vo) + cap(resultBase.final_status.da) + cap(resultBase.final_status.vi)
    : total;
  const totalDelta = total - totalBase;

  return (
    <div className="space-y-3">
      <h3 className="text-sm font-semibold text-gray-700">計算結果</h3>
      {STAT_CONFIG.map(({ key, label, color, bgColor }) => {
        const rawValue = final_status[key];
        const cappedValue = cap(rawValue);
        const rawValueBase = resultBase ? resultBase.final_status[key] : rawValue;
        const cappedValueBase = cap(rawValueBase);
        const delta = cappedValue - cappedValueBase;
        const atCap = rawValue >= statCap;
        const overflow = rawValue - cappedValue;
        const widthPercent = Math.min((rawValue / statCap) * 100, 100);
        const widthBasePercent = Math.min((rawValueBase / statCap) * 100, 100);
        return (
          <div key={key} className="flex items-center gap-3">
            <span className="w-14 text-sm font-bold" style={{ color }}>{label}</span>
            <div
              className="relative flex-1 h-7 rounded-full overflow-hidden"
              style={{ backgroundColor: bgColor }}
            >
              <div
                className="absolute top-0 left-0 h-full transition-all duration-500 ease-out"
                style={{
                  width: `${widthPercent}%`,
                  backgroundColor: color,
                  filter: 'brightness(0.65)',
                }}
              />
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
                title={atCap ? `実ゲーム表示値 ${cappedValue} (cap前 ${rawValue})` : undefined}
              >
                {cappedValue}
              </span>
              {hasCharBonus && delta !== 0 && (
                <span className="block text-[10px] font-mono text-gray-500">
                  {delta > 0 ? '+' : ''}
                  {delta}
                </span>
              )}
              {overflow > 0 && (
                <span
                  className="block text-[10px] font-mono text-gray-400"
                  title={`cap前は ${rawValue}。${overflow} 分は実ゲームで打ち止め`}
                >
                  元 {rawValue}
                </span>
              )}
            </div>
            {/* MAX列は常時スペース (w-8) を確保し、各属性のバー幅を揃える */}
            <span className="text-xs w-8 text-red-400">{atCap ? 'MAX' : ''}</span>
          </div>
        );
      })}
      <div className="flex items-center gap-3 pt-1 border-t border-gray-200">
        <span className="w-14 text-sm font-bold text-gray-600">合計</span>
        <div className="flex-1">
          {totalOverflow > 0 && (
            <span
              className="text-[10px] font-mono text-gray-400 whitespace-nowrap"
              title={`各属性の上限 (${statCap}) を超えた分は実ゲームでは捨てられるため、合計には含みません`}
            >
              cap超過 −{totalOverflow}
            </span>
          )}
        </div>
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
        <span className="w-8" />
      </div>
    </div>
  );
}
