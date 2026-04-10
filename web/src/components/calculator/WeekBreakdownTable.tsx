import { useState } from 'react';
import { useCalcStore } from '../../stores/calcStore';
import { trackEvent, trackFunnelStep } from '../../utils/analytics';

export default function WeekBreakdownTable() {
  const [expanded, setExpanded] = useState(false);
  const result = useCalcStore((s) => s.calculationResult);

  if (!result) return null;

  return (
    <div className="space-y-2">
      <button
        type="button"
        className="text-sm text-[var(--color-accent)] hover:underline flex items-center gap-1"
        onClick={() => {
          const next = !expanded;
          trackEvent('week_breakdown_expanded', { expanded: next });
          if (next) trackFunnelStep('calculator', 4, 'result_detail_viewed');
          setExpanded(next);
        }}
      >
        <span className={`inline-block transition-transform ${expanded ? 'rotate-90' : ''}`}>
          &#9654;
        </span>
        週別内訳を{expanded ? '閉じる' : '表示'}
      </button>

      {expanded && (
        <div className="max-h-96 overflow-y-auto border border-gray-200 rounded-md">
          <table className="w-full text-sm">
            <thead className="bg-gray-100 sticky top-0">
              <tr>
                <th className="px-3 py-2 text-left font-semibold text-gray-600 w-12">週</th>
                <th className="px-3 py-2 text-left font-semibold text-gray-600">行動</th>
                <th className="px-3 py-2 text-right font-semibold" style={{ color: 'var(--color-vo)' }}>Vo</th>
                <th className="px-3 py-2 text-right font-semibold" style={{ color: 'var(--color-da)' }}>Da</th>
                <th className="px-3 py-2 text-right font-semibold" style={{ color: 'var(--color-vi)' }}>Vi</th>
              </tr>
            </thead>
            <tbody>
              {result.week_details.map((week) => {
                const hasGain = week.gain.vo !== 0 || week.gain.da !== 0 || week.gain.vi !== 0;
                return (
                  <tr
                    key={week.week}
                    className={`border-t border-gray-100 ${hasGain ? '' : 'text-gray-400'}`}
                  >
                    <td className="px-3 py-1.5 font-mono">{week.week}</td>
                    <td className="px-3 py-1.5">{week.action_name}</td>
                    <td className="px-3 py-1.5 text-right font-mono">
                      {week.gain.vo > 0 ? `+${week.gain.vo}` : week.gain.vo || '-'}
                    </td>
                    <td className="px-3 py-1.5 text-right font-mono">
                      {week.gain.da > 0 ? `+${week.gain.da}` : week.gain.da || '-'}
                    </td>
                    <td className="px-3 py-1.5 text-right font-mono">
                      {week.gain.vi > 0 ? `+${week.gain.vi}` : week.gain.vi || '-'}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
