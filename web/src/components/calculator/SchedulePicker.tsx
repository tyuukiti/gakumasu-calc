import { useEffect, useMemo, useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useCalcStore } from '../../stores/calcStore';
import type { ScheduleChoice } from '../../stores/calcStore';
import type { WeekSchedule } from '../../types/models';
import type { ActionType } from '../../types/enums';

type Stat = 'vo' | 'da' | 'vi';
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };

const ACTION_LABEL: Record<string, string> = {
  vo_lesson: 'Voレッスン',
  da_lesson: 'Daレッスン',
  vi_lesson: 'Viレッスン',
  vo_class: 'Vo授業',
  da_class: 'Da授業',
  vi_class: 'Vi授業',
  outing: 'お出かけ',
  rest: '休む',
  consultation: '相談',
  activity_supply: '活動支給',
  special_training: '特別指導',
};

function isFixed(week: WeekSchedule): boolean {
  return week.type === 'fixed_event' || week.type === 'audition' || week.type === 'exam';
}

/** 週の「種類」短ラベル。 */
function weekTypeLabel(week: WeekSchedule): string {
  if (isFixed(week)) return week.event_name ?? '固定イベント';
  const acts = week.available_actions;
  if (acts.length === 0) return '—';
  if (acts.some((a) => a.endsWith('_lesson'))) return 'レッスン';
  if (acts.some((a) => a.endsWith('_class'))) return '授業';
  if (acts.length === 1) return ACTION_LABEL[acts[0]] ?? acts[0];
  return acts.map((a) => ACTION_LABEL[a] ?? a).join(' / ');
}

/**
 * 日程方式 (初レジェンド / NIA) の個別週調整UI。
 * - マウント/プラン切替時に未設定週を自動配分でシード
 * - 固定イベント(中間試験/オーディション等)は読み取り専用表示
 * - レッスン/授業/その他選択日はドロップダウンで選択
 */
export default function SchedulePicker({ planId }: { planId: string }) {
  const plans = useAppStore((s) => s.plans);
  const plan = useMemo(() => plans.find((p) => p.id === planId), [plans, planId]);
  const scheduleChoices = useCalcStore((s) => s.scheduleChoices);
  const setScheduleChoice = useCalcStore((s) => s.setScheduleChoice);
  const seedScheduleDefaults = useCalcStore((s) => s.seedScheduleDefaults);
  const [expanded, setExpanded] = useState(false);

  // 初回/プラン切替時、未設定の週にデフォルトを埋める
  useEffect(() => {
    if (plan) seedScheduleDefaults(planId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [planId, plan]);

  const sched = scheduleChoices[planId] ?? {};
  const items = plan?.schedule ?? [];

  if (!plan) {
    return <div className="text-sm text-gray-500">プランが読み込まれていません</div>;
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
        個別調整 (全 {items.length} 週)
        <span className="text-xs font-normal text-gray-500">— 一括設定で多くの週が設定済みです</span>
      </button>
      {expanded && (
        <div className="border border-gray-200 rounded-md bg-gray-50 max-h-[480px] overflow-y-auto">
          <ul className="divide-y divide-gray-200">
            {items.map((week) => (
              <li key={week.week} className="flex items-center gap-3 px-3 py-2 text-sm">
                <div className="w-24 shrink-0">
                  <div className="font-semibold text-gray-700">{week.week}週目</div>
                  <div className="text-xs text-gray-500">{weekTypeLabel(week)}</div>
                </div>
                <div className="flex-1">
                  {isFixed(week) ? (
                    <FixedDisplay week={week} />
                  ) : (
                    <ActionChoice
                      week={week}
                      choice={sched[week.week]}
                      onChange={(c) => setScheduleChoice(planId, week.week, c)}
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

function FixedDisplay({ week }: { week: WeekSchedule }) {
  const g = week.status_gain ?? { vo: 0, da: 0, vi: 0 };
  const hasGain = g.vo !== 0 || g.da !== 0 || g.vi !== 0;
  return (
    <span className="text-gray-600 italic text-xs">
      {week.event_name ?? '固定イベント'}
      {hasGain ? ` (Vo+${g.vo} / Da+${g.da} / Vi+${g.vi})` : ''}
    </span>
  );
}

function ActionChoice({
  week,
  choice,
  onChange,
}: {
  week: WeekSchedule;
  choice: ScheduleChoice | undefined;
  onChange: (c: ScheduleChoice) => void;
}) {
  const acts = week.available_actions;
  const action = (choice?.action as string) ?? acts[0] ?? '';

  if (acts.length === 0) {
    return <span className="text-gray-500 italic">—</span>;
  }
  if (acts.length === 1) {
    return <span className="text-gray-700">{ACTION_LABEL[acts[0]] ?? acts[0]}</span>;
  }

  return (
    <select
      className="border border-gray-300 rounded px-2 py-1 text-xs bg-white"
      value={action}
      onChange={(e) => onChange({ action: e.target.value as ActionType })}
    >
      {acts.map((a) => {
        // 授業/レッスンは選択肢ごとに上昇値を併記 (休む等と混在する週があるため per-option 判定)
        let label = ACTION_LABEL[a] ?? a;
        if (a.endsWith('_class')) {
          const stat = a.split('_')[0] as Stat;
          const val = week.classes.find((c) => c.type === stat)?.sp_bonus[stat] ?? 0;
          label = `${STAT_LABEL[stat]}授業 (+${val})`;
        } else if (a.endsWith('_lesson')) {
          const stat = a.split('_')[0] as Stat;
          const val = week.lessons.find((l) => l.type === stat)?.sp_bonus[stat] ?? 0;
          label = `${STAT_LABEL[stat]}レッスン (+${val})`;
        }
        return (
          <option key={a} value={a}>{label}</option>
        );
      })}
    </select>
  );
}
