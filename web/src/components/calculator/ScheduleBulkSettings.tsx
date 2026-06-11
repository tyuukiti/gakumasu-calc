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
 * - レッスン配分: メイン1/メイン2 を選んで全レッスン週に一括適用 (中間前1:1・後2:1)
 * - 授業: 属性を選んで全授業週に一括適用
 * HIF と違い試験配分は無い。
 */
export default function ScheduleBulkSettings({ planId }: { planId: string }) {
  const main1 = useCalcStore((s) => s.scheduleBulkMain1);
  const main2 = useCalcStore((s) => s.scheduleBulkMain2);
  const classStat = useCalcStore((s) => s.scheduleBulkClassStat);
  const setMain1 = useCalcStore((s) => s.setScheduleBulkMain1);
  const setMain2 = useCalcStore((s) => s.setScheduleBulkMain2);
  const setClassStat = useCalcStore((s) => s.setScheduleBulkClassStat);
  const applyDist = useCalcStore((s) => s.applyScheduleBulkDistribution);
  const applyClass = useCalcStore((s) => s.applyScheduleBulkClass);

  const main2Options = STATS.filter((s) => s !== main1);
  const valid = main1 !== main2;

  return (
    <div className="space-y-3">
      <label className="text-sm font-semibold text-gray-700">一括設定</label>

      {/* レッスン配分の一括適用 */}
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:flex-wrap text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-gray-700 shrink-0">レッスン配分:</span>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">メイン1</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={main1}
              onChange={(e) => setMain1(e.target.value as Stat)}
            >
              {STATS.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
          <label className="flex items-center gap-1">
            <span className="text-xs text-gray-600">メイン2</span>
            <select
              className="border border-gray-300 rounded px-2 py-0.5 text-sm bg-white"
              value={main2}
              onChange={(e) => setMain2(e.target.value as Stat)}
            >
              {main2Options.map((s) => (
                <option key={s} value={s} style={{ color: STAT_COLOR_VAR[s] }}>{STAT_LABEL[s]}</option>
              ))}
            </select>
          </label>
        </div>
        <button
          type="button"
          onClick={() => applyDist(planId)}
          disabled={!valid}
          className="w-full sm:w-auto sm:ml-auto px-3 py-1 bg-[var(--color-accent)] text-white rounded text-xs font-bold hover:opacity-90 disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
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
