import { useState } from 'react';
import type { AbilitySummaryEntry } from '../types/results';

const STAT_COLORS: Record<string, string> = {
  vo: 'var(--color-vo)',
  da: 'var(--color-da)',
  vi: 'var(--color-vi)',
  all: '#4caf50',
};

const STAT_LABELS: Record<string, string> = {
  vo: 'Vo',
  da: 'Da',
  vi: 'Vi',
  all: 'All',
};

/**
 * 選択デッキ6枚を行動別に合算した「アビリティまとめ」行 (デッキリストの7番目の項目)。
 * 「授業終了 Vo+75 (45+30) ×6回  +450」形式で、total 降順に並ぶ。
 * 「どのカード(行動)を取るとパラメが伸びるか」をパッと比較するための表示。
 */
export default function AbilitySummaryRow({ entries }: { entries: AbilitySummaryEntry[] }) {
  const [expanded, setExpanded] = useState(false);

  if (entries.length === 0) return null;

  return (
    <div
      className="rounded-md bg-amber-50 border border-amber-200 hover:border-amber-300 transition-colors cursor-pointer"
      onClick={() => setExpanded(!expanded)}
    >
      <div className="flex items-center gap-2 px-3 py-2">
        <span className="text-xs font-bold px-1.5 py-0.5 rounded bg-amber-100 text-amber-700">
          まとめ
        </span>
        <span className="flex-1 text-sm text-gray-800">アビリティまとめ（行動別）</span>
        <span className="text-xs text-gray-400">{entries.length}項目</span>
        <span className={`text-xs text-gray-400 transition-transform ${expanded ? 'rotate-90' : ''}`}>
          &#9654;
        </span>
      </div>

      {expanded && (
        <div className="px-3 pb-2 text-xs space-y-1">
          {entries.map((e, i) => {
            const color = STAT_COLORS[e.stat] ?? STAT_COLORS.all;
            const label = STAT_LABELS[e.stat] ?? e.stat;
            return (
              <div key={i} className="flex justify-between font-mono text-gray-500">
                <span className="truncate mr-2">
                  {e.trigger_name}{' '}
                  <span style={{ color }}>
                    {label}+{e.per_fire}
                  </span>
                  {e.parts.length > 1 && (
                    <span className="text-gray-400"> ({e.parts.join('+')})</span>
                  )}
                  <span className="text-gray-400"> ×{e.fires}回</span>
                </span>
                <span className="shrink-0 font-bold" style={{ color }}>
                  +{e.total}
                </span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
