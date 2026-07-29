import { useState } from 'react';
import { useHifStore, MAX_HIF_CONDITION_PRESETS } from '../../stores/hifStore';

/**
 * 全体プリセット（HIFタブの入力条件一式の保存・読み込み）。
 * 持ち込みメモリー (MemoryBonusInput) と同じ折りたたみ + プリセット管理のUIパターン。
 * - 保存対象: スケジュール・試験配分・一括設定 + calcStore側の入力条件（キャラ選択含む）
 * - 含めない: 凸トグル・HIFボーナスLv・MAX超過再抽選（別途永続化されるアカウント状態）
 * - 読込ボタンは読込と同時に計算を実行する（比較ワークフローを1クリックにするため）
 */
export default function HifConditionPresets() {
  const conditionPresets = useHifStore((s) => s.conditionPresets);
  const saveConditionPreset = useHifStore((s) => s.saveConditionPreset);
  const loadConditionPreset = useHifStore((s) => s.loadConditionPreset);
  const deleteConditionPreset = useHifStore((s) => s.deleteConditionPreset);
  const executeCalculate = useHifStore((s) => s.executeCalculate);
  const [isOpen, setIsOpen] = useState(false);
  const [selectedPresetName, setSelectedPresetName] = useState<string>('');
  const [newPresetName, setNewPresetName] = useState<string>('');

  return (
    <div className="border-t border-gray-100 pt-3">
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between text-sm font-semibold text-gray-700 hover:text-gray-900 cursor-pointer"
      >
        <span>
          全体プリセット
          <span className="text-xs text-gray-400 ml-1">（入力一式・任意）</span>
          {selectedPresetName && (
            <span className="text-xs text-gray-600 ml-2">: {selectedPresetName}</span>
          )}
        </span>
        <span
          className={`text-xs text-gray-400 transition-transform ${isOpen ? 'rotate-90' : ''}`}
        >
          &#9654;
        </span>
      </button>

      {isOpen && (
        <div className="mt-3 space-y-2">
          <div className="flex items-center gap-2 text-xs text-gray-600">
            <span className="font-semibold">プリセット</span>
            <span className="text-gray-400">{conditionPresets.length}/{MAX_HIF_CONDITION_PRESETS}</span>
          </div>

          {/* 呼び出し行: ボタンリスト + 削除（同じボタンを再クリックしても都度ロードされる） */}
          <div className="flex items-start gap-2">
            <div className="flex-1 flex flex-wrap gap-1 min-h-[28px] items-center">
              {conditionPresets.length === 0 ? (
                <span className="text-xs text-gray-400">プリセット未登録</span>
              ) : (
                conditionPresets.map((p) => {
                  const isSelected = selectedPresetName === p.name;
                  return (
                    <button
                      key={p.name}
                      type="button"
                      onClick={() => {
                        setSelectedPresetName(p.name);
                        setNewPresetName(p.name);
                        loadConditionPreset(p.name);
                        // 読込と同時に計算実行（比較ワークフローを1クリックで回す）
                        executeCalculate();
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
                !conditionPresets.some((p) => p.name === selectedPresetName)
              }
              onClick={() => {
                if (!selectedPresetName) return;
                deleteConditionPreset(selectedPresetName);
                setSelectedPresetName('');
              }}
              className="text-xs border border-gray-300 rounded px-2 py-1 text-red-600 hover:bg-red-50 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
            >
              削除
            </button>
          </div>

          {/* 保存行: 名前入力 + 保存 */}
          <div className="flex items-center gap-2">
            <input
              type="text"
              value={newPresetName}
              onChange={(e) => setNewPresetName(e.target.value)}
              placeholder="プリセット名（同名があれば上書き）"
              className="flex-1 border border-gray-300 rounded px-2 py-1 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
            />
            <button
              type="button"
              disabled={
                !newPresetName.trim() ||
                (conditionPresets.length >= MAX_HIF_CONDITION_PRESETS &&
                  !conditionPresets.some((p) => p.name === newPresetName.trim()))
              }
              onClick={() => {
                const name = newPresetName.trim();
                if (!name) return;
                saveConditionPreset(name);
                setSelectedPresetName(name);
                setNewPresetName('');
              }}
              className="text-xs border border-[var(--color-accent)] rounded px-2 py-1 text-[var(--color-accent)] hover:bg-[var(--color-accent)] hover:text-white disabled:border-gray-300 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
              title={
                conditionPresets.length >= MAX_HIF_CONDITION_PRESETS &&
                !conditionPresets.some((p) => p.name === newPresetName.trim())
                  ? `上限 ${MAX_HIF_CONDITION_PRESETS} 件に達しています`
                  : '現在の入力条件一式をこの名前で保存'
              }
            >
              保存
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
