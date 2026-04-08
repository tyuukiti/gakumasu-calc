import { useState, useRef, useEffect } from 'react';
import { useAppStore } from '../../stores/appStore';
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

export default function RequiredCardSelector() {
  const allCards = useAppStore((s) => s.cards);
  const requiredCardIds = useCalcStore((s) => s.requiredCardIds);
  const addRequiredCard = useCalcStore((s) => s.addRequiredCard);
  const removeRequiredCard = useCalcStore((s) => s.removeRequiredCard);
  const selectedPlanType = useCalcStore((s) => s.selectedPlanType);

  const [searchText, setSearchText] = useState('');
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // プランタイプでフィルタし、既に選択済みのカードを除外
  const filteredCards = allCards.filter((c) => {
    if (requiredCardIds.includes(c.id)) return false;
    if (selectedPlanType && c.plan && c.plan !== selectedPlanType && c.plan !== 'free') return false;
    if (searchText && !c.name.includes(searchText)) return false;
    return true;
  });

  const selectedCards = requiredCardIds
    .map((id) => allCards.find((c) => c.id === id))
    .filter((c) => c != null);

  // 外部クリックでドロップダウンを閉じる
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const canAdd = requiredCardIds.length < 4;

  return (
    <div>
      <h4 className="text-sm font-semibold text-gray-700 mb-2">
        必須カード ({requiredCardIds.length}/4)
      </h4>

      {/* 検索 + ドロップダウン */}
      <div ref={containerRef} className="relative mb-2">
        <input
          type="text"
          value={searchText}
          onChange={(e) => {
            setSearchText(e.target.value);
            setIsOpen(true);
          }}
          onFocus={() => setIsOpen(true)}
          placeholder="カード名で検索..."
          disabled={!canAdd}
          className="w-full max-w-sm px-3 py-2 border border-gray-300 rounded text-sm focus:outline-none focus:border-[var(--color-accent)] disabled:bg-gray-100 disabled:text-gray-400"
        />
        {isOpen && canAdd && filteredCards.length > 0 && (
          <div className="absolute z-10 w-full max-w-sm mt-1 bg-white border border-gray-200 rounded shadow-lg max-h-48 overflow-y-auto">
            {filteredCards.slice(0, 50).map((card) => {
              const typeStyle = TYPE_COLORS[card.type] ?? TYPE_COLORS['all'];
              return (
                <button
                  key={card.id}
                  type="button"
                  className="w-full flex items-center gap-2 px-3 py-1.5 hover:bg-gray-50 text-left cursor-pointer"
                  onClick={() => {
                    addRequiredCard(card.id);
                    setSearchText('');
                    setIsOpen(false);
                  }}
                >
                  <span
                    className="text-xs font-bold px-1 py-0.5 rounded"
                    style={{ backgroundColor: typeStyle.bg, color: typeStyle.text }}
                  >
                    {TYPE_LABELS[card.type] ?? card.type}
                  </span>
                  <span className="text-xs text-gray-500">{card.rarity}</span>
                  <span className="text-sm text-gray-800 truncate">{card.name}</span>
                </button>
              );
            })}
          </div>
        )}
      </div>

      {/* 選択済みカード一覧 */}
      {selectedCards.length > 0 && (
        <div className="space-y-1">
          {selectedCards.map((card) => {
            const typeStyle = TYPE_COLORS[card.type] ?? TYPE_COLORS['all'];
            return (
              <div
                key={card.id}
                className="flex items-center gap-2 rounded-md px-3 py-1.5 bg-purple-50 border border-purple-200"
              >
                <span
                  className="text-xs font-bold px-1 py-0.5 rounded"
                  style={{ backgroundColor: typeStyle.bg, color: typeStyle.text }}
                >
                  {TYPE_LABELS[card.type] ?? card.type}
                </span>
                <span className="text-xs text-gray-500">{card.rarity}</span>
                <span className="flex-1 text-sm text-gray-800 truncate">{card.name}</span>
                <button
                  type="button"
                  className="text-gray-400 hover:text-red-500 text-xs cursor-pointer"
                  onClick={() => removeRequiredCard(card.id)}
                >
                  ✕
                </button>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
