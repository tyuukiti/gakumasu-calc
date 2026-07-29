import { useEffect, useMemo, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useHifStore, defaultChoiceForWeek } from '../../stores/hifStore';
import type { HifChoice } from '../../stores/hifStore';
import type { WeekSchedule } from '../../types/models';

type Stat = 'vo' | 'da' | 'vi';
const STATS: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
/** 白背景でも視認できるダーク版カラー (テキスト用) */
const STAT_COLOR: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};

const ACTION_LABEL: Record<string, string> = {
  outing: 'お出かけ',
  consultation: '相談',
  activity_supply: '活動支給',
  special_training: '特別指導',
  vo_class: 'Vo授業',
  da_class: 'Da授業',
  vi_class: 'Vi授業',
};

/**
 * 週情報から「Day X」ラベルを構築。本戦は別表記。
 */
function dayLabel(week: WeekSchedule): string {
  if (week.week <= 20) return `Day ${week.week}`;
  if (week.week === 27) return '本戦R1';
  if (week.week === 29) return '本戦R2';
  if (week.week === 28) return '本戦インターバル';
  return `本戦${week.week - 20}日目`;
}

/**
 * 週の「種類」短ラベル。
 */
function weekTypeLabel(week: WeekSchedule): string {
  if (week.type === 'audition') return '固定イベント';
  if (week.type === 'public_lesson') return '公開レッスン';
  const acts = week.available_actions;
  // 選択肢なし (本戦インターバル等): サポート発動なしの固定日
  if (acts.length === 0) return 'インターバル';
  if (acts.some((a) => a.endsWith('_class'))) return '授業';
  if (acts.length === 1) return ACTION_LABEL[acts[0]] ?? acts[0];
  return acts.map((a) => ACTION_LABEL[a] ?? a).join(' / ');
}

