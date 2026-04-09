import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useCalcStore } from '../../stores/calcStore';
import { trackEvent } from '../../utils/analytics';

const COUNT_LABELS: { key: string; label: string }[] = [
  { key: 'p_drink_acquire', label: 'Pドリンク獲得' },
  { key: 'p_item_acquire', label: 'Pアイテム獲得' },
  { key: 'skill_ssr_acquire', label: 'スキル(SSR)獲得' },
  { key: 'skill_enhance', label: 'スキル強化' },
  { key: 'skill_delete', label: 'スキル削除' },
  { key: 'skill_custom', label: 'スキルカスタム' },
  { key: 'skill_change', label: 'スキルチェンジ' },
  { key: 'active_enhance', label: 'アクティブ強化' },
  { key: 'active_delete', label: 'アクティブ削除' },
  { key: 'mental_acquire', label: 'メンタル獲得' },
  { key: 'mental_enhance', label: 'メンタル強化' },
  { key: 'active_acquire', label: 'アクティブ獲得' },
  { key: 'good_condition_acquire', label: '好調カード獲得' },
  { key: 'concentrate_acquire', label: '集中カード獲得' },
  { key: 'genki_acquire', label: '元気カード獲得' },
  { key: 'good_impression_acquire', label: '好印象カード獲得' },
  { key: 'motivation_acquire', label: 'やる気カード獲得' },
  { key: 'conserve_acquire', label: '温存カード獲得' },
  { key: 'fullpower_acquire', label: '全力カード獲得' },
  { key: 'aggressive_acquire', label: '強気カード獲得' },
  { key: 'consultation_drink', label: '相談Pドリンク交換' },
];

export default function EventCountConfig() {
  const [expanded, setExpanded] = useState(false);
  const templates = useAppStore((s) => s.templates);
  const selectedPlanId = useCalcStore((s) => s.selectedPlanId);
  const additionalCounts = useCalcStore((s) => s.additionalCounts);
  const setAdditionalCount = useCalcStore((s) => s.setAdditionalCount);
  const applyTemplate = useCalcStore((s) => s.applyTemplate);

  const filteredTemplates = templates.filter(
    (t) => !t.plan_id || t.plan_id === selectedPlanId,
  );

  return (
    <div className="space-y-2">
      <div className="flex items-center gap-3">
        <label className="text-sm font-semibold text-gray-700 shrink-0">
          イベント回数テンプレート
        </label>
        <select
          className="flex-1 border border-gray-300 rounded-md px-3 py-2 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-accent)] focus:border-transparent"
          defaultValue=""
          onChange={(e) => {
            const tmpl = filteredTemplates.find((t) => t.name === e.target.value);
            if (tmpl) {
              trackEvent('template_applied', { template_name: tmpl.name });
              applyTemplate(tmpl);
            }
          }}
        >
          <option value="">-- テンプレートを選択 --</option>
          {filteredTemplates.map((t) => (
            <option key={t.name} value={t.name}>
              {t.name}
            </option>
          ))}
        </select>
      </div>

      <button
        type="button"
        className="text-sm text-[var(--color-accent)] hover:underline flex items-center gap-1"
        onClick={() => {
          const next = !expanded;
          trackEvent('event_count_expanded', { expanded: next });
          setExpanded(next);
        }}
      >
        <span className={`inline-block transition-transform ${expanded ? 'rotate-90' : ''}`}>
          &#9654;
        </span>
        イベント回数を個別設定
      </button>

      {expanded && (
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-x-4 gap-y-2 bg-gray-50 rounded-md p-3 border border-gray-200">
          {COUNT_LABELS.map(({ key, label }) => (
            <label key={key} className="flex items-center gap-2 text-sm">
              <span className="text-gray-600 min-w-[8rem]">{label}</span>
              <input
                type="number"
                min={0}
                className="w-16 border border-gray-300 rounded px-2 py-1 text-sm text-center bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                value={(additionalCounts as Record<string, number>)[key] ?? 0}
                onChange={(e) => setAdditionalCount(key, parseInt(e.target.value) || 0)}
              />
            </label>
          ))}
        </div>
      )}
    </div>
  );
}
