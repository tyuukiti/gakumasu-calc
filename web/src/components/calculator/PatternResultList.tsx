import { useCalcStore } from '../../stores/calcStore';

export default function PatternResultList() {
  const deckResults = useCalcStore((s) => s.deckResults);
  const selectedPatternIndex = useCalcStore((s) => s.selectedPatternIndex);
  const selectPattern = useCalcStore((s) => s.selectPattern);

  if (deckResults.length === 0) return null;

  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-gray-700">編成パターン</h3>
      <div className="flex flex-wrap gap-2">
        {deckResults.map((result, index) => {
          const isSelected = index === selectedPatternIndex;
          return (
            <button
              key={index}
              type="button"
              className={`px-3 py-2 rounded-md text-sm border transition-colors ${
                isSelected
                  ? 'bg-[var(--color-accent)] text-white border-[var(--color-accent)]'
                  : 'bg-white text-gray-700 border-gray-300 hover:border-[var(--color-accent)] hover:text-[var(--color-accent)]'
              }`}
              onClick={() => selectPattern(index)}
            >
              <div className="font-medium">{result.label}</div>
              <div className={`text-xs ${isSelected ? 'text-white/80' : 'text-gray-500'}`}>
                合計: {result.total_value}
              </div>
            </button>
          );
        })}
      </div>
    </div>
  );
}
