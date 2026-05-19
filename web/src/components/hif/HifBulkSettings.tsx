import { useHifStore } from '../../stores/hifStore';

type Stat = 'vo' | 'da' | 'vi';
const STATS: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
/** 白背景に出すテキスト用のダーク版カラー */
const STAT_TEXT: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};
/** 試験プリセットボタンの背景色 */
const STAT_BG: Record<Stat, string> = {
  vo: 'var(--color-vo-bg)',
  da: 'var(--color-da-bg)',
  vi: 'var(--color-vi-bg)',
};

/**
 * HIF用 一括設定セクション。
 * - 公開レッスンのデフォルトメイン/サブを設定 → 「全公開レッスンに適用」ボタンで一気に反映
 * - 試験配分のプリセットボタン (Vo全振り / Da全振り / Vi全振り / 均等) で全試験に一括適用
 */
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

  return (
    <div className="space-y-3">
      <label className="text-sm font-semibold text-gray-700">一括設定</label>

      {/* 公開レッスンの一括適用 */}
      <div className="flex items-center gap-2 flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
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
              <option key={s} value={s} style={{ color: STAT_TEXT[s] }}>{STAT_LABEL[s]}</option>
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
              <option key={s} value={s} style={{ color: STAT_TEXT[s] }}>{STAT_LABEL[s]}</option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={applyBulk}
          disabled={!validBulk}
          className="ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
        >
          全公開レッスンに適用
        </button>
      </div>

      {/* 授業の一括適用 */}
      <div className="flex items-center gap-2 flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <span className="text-gray-700 shrink-0">授業:</span>
        <label className="flex items-center gap-1">
          <span className="text-xs text-gray-600">属性</span>
          <select
            className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
            value={bulkClass}
            onChange={(e) => setBulkClass(e.target.value as Stat)}
          >
            {STATS.map((s) => (
              <option key={s} value={s} style={{ color: STAT_TEXT[s] }}>{STAT_LABEL[s]}</option>
            ))}
          </select>
        </label>
        <button
          type="button"
          onClick={applyClass}
          className="ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 cursor-pointer"
        >
          全授業に適用
        </button>
      </div>

      {/* 試験配分プリセット */}
      <div className="flex items-center gap-2 flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <span className="text-gray-700 shrink-0">試験配分:</span>
        <button
          type="button"
          onClick={() => applyExam('vo_all')}
          className="px-2 py-1 border rounded text-xs font-semibold cursor-pointer hover:opacity-80"
          style={{ background: STAT_BG.vo, color: STAT_TEXT.vo, borderColor: STAT_TEXT.vo }}
        >
          Vo 全振り
        </button>
        <button
          type="button"
          onClick={() => applyExam('da_all')}
          className="px-2 py-1 border rounded text-xs font-semibold cursor-pointer hover:opacity-80"
          style={{ background: STAT_BG.da, color: STAT_TEXT.da, borderColor: STAT_TEXT.da }}
        >
          Da 全振り
        </button>
        <button
          type="button"
          onClick={() => applyExam('vi_all')}
          className="px-2 py-1 border rounded text-xs font-semibold cursor-pointer hover:opacity-80"
          style={{ background: STAT_BG.vi, color: STAT_TEXT.vi, borderColor: STAT_TEXT.vi }}
        >
          Vi 全振り
        </button>
        <button
          type="button"
          onClick={() => applyExam('equal')}
          className="px-2 py-1 bg-white border border-gray-300 rounded text-xs text-gray-700 hover:border-[var(--color-accent)] hover:text-[var(--color-accent)] cursor-pointer"
        >
          均等3分割
        </button>
      </div>
    </div>
  );
}
