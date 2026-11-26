import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useHifStore, MAX_HIF_POUCH_PRESETS } from '../../stores/hifStore';
import {
  HIF_POUCH_TRIGGER_LABELS,
  describeHifPouchSelection,
  formatHifPouchCountSummary,
  formatHifPouchEffects,
} from '../../services/hifPouch';
import { hifDayLabel } from './hifDayLabel';
import { useHifPouchCounts } from './useHifPouchCounts';
import Accordion from '../Accordion';

const STAGE_NAMES = ['未カスタマイズ', '1段階目', '2段階目'];

const SELECT_CLASS =
  'border border-gray-300 rounded-md px-2 py-1 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-accent)] focus:border-transparent';

/**
 * カスタムPアイテム (ポーチ) の選択UI。
 * - 色 → ぬいぐるみ → デコレーション の従属セレクト (親を変えると子はリセット)
 * - 段階別プレビュー: 発動契機・効果・この日程での発動日と回数 (発動 0 回は注意色)
 * - 合計行: イベント回数へ自動加算される回数 (手入力と二重に数えない)
 * - プリセット (他セクションと同じ保存・読込・削除。初期値として Wiki の 3 ルートが入っている)
 * 定義 (Data/Hif/pouch.yaml) が読めていなければ何も表示しない。
 */
export default function HifPouchConfig() {
  const def = useAppStore((s) => s.hifPouch);
  const pouch = useHifStore((s) => s.pouch);
  const setPouchSelection = useHifStore((s) => s.setPouchSelection);
  const presets = useHifStore((s) => s.pouchPresets);
  const savePreset = useHifStore((s) => s.savePouchPreset);
  const loadPreset = useHifStore((s) => s.loadPouchPreset);
  const deletePreset = useHifStore((s) => s.deletePouchPreset);
  const result = useHifPouchCounts();
  const [expanded, setExpanded] = useState(false);
  const [selectedName, setSelectedName] = useState('');
  const [newName, setNewName] = useState('');

  if (!def || def.colors.length === 0) return null;

  const color = def.colors.find((c) => c.id === pouch.color) ?? null;
  const plush = color?.plushes.find((p) => p.id === pouch.plush) ?? null;
  const description = describeHifPouchSelection(def, pouch);

  const trimmedNewName = newName.trim();
  const atLimit =
    presets.length >= MAX_HIF_POUCH_PRESETS && !presets.some((p) => p.name === trimmedNewName);

  return (
    <Accordion
      open={expanded}
      onToggle={setExpanded}
      title="カスタムPアイテム（ポーチ）"
      note="（任意）"
      status={description ?? undefined}
      emptyStatus="未選択"
    >
      {expanded && (
        <div className="border border-gray-200 rounded-md bg-gray-50 p-3 space-y-3">
          {/* 色 → ぬいぐるみ → デコレーション */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
            <label className="flex items-center gap-2">
              <span className="text-gray-600">色</span>
              <select
                className={SELECT_CLASS}
                value={pouch.color ?? ''}
                onChange={(e) => setPouchSelection({ color: e.target.value || null })}
              >
                <option value="">未選択</option>
                {def.colors.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </label>
            <label className="flex items-center gap-2">
              <span className="text-gray-600">ぬいぐるみ</span>
              <select
                className={SELECT_CLASS}
                value={pouch.plush ?? ''}
                disabled={!color}
                onChange={(e) => setPouchSelection({ plush: e.target.value || null })}
              >
                <option value="">未選択</option>
                {(color?.plushes ?? []).map((p) => (
                  <option key={p.id} value={p.id}>{p.name}</option>
                ))}
              </select>
            </label>
            <label className="flex items-center gap-2">
              <span className="text-gray-600">デコレーション</span>
              <select
                className={SELECT_CLASS}
                value={pouch.deco ?? ''}
                disabled={!plush}
                onChange={(e) => setPouchSelection({ deco: e.target.value || null })}
              >
                <option value="">未選択</option>
                {(plush?.decorations ?? []).map((d) => (
                  <option key={d.id} value={d.id}>{d.name}</option>
                ))}
              </select>
            </label>
          </div>

          {/* 段階別プレビュー */}
          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-1 w-28">段階</th>
                <th className="py-1 w-32">発動契機</th>
                <th className="py-1">効果（1回あたり）</th>
                <th className="py-1 w-44">この日程での発動</th>
              </tr>
            </thead>
            <tbody>
              {STAGE_NAMES.map((name, s) => {
                const st = result.stages[s];
                if (!st) {
                  return (
                    <tr key={s} className="border-b border-gray-100 last:border-b-0 text-gray-400">
                      <td className="py-1.5">{name}</td>
                      <td className="py-1.5">—</td>
                      <td className="py-1.5">—</td>
                      <td className="py-1.5">—</td>
                    </tr>
                  );
                }
                const zero = st.fires === 0;
                return (
                  <tr
                    key={s}
                    className={`border-b border-gray-100 last:border-b-0 ${zero ? 'text-red-600' : 'text-gray-700'}`}
                    title={st.description || undefined}
                  >
                    <td className="py-1.5">{st.label}</td>
                    <td className="py-1.5">
                      {HIF_POUCH_TRIGGER_LABELS[st.trigger]} ×{st.maxCount}
                    </td>
                    <td className="py-1.5">{formatHifPouchEffects(st.effects)}</td>
                    <td className="py-1.5 font-mono">
                      {zero
                        ? '発動なし'
                        : `${st.firedWeeks.map(hifDayLabel).join(', ')} → ${st.fires}回`}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          <div className="text-xs text-gray-600">
            {pouch.color
              ? `イベント回数へ自動加算: ${formatHifPouchCountSummary(result.counts)}`
              : 'ポーチ未選択（イベント回数への加算なし）'}
          </div>

          {/* マイプリセット (イベント回数欄と同じボタン一覧形式。初期値として Wiki の 3 ルートが入っている) */}
          <div className="border border-gray-200 rounded-md bg-white p-3 space-y-2">
            <div className="flex items-center gap-2 text-xs text-gray-600">
              <span className="font-semibold">マイプリセット</span>
              <span className="text-gray-400">
                {presets.length}/{MAX_HIF_POUCH_PRESETS}
              </span>
            </div>

            {/* 呼び出し行: ボタンリスト + 削除 */}
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
                onClick={() => {
                  if (!selectedName) return;
                  deletePreset(selectedName);
                  setSelectedName('');
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
                value={newName}
                onChange={(e) => setNewName(e.target.value)}
                placeholder="プリセット名"
                className="flex-1 border border-gray-300 rounded px-2 py-1 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
              />
              <button
                type="button"
                disabled={!trimmedNewName || atLimit}
                onClick={() => {
                  if (!trimmedNewName) return;
                  savePreset(trimmedNewName);
                  setSelectedName(trimmedNewName);
                  setNewName('');
                }}
                className="text-xs border border-[var(--color-accent)] rounded px-2 py-1 text-[var(--color-accent)] hover:bg-[var(--color-accent)] hover:text-white disabled:border-gray-300 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
                title={
                  atLimit
                    ? `上限 ${MAX_HIF_POUCH_PRESETS} 件に達しています`
                    : '現在のポーチ選択をこの名前で保存'
                }
              >
                保存
              </button>
            </div>
          </div>
        </div>
      )}
    </Accordion>
  );
}
