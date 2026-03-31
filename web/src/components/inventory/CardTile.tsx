import type { SupportCard } from '../../types/models';
import type { CardInventoryEntry } from '../../types/inventory';

const TYPE_COLORS: Record<string, string> = {
  vo: '#ffe0ec',
  da: '#e0eeff',
  vi: '#fff9e0',
  all: '#e8f5e9',
};

const PLAN_LABELS: Record<string, string> = {
  sense: 'センス',
  logic: 'ロジック',
  anomaly: 'アノマリー',
  free: 'フリー',
};

interface Props {
  card: SupportCard;
  entry: CardInventoryEntry;
  onToggleOwned: () => void;
  onSetUncap: (uncap: number) => void;
}

export default function CardTile({ card, entry, onToggleOwned, onSetUncap }: Props) {
  const baseColor = TYPE_COLORS[card.type] ?? '#f0f0f0';
  const bg = entry.owned ? baseColor : `color-mix(in srgb, ${baseColor} 40%, white)`;
  const borderColor = entry.owned ? '#4CAF50' : '#ddd';

  const typeDisplay = card.type === 'all' ? 'As' : card.type.charAt(0).toUpperCase() + card.type.slice(1);

  return (
    <div
      className="rounded-lg p-2.5 cursor-pointer select-none"
      style={{ backgroundColor: bg, border: `2px solid ${borderColor}` }}
      onClick={onToggleOwned}
    >
      <div className="text-xs font-medium text-gray-800 mb-1 leading-tight min-h-[2rem]" title={card.name}>
        <span className="line-clamp-2">{card.name}</span>
      </div>
      <div className="flex items-center gap-1.5 text-[11px] mb-1.5">
        <span className="font-bold text-gray-700 bg-white/60 rounded px-1">{card.rarity}</span>
        <span className="font-semibold text-gray-600">{typeDisplay}</span>
        <span className="text-gray-500">{PLAN_LABELS[card.plan] ?? ''}</span>
      </div>
      <div className="flex items-center gap-1.5" onClick={e => e.stopPropagation()}>
        <input type="checkbox" checked={entry.owned} onChange={onToggleOwned} className="w-4 h-4" />
        <span className="text-[11px] text-gray-600">凸:</span>
        <select
          value={entry.uncap}
          onChange={e => onSetUncap(Number(e.target.value))}
          className="text-[11px] border rounded px-1.5 py-0.5 bg-white"
        >
          {[0, 1, 2, 3, 4].map(n => <option key={n} value={n}>{n}</option>)}
        </select>
      </div>
    </div>
  );
}
