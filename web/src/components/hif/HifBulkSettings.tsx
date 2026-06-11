import { useEffect, useMemo } from 'react';
import type { ExamAllocationPreset } from '../../stores/hifStore';
import { useHifStore } from '../../stores/hifStore';
import { useAppStore } from '../../stores/appStore';
import ExamRatioBar from './ExamRatioBar';

type Stat = 'vo' | 'da' | 'vi';
const STATS: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };

/** ドット等に使用する、属性ごとのCSS変数マッピング */
const STAT_COLOR_VAR: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};

/** 2分割プリセット (VoDa → VoVi → DaVi の順) */
const SPLIT_PRESETS: Array<{ preset: ExamAllocationPreset; a: Stat; b: Stat }> = [
  { preset: 'vo_da', a: 'vo', b: 'da' },
  { preset: 'vo_vi', a: 'vo', b: 'vi' },
  { preset: 'da_vi', a: 'da', b: 'vi' },
];

export default function HifBulkSettings() {
  const bulk = useHifStore((s) => s.bulkLessonDefault);
  const setBulk = useHifStore((s) => s.setBulkLessonDefault);
  const bulkClass = useHifStore((s) => s.bulkClassStat);
  const setBulkClass = useHifStore((s) => s.setBulkClassStat);
  const applyBulk = useHifStore((s) => s.applyBulkLessonChoice);
  const applyClass = useHifStore((s) => s.applyBulkClassChoice);
  const applyExam = useHifStore((s) => s.applyExamAllocationPreset);
  const examRatio = useHifStore((s) => s.examRatio);
  const setExamRatio = useHifStore((s) => s.setExamRatio);
  const ensureExamAllocations = useHifStore((s) => s.ensureExamAllocations);
  const examAllocations = useHifStore((s) => s.examAllocations);
  const plans = useAppStore((s) => s.plans);

  // 初回表示時、試験配分が未設定ならデフォルト比率から materialize
  useEffect(() => {
    ensureExamAllocations();
  }, [ensureExamAllocations, plans]);

  // 試験日 (audition かつ配分プールあり) の一覧
  const examDays = useMemo(() => {
    const hifPlan = plans.find((p) => p.id === 'hif');
    if (!hifPlan) return [];
    return hifPlan.schedule
      .filter((w) => w.type === 'audition' && (w.hif_exam_distributed ?? 0) > 0)
      .map((w) => ({ week: w.week, name: w.event_name ?? `Day ${w.week}`, base: w.hif_exam_base ?? 0 }));
  }, [plans]);

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

      {/* 試験配分: バー1本で Vo/Da/Vi 比率を決め、全試験に同じ比率で按分 */}
      <div className="flex flex-col gap-3 text-sm bg-gray-50 border border-gray-200 rounded-md p-3">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-gray-700 shrink-0">試験配分:</span>

          {/* プリセット: バー比率を一発で設定 */}
          <div className="flex items-center gap-2 flex-wrap">
            <div className="flex gap-1.5">
              {STATS.map((s) => (
                <button
                  key={s}
                  type="button"
                  onClick={() => applyExam(`${s}_all` as ExamAllocationPreset)}
                  className={btnBaseClass}
                >
                  <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: STAT_COLOR_VAR[s] }} />
                  {STAT_LABEL[s]} 全振り
                </button>
              ))}
            </div>
            <div className="hidden sm:block h-5 w-px bg-gray-300 mx-0.5" />
            <div className="flex gap-1.5">
              {SPLIT_PRESETS.map(({ preset, a, b }) => (
                <button
                  key={preset}
                  type="button"
                  onClick={() => applyExam(preset)}
                  title={`${STAT_LABEL[a]}・${STAT_LABEL[b]} 2極化`}
                  className={btnBaseClass}
                >
                  <div className="flex gap-0.5 shrink-0">
                    <span className="w-1.5 h-3 rounded-l-sm" style={{ backgroundColor: STAT_COLOR_VAR[a] }} />
                    <span className="w-1.5 h-3 rounded-r-sm" style={{ backgroundColor: STAT_COLOR_VAR[b] }} />
                  </div>
                  <span>{STAT_LABEL[a]}{STAT_LABEL[b]} 2極</span>
                </button>
              ))}
            </div>
            <div className="hidden sm:block h-5 w-px bg-gray-300 mx-0.5" />
            <button type="button" onClick={() => applyExam('equal')} className={btnBaseClass}>
              <div className="flex gap-0.5 shrink-0">
                {STATS.map((s) => (
                  <span key={s} className="w-1 h-3 rounded-sm" style={{ backgroundColor: STAT_COLOR_VAR[s] }} />
                ))}
              </div>
              均等 3分割
            </button>
          </div>
        </div>

        {/* 比率バー（ハンドルをドラッグして調整 → 全試験に同じ比率で適用） */}
        <ExamRatioBar ratio={examRatio} onChange={setExamRatio} />

        {/* 適用結果（配分のみ・基礎値は別途+加算） */}
        {examDays.length > 0 && (
          <div className="flex flex-col gap-1 text-xs">
            <span className="text-gray-500">適用結果（配分のみ・基礎値は別途+加算）:</span>
            {examDays.map((e) => {
              const a = examAllocations[e.week] ?? { vo: 0, da: 0, vi: 0 };
              return (
                <div key={e.week} className="flex items-center gap-2 flex-wrap">
                  <span className="text-gray-600 italic">{e.name}</span>
                  <span className="flex gap-2">
                    {STATS.map((s) => (
                      <span key={s} style={{ color: STAT_COLOR_VAR[s] }}>
                        {STAT_LABEL[s]} {a[s]}
                      </span>
                    ))}
                  </span>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}