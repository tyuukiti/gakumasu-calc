import { useCalcStore } from '../../stores/calcStore';

type Stat = 'vo' | 'da' | 'vi';
const STATS: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
const STAT_COLOR_VAR: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};

/**
 * 日程方式 (初レジェンド / NIA) の一括設定。
 * - レッスン: 属性を1つ選んで全レッスン週に一括適用（メイン1/2の概念は廃止。個別調整で各週を微修正）
 * - 授業: 属性を選んで全授業週に一括適用
 * HIF と違い試験配分は無い。
 */
export default function ScheduleBulkSettings({ planId }: { planId: string }) {
  const lessonStat = useCalcStore((s) => s.scheduleBulkLessonStat);
  const classStat = useCalcStore((s) => s.scheduleBulkClassStat);
  const setLessonStat = useCalcStore((s) => s.setScheduleBulkLessonStat);
  const setClassStat = useCalcStore((s) => s.setScheduleBulkClassStat);
  const applyLesson = useCalcStore((s) => s.applyScheduleBulkLesson);
  const applyClass = useCalcStore((s) => s.applyScheduleBulkClass);

  return (
    <div className="space-y-3">
      <label className="text-sm font-semibold text-gray-700">一括設定</label>

      {/* レッスンの一括適用（全レッスン週に単一属性） */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-gray-700 shrink-0">レッスン:</span>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">属性</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={lessonStat}
              onChange={(e) => setLessonStat(e.target.value as Stat)}
            >
              {STATS.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          onClick={() => applyLesson(planId)}
          className="w-full sm:w-auto sm:ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 cursor-pointer"
        >
          全レッスンに適用
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
              value={classStat}
              onChange={(e) => setClassStat(e.target.value as Stat)}
            >
              {STATS.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          onClick={() => applyClass(planId)}
          className="w-full sm:w-auto sm:ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 cursor-pointer"
        >
          全授業に適用
        </button>
      </div>
    </div>
  );
}
