import { useState } from 'react';
import { useHifStore, MAX_HIF_SCHEDULE_PRESETS } from '../../stores/hifStore';

/**
 * HIFスケジュール調整のプリセット保存・読み込みUI。
 * 他のプリセット欄 (イベント回数・持ち込みメモリー等) と同じボタン一覧形式。
 * - ボタンで既存プリセットを選択 → ロード
 * - テキストボックスで新規プリセット名入力 → 保存 (上限到達時は保存ボタンを無効化)
 * - 削除ボタンで選択中プリセット削除 (確認ダイアログあり)
 */
export default function HifSchedulePresets() {
  const presets = useHifStore((s) => s.schedulePresets);
  const savePreset = useHifStore((s) => s.saveSchedulePreset);
  const loadPreset = useHifStore((s) => s.loadSchedulePreset);
  const deletePreset = useHifStore((s) => s.deleteSchedulePreset);

  const [selectedName, setSelectedName] = useState<string>('');
  const [newName, setNewName] = useState<string>('');

  const trimmedNewName = newName.trim();
  const atLimit =
    presets.length >= MAX_HIF_SCHEDULE_PRESETS && !presets.some((p) => p.name === trimmedNewName);

  const handleSave = () => {
    if (!trimmedNewName || atLimit) return;
    savePreset(trimmedNewName);
    setSelectedName(trimmedNewName);
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
      <div className="flex items-center gap-2 text-xs text-gray-600">
        <span className="font-semibold">マイプリセット</span>
        <span className="text-gray-400">
          {presets.length}/{MAX_HIF_SCHEDULE_PRESETS}
        </span>
      </div>

      {/* 呼び出し行: ボタンリスト + 削除（同じボタンを再クリックしても都度ロードされる） */}
      <div className="flex items-start gap-2">
        <div className="flex-1 flex flex-wrap gap-1 min-h-[28px] items-center">
          {presets.length === 0 ? (
            <span className="text-xs text-gray-400">プリセット未登録</span>
          ) : (
            presets.map((p) => {
              const isSelected = selectedName === p.name;
              return (
                <button
                  key={p.name}
                  type="button"
                  onClick={() => {
                    setSelectedName(p.name);
                    setNewName(p.name);
                    loadPreset(p.name);
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
          disabled={!selectedName || !presets.some((p) => p.name === selectedName)}
          onClick={handleDelete}
          className="text-xs border border-gray-300 rounded px-2 py-1 text-red-600 hover:bg-red-50 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
        >
          削除
        </button>
      </div>

      {/* 保存行: 名前入力 + 保存 */}
      <div className="flex items-center gap-2">
        <input
          type="text"
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          placeholder="プリセット名"
          className="flex-1 border border-gray-300 rounded px-2 py-1 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
        />
        <button
          type="button"
          disabled={!trimmedNewName || atLimit}
          onClick={handleSave}
          className="text-xs border border-[var(--color-accent)] rounded px-2 py-1 text-[var(--color-accent)] hover:bg-[var(--color-accent)] hover:text-white disabled:border-gray-300 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
          title={
            atLimit
              ? `上限 ${MAX_HIF_SCHEDULE_PRESETS} 件に達しています`
              : '現在のスケジュールをこの名前で保存'
          }
        >
          保存
        </button>
      </div>
    </div>
  );
}
