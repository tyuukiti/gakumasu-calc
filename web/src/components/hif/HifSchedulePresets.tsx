import { useState } from 'react';
import { useHifStore, MAX_HIF_SCHEDULE_PRESETS } from '../../stores/hifStore';

/**
 * HIFスケジュール調整のプリセット保存・読み込みUI。
 * - ドロップダウンで既存プリセットを選択 → ロード
 * - テキストボックスで新規プリセット名入力 → 保存
 * - 削除ボタンで選択中プリセット削除
 */
export default function HifSchedulePresets() {
  const presets = useHifStore((s) => s.schedulePresets);
  const savePreset = useHifStore((s) => s.saveSchedulePreset);
  const loadPreset = useHifStore((s) => s.loadSchedulePreset);
  const deletePreset = useHifStore((s) => s.deleteSchedulePreset);

  const [selectedName, setSelectedName] = useState<string>('');
  const [newName, setNewName] = useState<string>('');

  const handleLoad = (name: string) => {
    setSelectedName(name);
    if (name) {
      setNewName(name);
      loadPreset(name);
    }
  };

  const handleSave = () => {
    const name = newName.trim();
    if (!name) return;
    if (presets.length >= MAX_HIF_SCHEDULE_PRESETS && !presets.some((p) => p.name === name)) {
      alert(`プリセットは最大${MAX_HIF_SCHEDULE_PRESETS}件まで保存できます`);
      return;
    }
    savePreset(name);
    setNewName('');
  };

  const handleDelete = () => {
    if (!selectedName) return;
    if (!confirm(`プリセット「${selectedName}」を削除しますか？`)) return;
    deletePreset(selectedName);
    setSelectedName('');
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2 text-sm">
        <label className="text-gray-700 font-semibold shrink-0">プリセット</label>
        <span className="text-xs text-gray-500">
          {presets.length}/{MAX_HIF_SCHEDULE_PRESETS}
        </span>
      </div>

      {/* 既存プリセット読み込み・削除 */}
      <div className="flex items-center gap-2">
        <select
          className="flex-1 border border-gray-300 rounded px-2 py-1 text-sm bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
          value={selectedName}
          onChange={(e) => handleLoad(e.target.value)}
        >
          <option value="">-- プリセットを選択して読み込み --</option>
          {presets.map((p) => (
            <option key={p.name} value={p.name}>{p.name}</option>
          ))}
        </select>
        <button
          type="button"
          onClick={handleDelete}
          disabled={!selectedName}
          className="px-3 py-1 bg-red-50 text-red-700 border border-red-300 rounded text-xs font-semibold cursor-pointer hover:opacity-80 disabled:opacity-40 disabled:cursor-not-allowed"
        >
          削除
        </button>
      </div>

      {/* 新規保存 */}
      <div className="flex items-center gap-2">
        <input
          type="text"
          placeholder="保存名を入力（同名があれば上書き）"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          className="flex-1 border border-gray-300 rounded px-2 py-1 text-sm bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
        />
        <button
          type="button"
          onClick={handleSave}
          disabled={!newName.trim()}
          className="px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold cursor-pointer hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed"
        >
          現在のスケジュールを保存
        </button>
      </div>
    </div>
  );
}
