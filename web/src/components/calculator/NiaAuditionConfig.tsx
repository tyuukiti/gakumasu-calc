import { useMemo } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useCalcStore, computeNiaAuditionGain } from '../../stores/calcStore';

type Stat = 'vo' | 'da' | 'vi';
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
const STAT_COLOR: Record<Stat, string> = {
  vo: 'var(--color-vo-text)',
  da: 'var(--color-da-text)',
  vi: 'var(--color-vi-text)',
};

/**
 * N.I.Aオーディション獲得パラメータの設定UI。
 * 各オーディションで種別を選び、選択キャラの審査基準・流行で Vo/Da/Vi へ振り分けた獲得量を表示。
 * キャラ未選択・流行データ無しなら獲得0。オーディション種別を持たないプラン(初レジェンド)では非表示。
 */
export default function NiaAuditionConfig({ planId }: { planId: string }) {
  const plans = useAppStore((s) => s.plans);
  const characters = useAppStore((s) => s.characters);
  const selectedCharacterId = useCalcStore((s) => s.selectedCharacterId);
  const tierByWeek = useCalcStore((s) => s.niaAuditionTierByWeek);
  const setTier = useCalcStore((s) => s.setNiaAuditionTier);

  const plan = useMemo(() => plans.find((p) => p.id === planId), [plans, planId]);
  const auditions = useMemo(
    () => (plan?.schedule ?? []).filter((w) => (w.nia_audition_tiers?.length ?? 0) > 0),
    [plan],
  );
  const character = selectedCharacterId
    ? characters.find((c) => c.id === selectedCharacterId) ?? null
    : null;

  if (!plan || auditions.length === 0) return null;

  const hasTrend = !!character?.nia_trend && character.nia_trend.length >= 3;

  return (
    <div className="space-y-2">
      <label className="text-sm font-semibold text-gray-700">
        オーディション獲得（種別選択）
        <span className="ml-2 text-xs font-normal text-gray-500">※表示はパラメータボーナス適用前の基礎値</span>
      </label>
      {!hasTrend && (
        <p className="text-xs text-gray-500">
          {character
            ? 'このキャラの流行データが無いため獲得は0です。'
            : 'キャラ未選択のため獲得は0です（キャラを選ぶと流行で反映されます）。'}
        </p>
      )}
      <div className="space-y-2">
        {auditions.map((w) => {
          const tiers = w.nia_audition_tiers ?? [];
          const selected = tierByWeek[w.week] ?? tiers[0]?.name ?? '';
          const gain = computeNiaAuditionGain(w, character, selected);
          return (
            <div
              key={w.week}
              className="flex items-center gap-3 bg-gray-50 border border-gray-200 rounded-md px-3 py-2 text-sm flex-wrap"
            >
              <span className="text-gray-700 font-medium w-28 shrink-0">
                {w.event_name ?? `Week ${w.week}`}
              </span>
              <select
                className="border border-gray-300 rounded px-2 py-1 text-sm bg-white"
                value={selected}
                onChange={(e) => setTier(w.week, e.target.value)}
              >
                {tiers.map((t) => (
                  <option key={t.name} value={t.name}>{t.name}</option>
                ))}
              </select>
              {gain ? (
                <span className="text-xs ml-auto flex gap-2">
                  {(['vo', 'da', 'vi'] as Stat[]).map((s) => (
                    <span key={s} style={{ color: STAT_COLOR[s] }}>
                      {STAT_LABEL[s]}+{gain[s]}
                    </span>
                  ))}
                </span>
              ) : (
                <span className="text-xs text-gray-400 ml-auto">獲得0</span>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