export default function ScheduleConfig() {
  const { plans } = useAppStore();
  const hifPlan = plans.find((p) => p.id === 'hif');
  const scheduleChoices = useHifStore((s) => s.scheduleChoices);
  const setScheduleChoice = useHifStore((s) => s.setScheduleChoice);
  const [expanded, setExpanded] = useState(false);

  // 初回ロード時、未設定の週にデフォルトを埋める
  useEffect(() => {
    if (!hifPlan) return;
    for (const week of hifPlan.schedule) {
      if (scheduleChoices[week.week] !== undefined) continue;
      const def = defaultChoiceForWeek(week);
      if (def) setScheduleChoice(week.week, def);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hifPlan]);

  const items = useMemo(() => hifPlan?.schedule ?? [], [hifPlan]);

  if (!hifPlan) {
    return (
      <div className="text-sm text-gray-500">HIFプランが読み込まれていません</div>
    );
  }

  return (
    <div className="space-y-2">
      <button
        type="button"
        className="flex items-center gap-2 text-sm font-semibold text-gray-700 hover:text-[var(--color-accent)] cursor-pointer"
        onClick={() => setExpanded(!expanded)}
      >
        <span className={`inline-block transition-transform ${expanded ? 'rotate-90' : ''}`}>
          &#9654;
        </span>
        個別調整 (全 {items.length} 日)
        <span className="text-xs font-normal text-gray-500">— 一括設定で多くの日が設定済みです</span>
      </button>
      {expanded && (
      <div className="border border-gray-200 rounded-md bg-gray-50 max-h-[480px] overflow-y-auto">
        <ul className="divide-y divide-gray-200">
          {items.map((week) => (
            <li key={week.week} className="flex items-center gap-3 px-3 py-2 text-sm">
              {/* Day ラベル */}
              <div className="w-24 shrink-0">
                <div className="font-semibold text-gray-700">{dayLabel(week)}</div>
                <div className="text-xs text-gray-500">{weekTypeLabel(week)}</div>
              </div>

              {/* 選択UI */}
              <div className="flex-1">
                {week.type === 'audition' ? (
                  <ExamChoice week={week} />
                ) : week.type === 'public_lesson' ? (
                  <PublicLessonChoice
                    week={week}
                    choice={scheduleChoices[week.week]}
                    onChange={(c) => setScheduleChoice(week.week, c)}
                  />
                ) : (
                  <SingleActionChoice
                    week={week}
                    choice={scheduleChoices[week.week]}
                    onChange={(c) => setScheduleChoice(week.week, c)}
                  />
                )}
              </div>
            </li>
          ))}
        </ul>
      </div>
      )}
    </div>
  );
}

function ExamChoice({ week }: { week: WeekSchedule }) {
  const examAllocations = useHifStore((s) => s.examAllocations);
  const setExamAllocation = useHifStore((s) => s.setExamAllocation);
  const base = week.hif_exam_base ?? 0;
  const distributed = week.hif_exam_distributed ?? 0;
  const alloc = examAllocations[week.week] ?? { vo: 0, da: 0, vi: 0 };
  const used = alloc.vo + alloc.da + alloc.vi;
  const remaining = distributed - used;
  const over = remaining < 0;

  if (distributed === 0 && base === 0) {
    return <span className="text-gray-600 italic">{week.event_name ?? '固定イベント'}</span>;
  }

  // 一括設定のバー/プリセットで配分されるが、ここで試験ごとに手入力で上書きもできる
  return (
    <div className="flex items-center gap-2 flex-wrap text-xs">
      <span className="text-gray-700 italic">{week.event_name}</span>
      {base > 0 && (
        <span className="text-gray-500">基礎 +{base}/属性</span>
      )}
      {distributed > 0 && (
        <>
          {STATS.map((s) => (
            <label key={s} className="flex items-center gap-1 text-gray-600">
              <span style={{ color: STAT_COLOR[s] }}>{STAT_LABEL[s]}</span>
              <input
                type="number"
                min={0}
                className="w-12 border border-gray-300 rounded px-1 py-0.5 text-xs text-center bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                value={alloc[s]}
                onChange={(e) => setExamAllocation(week.week, s, parseInt(e.target.value) || 0)}
              />
            </label>
          ))}
          <span className={`text-xs ${over ? 'text-red-500 font-semibold' : 'text-gray-500'}`}>
            配分 {used}/{distributed}
          </span>
        </>
      )}
    </div>
  );
}

/** 6パターンの「メイン→サブ」組合せ */
const LESSON_COMBOS: Array<{ main: Stat; sub: Stat }> = [
  { main: 'vo', sub: 'da' },
  { main: 'vo', sub: 'vi' },
  { main: 'da', sub: 'vo' },
  { main: 'da', sub: 'vi' },
  { main: 'vi', sub: 'vo' },
  { main: 'vi', sub: 'da' },
];

function PublicLessonChoice({
  week,
  choice,
  onChange,
}: {
  week: WeekSchedule;
  choice: HifChoice | undefined;
  onChange: (c: HifChoice) => void;
}) {
  const lessonChoice = choice as Extract<HifChoice, { sub_stat: Stat }> | undefined;
  const mainStat: Stat = lessonChoice?.action ? (lessonChoice.action.split('_')[0] as Stat) : 'vo';
  const subStat: Stat = lessonChoice?.sub_stat ?? (mainStat === 'vo' ? 'da' : 'vo');
  const mainValue = week.lessons.find((l) => l.type === mainStat)?.sp_bonus[mainStat] ?? 0;
  const subValue = week.hif_sub_value ?? 0;

  const currentValue = `${mainStat}-${subStat}`;

  return (
    <div className="flex items-center gap-2 text-xs">
      <select
        className="border border-gray-300 rounded px-2 py-0.5 text-xs bg-white"
        value={currentValue}
        onChange={(e) => {
          const [m, s] = e.target.value.split('-') as [Stat, Stat];
          onChange({ action: `${m}_lesson` as 'vo_lesson', sub_stat: s });
        }}
      >
        {LESSON_COMBOS.map(({ main, sub }) => (
          <option key={`${main}-${sub}`} value={`${main}-${sub}`}>
            {STAT_LABEL[main]}→{STAT_LABEL[sub]}
          </option>
        ))}
      </select>
      <span className="text-gray-500">
        <span style={{ color: STAT_COLOR[mainStat] }}>{STAT_LABEL[mainStat]}+{mainValue}</span>
        {' / '}
        <span style={{ color: STAT_COLOR[subStat] }}>{STAT_LABEL[subStat]}+{subValue}</span>
      </span>
    </div>
  );
}

function SingleActionChoice({
  week,
  choice,
  onChange,
}: {
  week: WeekSchedule;
  choice: HifChoice | undefined;
  onChange: (c: HifChoice) => void;
}) {
  const action = (choice && 'action' in choice ? choice.action : week.available_actions[0]) ?? '';
  const acts = week.available_actions;

  // 選択肢なし (本戦インターバル等): 相談/特別指導はサポート効果が発動しないため計算対象外
  if (acts.length === 0) {
    return <span className="text-gray-500 italic">サポート発動なし</span>;
  }

  // 授業日: type別の sp_bonus を併記表示
  const isClass = acts.length > 0 && acts.every((a) => a.endsWith('_class'));

  if (acts.length === 1) {
    return (
      <span className="text-gray-700">{ACTION_LABEL[acts[0]] ?? acts[0]}</span>
    );
  }

  return (
    <select
      className="border border-gray-300 rounded px-2 py-1 text-xs bg-white"
      value={action}
      onChange={(e) => onChange({ action: e.target.value } as HifChoice)}
    >
      {acts.map((a) => {
        let label = ACTION_LABEL[a] ?? a;
        if (isClass) {
          const stat = a.split('_')[0] as Stat;
          const val = week.classes.find((c) => c.type === stat)?.sp_bonus[stat] ?? 0;
          label = `${STAT_LABEL[stat]}授業 (+${val})`;
        }
        return (
          <option key={a} value={a}>{label}</option>
        );
      })}
    </select>
  );
}
