import { useState } from 'react';
import { useCalcStore, MAX_SCHEDULE_PRESETS } from '../../stores/calcStore';

/**
 * 日程方式 (初レジェンド / NIA) のプリセット保存・読み込みUI。プランごとに独立。
 */
export default function SchedulePresets({ planId }: { planId: string }) {
  const presetsByPlan = useCalcStore((s) => s.schedulePresetsByPlan);
  const savePreset = useCalcStore((s) => s.saveSchedulePreset);
  const loadPreset = useCalcStore((s) => s.loadSchedulePreset);
  const deletePreset = useCalcStore((s) => s.deleteSchedulePreset);

  const presets = presetsByPlan[planId] ?? [];

  const [selectedName, setSelectedName] = useState<string>('');
  const [newName, setNewName] = useState<string>('');

  const handleLoad = (name: string) => {
    setSelectedName(name);
    if (name) {
      setNewName(name);
      loadPreset(planId, name);
    }
  };

  const handleSave = () => {
    const name = newName.trim();
    if (!name) return;
    if (presets.length >= MAX_SCHEDULE_PRESETS && !presets.some((p) => p.name === name)) {
      alert(`プリセットは最大${MAX_SCHEDULE_PRESETS}件まで保存できます`);
      return;
    }
    savePreset(planId, name);
    setNewName('');
  };

  const handleDelete = () => {
    if (!selectedName) return;
    if (!confirm(`プリセット「${selectedName}」を削除しますか？`)) return;
    deletePreset(planId, selectedName);
    setSelectedName('');
  };

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2 text-sm">
        <label className="text-gray-700 font-semibold shrink-0">プリセット</label>
        <span className="text-xs text-gray-500">
          {presets.length}/{MAX_SCHEDULE_PRESETS}
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
