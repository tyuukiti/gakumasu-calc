import { useState } from 'react';
import { useCalcStore } from '../../stores/calcStore';
import type { CardScore } from '../../types/results';
import { trackEvent } from '../../utils/analytics';

const TYPE_COLORS: Record<string, { bg: string; text: string }> = {
  vo: { bg: 'var(--color-vo-bg)', text: 'var(--color-vo)' },
  da: { bg: 'var(--color-da-bg)', text: 'var(--color-da)' },
  vi: { bg: 'var(--color-vi-bg)', text: 'var(--color-vi)' },
  all: { bg: 'var(--color-all-bg)', text: '#4caf50' },
};

const TYPE_LABELS: Record<string, string> = {
  vo: 'Vo',
  da: 'Da',
  vi: 'Vi',
  all: 'All',
};

const PLAN_LABELS: Record<string, string> = {
  sense: 'セ',
  logic: 'ロ',
  anomaly: 'ア',
  free: 'フリー',
};

const STAT_COLORS: Record<string, string> = {
  vo: 'var(--color-vo)',
  da: 'var(--color-da)',
  vi: 'var(--color-vi)',
};

function BreakdownPanel({ cs }: { cs: CardScore }) {
  return (
    <div className="px-3 pb-2 text-xs space-y-1">
      <div className="flex gap-3 font-mono text-gray-600">
        <span style={{ color: STAT_COLORS.vo }}>Vo:{cs.raw_vo}</span>
        <span style={{ color: STAT_COLORS.da }}>Da:{cs.raw_da}</span>
        <span style={{ color: STAT_COLORS.vi }}>Vi:{cs.raw_vi}</span>
      </div>
      {cs.breakdowns.map((b, i) => (
        <div key={i} className="flex justify-between font-mono text-gray-500">
          <span className="truncate mr-2">{b.reason}</span>
          <span className="shrink-0" style={{ color: STAT_COLORS[b.stat] }}>
            {b.value > 0 ? '+' : ''}{b.value}
          </span>
        </div>
      ))}
    </div>
  );
}

export default function DeckCardList() {
  const deckResults = useCalcStore((s) => s.deckResults);
  const selectedPatternIndex = useCalcStore((s) => s.selectedPatternIndex);
  const [expandedIndex, setExpandedIndex] = useState<number | null>(null);

  if (deckResults.length === 0 || selectedPatternIndex >= deckResults.length) {
    return null;
  }

  const pattern = deckResults[selectedPatternIndex];

  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-700">選択デッキ</h3>
      <div className="space-y-1">
        {pattern.selected_cards.map((cs, index) => {
          const typeStyle = TYPE_COLORS[cs.card.type] ?? TYPE_COLORS['all'];
          const suffix = cs.is_rental ? ' (レンタル)' : cs.is_required ? ' (必須)' : '';
          const displayName = cs.card.name + suffix;
          const isExpanded = expandedIndex === index;

          return (
            <div
              key={`${cs.card.id}-${index}`}
              className="rounded-md bg-white border border-gray-200 hover:border-gray-300 transition-colors cursor-pointer"
              onClick={() => {
                if (!isExpanded) {
                  trackEvent('deck_card_expanded', {
                    card_id: cs.card.id,
                    card_name: cs.card.name,
                    is_rental: cs.is_rental,
                  });
                }
                setExpandedIndex(isExpanded ? null : index);
              }}
            >
              <div className="flex items-center gap-2 px-3 py-2">
                {/* Type badge */}
                <span
                  className="text-xs font-bold px-1.5 py-0.5 rounded"
                  style={{ backgroundColor: typeStyle.bg, color: typeStyle.text }}
                >
                  {TYPE_LABELS[cs.card.type] ?? cs.card.type}
                </span>

                {/* Rarity */}
                <span className="text-xs text-gray-500">{cs.card.rarity}</span>

                {/* Plan */}
                {cs.card.plan && (
                  <span className="text-xs text-gray-400">
                    {PLAN_LABELS[cs.card.plan] ?? cs.card.plan}
                  </span>
                )}

                {/* Card name */}
                <span className={`flex-1 text-sm truncate ${cs.is_rental ? 'text-orange-600' : cs.is_required ? 'text-purple-600' : 'text-gray-800'}`}>
                  {displayName}
                </span>

                {/* Stat value */}
                <span className="text-sm font-mono font-bold text-[var(--color-accent)]">
                  +{cs.total_value}
                </span>

                {/* Expand indicator */}
                <span className={`text-xs text-gray-400 transition-transform ${isExpanded ? 'rotate-90' : ''}`}>
                  &#9654;
                </span>
              </div>

              {isExpanded && <BreakdownPanel cs={cs} />}
            </div>
          );
        })}
      </div>
    </div>
  );
}
