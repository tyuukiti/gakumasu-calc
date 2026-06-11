import type { ExamAllocationPreset } from '../../stores/hifStore';
import { useHifStore } from '../../stores/hifStore';

type Stat = 'vo' | 'da' | 'vi';
const STATS: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };

/** ドット等に使用する、属性ごとのCSS変数マッピング */
const STAT_COLOR_VAR: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};

/** 2分割プリセット */
const SPLIT_PRESETS: Array<{ preset: ExamAllocationPreset; a: Stat; b: Stat }> = [
  { preset: 'vo_da', a: 'vo', b: 'da' },
  { preset: 'da_vi', a: 'da', b: 'vi' },
  { preset: 'vo_vi', a: 'vo', b: 'vi' },
];

export default function HifBulkSettings() {
  const bulk = useHifStore((s) => s.bulkLessonDefault);
  const setBulk = useHifStore((s) => s.setBulkLessonDefault);
  const bulkClass = useHifStore((s) => s.bulkClassStat);
  const setBulkClass = useHifStore((s) => s.setBulkClassStat);
  const applyBulk = useHifStore((s) => s.applyBulkLessonChoice);
  const applyClass = useHifStore((s) => s.applyBulkClassChoice);
  const applyExam = useHifStore((s) => s.applyExamAllocationPreset);

  const subOptions = STATS.filter((s) => s !== bulk.mainStat);
  const validBulk = bulk.mainStat !== bulk.subStat;

  // 共通のボタン基本スタイル
  const btnBaseClass = 
    "flex items-center justify-center gap-1.5 min-w-[88px] px-2.5 py-1 text-xs font-semibold " +
    "bg-white border border-gray-200 rounded text-gray-700 shadow-sm transition-all " +
    "hover:bg-gray-50 hover:border-gray-300 hover:text-gray-900 active:scale-95 cursor-pointer";

  return (
    <div className="space-y-3">
      <label className="text-sm font-semibold text-gray-700">一括設定</label>

      {/* 公開レッスンの一括適用 */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-gray-700 shrink-0">公開レッスン:</span>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">メイン</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={bulk.mainStat}
              onChange={(e) => {
                const m = e.target.value as Stat;
                const s = m === bulk.subStat ? STATS.find((x) => x !== m)! : bulk.subStat;
                setBulk({ mainStat: m, subStat: s });
              }}
            >
              {STATS.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">サブ</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={bulk.subStat}
              onChange={(e) => setBulk({ mainStat: bulk.mainStat, subStat: e.target.value as Stat })}
            >
              {subOptions.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          onClick={applyBulk}
          disabled={!validBulk}
          className="w-full sm:w-auto sm:ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          全公開レッスンに適用
        </button>
      </div>

      {/* 授業の一括適用 */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-gray-700 shrink-0">授業:</span>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">属性</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={bulkClass}
              onChange={(e) => setBulkClass(e.target.value as Stat)}
            >
              {STATS.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          onClick={applyClass}
          className="w-full sm:w-auto sm:ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 cursor-pointer"
        >
          全授業に適用
        </button>
      </div>

      {/* 試験配分プリセット */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <span className="text-gray-700 shrink-0">試験配分:</span>
        
        <div className="flex items-center gap-2 flex-wrap">
          {/* --- 1極グループ --- */}
          <div className="flex gap-1.5">
            {STATS.map((s) => (
              <button
                key={s}
                type="button"
                onClick={() => applyExam(`${s}_all` as ExamAllocationPreset)}
                className={btnBaseClass}
              >
                {/* 属性を示すカラーマッカードット */}
                <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: STAT_COLOR_VAR[s] }} />
                {STAT_LABEL[s]} 全振り
              </button>
            ))}
          </div>

          {/* 境界線 */}
          <div className="hidden sm:block h-5 w-px bg-gray-300 mx-0.5" />

          {/* --- 2極グループ --- */}
          <div className="flex gap-1.5">
            {SPLIT_PRESETS.map(({ preset, a, b }) => (
              <button
                key={preset}
                type="button"
                onClick={() => applyExam(preset)}
                title={`${STAT_LABEL[a]}・${STAT_LABEL[b]} 2極化`}
                className={btnBaseClass}
              >
                {/* 2色並んだツインドットで2極を表現 */}
                <div className="flex gap-0.5 shrink-0">
                  <span className="w-1.5 h-3 rounded-l-sm" style={{ backgroundColor: STAT_COLOR_VAR[a] }} />
                  <span className="w-1.5 h-3 rounded-r-sm" style={{ backgroundColor: STAT_COLOR_VAR[b] }} />
                </div>
                <span>{STAT_LABEL[a]}{STAT_LABEL[b]} 2極</span>
              </button>
            ))}
          </div>

          {/* 境界線 */}
          <div className="hidden sm:block h-5 w-px bg-gray-300 mx-0.5" />

          {/* --- 3分割グループ --- */}
          <button
            type="button"
            onClick={() => applyExam('equal')}
            className={btnBaseClass}
          >
            <div className="flex gap-0.5 shrink-0 opacity-40">
              <span className="w-1 h-3 rounded-sm bg-gray-500" />
              <span className="w-1 h-3 rounded-sm bg-gray-500" />
              <span className="w-1 h-3 rounded-sm bg-gray-500" />
            </div>
            均等 3分割
          </button>
        </div>
      </div>
    </div>
  );
}