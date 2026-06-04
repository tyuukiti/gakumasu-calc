import { useEffect, useState } from 'react';
import { useCalcStore, MAX_MEMORY_PRESETS } from '../../stores/calcStore';
import type { MemoryBonusType } from '../../types/models';

const STAT_LABELS: { key: 'vo' | 'da' | 'vi'; label: string; color: string }[] = [
  { key: 'vo', label: 'Vocal', color: 'var(--color-vo, #FF6B8A)' },
  { key: 'da', label: 'Dance', color: 'var(--color-da, #6B9FFF)' },
  { key: 'vi', label: 'Visual', color: 'var(--color-vi, #FFD36B)' },
];

/**
 * 持ち込みメモリー入力欄（最大4枚・セッション限定）。
 * 各メモリーは Vo/Da/Vi 各属性に「実数値加算」または「パラボ%」のいずれかを1値持つ。
 */
export default function MemoryBonusInput() {
  const memoryBonuses = useCalcStore((s) => s.memoryBonuses);
  const setMemoryBonus = useCalcStore((s) => s.setMemoryBonus);
  const clearMemoryBonuses = useCalcStore((s) => s.clearMemoryBonuses);
  const memoryPresets = useCalcStore((s) => s.memoryPresets);
  const saveMemoryPreset = useCalcStore((s) => s.saveMemoryPreset);
  const loadMemoryPreset = useCalcStore((s) => s.loadMemoryPreset);
  const deleteMemoryPreset = useCalcStore((s) => s.deleteMemoryPreset);
  const [isOpen, setIsOpen] = useState(false);
  const [selectedPresetName, setSelectedPresetName] = useState<string>('');
  const [newPresetName, setNewPresetName] = useState<string>('');

  // 表示用 draft（小数点の中間状態 "2." や空文字を保持するため、store の数値と分離）
  // store が外部から書き換わったとき (clearなど) はここを同期する
  const buildDraft = () =>
    memoryBonuses.map((m) => ({
      vo: m.vo.value === 0 ? '' : String(m.vo.value),
      da: m.da.value === 0 ? '' : String(m.da.value),
      vi: m.vi.value === 0 ? '' : String(m.vi.value),
    }));
  const [drafts, setDrafts] = useState<{ vo: string; da: string; vi: string }[]>(buildDraft);
  useEffect(() => {
    setDrafts(buildDraft());
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [memoryBonuses]);

  const hasAny = memoryBonuses.some(
    (m) => m.vo.value !== 0 || m.da.value !== 0 || m.vi.value !== 0,
  );

  const onValueInput = (idx: number, key: 'vo' | 'da' | 'vi', raw: string) => {
    // ローカル表示は raw を保持（"2." や "-" などの中間状態を許可）
    setDrafts((prev) => {
      const next = prev.map((d, i) => (i === idx ? { ...d } : d));
      next[idx][key] = raw;
      return next;
    });
    // store には parseFloat 可能なときだけ反映。空 or 中間状態は 0 として扱う
    const parsed = parseFloat(raw);
    setMemoryBonus(idx, key, { value: Number.isFinite(parsed) ? parsed : 0 });
  };

  return (
    <div className="border-t border-gray-100 pt-3">
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between text-sm font-semibold text-gray-700 hover:text-gray-900 cursor-pointer"
      >
        <span>
          持ち込みメモリー
          <span className="text-xs text-gray-400 ml-1">（最大4枚・任意）</span>
          {hasAny && <span className="text-xs text-gray-600 ml-2">: 設定済み</span>}
        </span>
        <span
          className={`text-xs text-gray-400 transition-transform ${isOpen ? 'rotate-90' : ''}`}
        >
          &#9654;
        </span>
      </button>

      {isOpen && (
        <div className="mt-3">
          {/* 列ヘッダ */}
          <div className="grid grid-cols-[28px_1fr_1fr_1fr] gap-2 mb-1 text-[11px] font-semibold">
            <span></span>
            {STAT_LABELS.map((s) => (
              <span key={s.key} className="text-center" style={{ color: s.color }}>
                {s.label}
              </span>
            ))}
          </div>

          {/* 4スロット */}
          <div className="space-y-1.5">
            {memoryBonuses.map((m, idx) => (
              <div
                key={idx}
                className="grid grid-cols-[28px_1fr_1fr_1fr] gap-2 items-center"
              >
                <span className="text-xs text-gray-500">{idx + 1}</span>
                {STAT_LABELS.map((s) => (
                  <div key={s.key} className="flex items-center gap-1 justify-center">
                    <input
                      type="number"
                      step="any"
                      value={drafts[idx]?.[s.key] ?? ''}
                      placeholder="0"
                      onChange={(e) => onValueInput(idx, s.key, e.target.value)}
                      className="w-14 border border-gray-300 rounded px-1 py-0.5 text-xs text-center bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                    />
                    <select
                      value={m[s.key].type}
                      onChange={(e) =>
                        setMemoryBonus(idx, s.key, {
                          type: e.target.value as MemoryBonusType,
                        })
                      }
                      className="border border-gray-300 rounded px-1 py-0.5 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                    >
                      <option value="flat">実</option>
                      <option value="para">%</option>
                    </select>
                  </div>
                ))}
              </div>
            ))}
          </div>

          {/* プリセット管理 */}
          <div className="border-t border-gray-100 mt-3 pt-3 space-y-2">
            <div className="flex items-center gap-2 text-xs text-gray-600">
              <span className="font-semibold">プリセット</span>
              <span className="text-gray-400">{memoryPresets.length}/{MAX_MEMORY_PRESETS}</span>
            </div>

            {/* 呼び出し行: ボタンリスト + 削除（同じボタンを再クリックしても都度ロードされる） */}
            <div className="flex items-start gap-2">
              <div className="flex-1 flex flex-wrap gap-1 min-h-[28px] items-center">
                {memoryPresets.length === 0 ? (
                  <span className="text-xs text-gray-400">プリセット未登録</span>
                ) : (
                  memoryPresets.map((p) => {
                    const isSelected = selectedPresetName === p.name;
                    return (
                      <button
                        key={p.name}
                        type="button"
                        onClick={() => {
                          setSelectedPresetName(p.name);
                          setNewPresetName(p.name);
                          loadMemoryPreset(p.name);
                        }}
                        className={`text-xs border rounded px-2 py-1 cursor-pointer transition-colors ${
                          isSelected
                            ? 'border-[var(--color-accent)] bg-[var(--color-accent)] text-white'
                            : 'border-gray-300 hover:bg-gray-50 text-gray-700'
                        }`}
                      >
                        {p.name}
                      </button>
                    );
                  })
                )}
              </div>
              <button
                type="button"
                disabled={
                  !selectedPresetName ||
                  !memoryPresets.some((p) => p.name === selectedPresetName)
                }
                onClick={() => {
                  if (!selectedPresetName) return;
                  deleteMemoryPreset(selectedPresetName);
                  setSelectedPresetName('');
                }}
                className="text-xs border border-gray-300 rounded px-2 py-1 text-red-600 hover:bg-red-50 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
              >
                削除
              </button>
            </div>

            {/* 保存行: 名前入力 + 保存 + 全クリア */}
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={newPresetName}
                onChange={(e) => setNewPresetName(e.target.value)}
                placeholder="プリセット名"
                className="flex-1 border border-gray-300 rounded px-2 py-1 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
              />
              <button
                type="button"
                disabled={
                  !newPresetName.trim() ||
                  (memoryPresets.length >= MAX_MEMORY_PRESETS &&
                    !memoryPresets.some((p) => p.name === newPresetName.trim()))
                }
                onClick={() => {
                  const name = newPresetName.trim();
                  if (!name) return;
                  saveMemoryPreset(name);
                  setSelectedPresetName(name);
                  setNewPresetName('');
                }}
                className="text-xs border border-[var(--color-accent)] rounded px-2 py-1 text-[var(--color-accent)] hover:bg-[var(--color-accent)] hover:text-white disabled:border-gray-300 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
                title={
                  memoryPresets.length >= MAX_MEMORY_PRESETS &&
                  !memoryPresets.some((p) => p.name === newPresetName.trim())
                    ? `上限 ${MAX_MEMORY_PRESETS} 件に達しています`
                    : '現在のメモリー値をこの名前で保存'
                }
              >
                保存
              </button>
              <button
                type="button"
                onClick={clearMemoryBonuses}
                className="text-xs text-gray-500 hover:text-gray-700 underline cursor-pointer"
              >
                クリア
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
