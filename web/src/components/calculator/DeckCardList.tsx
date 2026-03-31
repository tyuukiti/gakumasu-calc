import { useCalcStore } from '../../stores/calcStore';

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

export default function DeckCardList() {
  const deckResults = useCalcStore((s) => s.deckResults);
  const selectedPatternIndex = useCalcStore((s) => s.selectedPatternIndex);

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
          const displayName = cs.is_rental
            ? `${cs.card.name} (レンタル)`
            : cs.card.name;
          const breakdownText = [
            `Vo:${cs.raw_vo} Da:${cs.raw_da} Vi:${cs.raw_vi}`,
            ...cs.breakdowns.map((b) => `  ${b.reason} -> ${b.value > 0 ? '+' : ''}${b.value}`),
          ].join('\n');

          return (
            <div
              key={`${cs.card.id}-${index}`}
              className="flex items-center gap-2 rounded-md px-3 py-2 bg-white border border-gray-200 hover:border-gray-300 transition-colors group relative"
              title={breakdownText}
            >
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
              <span className={`flex-1 text-sm truncate ${cs.is_rental ? 'text-orange-600' : 'text-gray-800'}`}>
                {displayName}
              </span>

              {/* Stat value */}
              <span className="text-sm font-mono font-bold text-[var(--color-accent)]">
                +{cs.total_value}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
