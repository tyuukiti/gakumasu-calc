import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useCalcStore, MAX_EVENT_COUNT_PRESETS } from '../../stores/calcStore';
import { trackEvent, trackFunnelStep } from '../../utils/analytics';

const COUNT_LABELS: { key: string; label: string }[] = [
  { key: 'p_drink_acquire', label: 'Pドリンク獲得' },
  { key: 'p_item_acquire', label: 'Pアイテム獲得' },
  { key: 'skill_acquire', label: 'スキルカード獲得' },
  { key: 'skill_ssr_acquire', label: 'スキル(SSR)獲得' },
  { key: 'skill_enhance', label: 'スキル強化' },
  { key: 'skill_delete', label: 'スキル削除' },
  { key: 'skill_custom', label: 'スキルカスタム' },
  { key: 'skill_change', label: 'スキルチェンジ' },
  { key: 'active_acquire', label: 'アクティブ獲得' },
  { key: 'active_enhance', label: 'アクティブ強化' },
  { key: 'active_delete', label: 'アクティブ削除' },
  { key: 'mental_acquire', label: 'メンタル獲得' },
  { key: 'mental_enhance', label: 'メンタル強化' },
  { key: 'mental_delete', label: 'メンタル削除' },
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

const PLAN_TYPE_KEYWORDS: Record<string, string> = {
  sense: 'センス',
  logic: 'ロジック',
  anomaly: 'アノマリー',
};

interface Props {
  /** テンプレート絞り込み用の plan_id 上書き (HIFタブでは "hif" を渡す) */
  planIdOverride?: string;
}

export default function EventCountConfig({ planIdOverride }: Props = {}) {
  const [expanded, setExpanded] = useState(false);
  const [selectedPresetName, setSelectedPresetName] = useState('');
  const [newPresetName, setNewPresetName] = useState('');
  const templates = useAppStore((s) => s.templates);
  const selectedPlanId = useCalcStore((s) => s.selectedPlanId);
  const selectedPlanType = useCalcStore((s) => s.selectedPlanType);
  const additionalCounts = useCalcStore((s) => s.additionalCounts);
  const setAdditionalCount = useCalcStore((s) => s.setAdditionalCount);
  const applyTemplate = useCalcStore((s) => s.applyTemplate);
  const eventCountPresets = useCalcStore((s) => s.eventCountPresets);
  const saveEventCountPreset = useCalcStore((s) => s.saveEventCountPreset);
  const loadEventCountPreset = useCalcStore((s) => s.loadEventCountPreset);
  const deleteEventCountPreset = useCalcStore((s) => s.deleteEventCountPreset);

  const effectivePlanId = planIdOverride ?? selectedPlanId;
  const planTypeKeyword = PLAN_TYPE_KEYWORDS[selectedPlanType];
  const filteredTemplates = templates.filter((t) => {
    if (t.plan_id && t.plan_id !== effectivePlanId) return false;
    if (planTypeKeyword && !t.name.includes(planTypeKeyword)) return false;
    return true;
  });

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
              trackFunnelStep('calculator', 2, 'config_set');
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
        <div className="space-y-3">
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

          {/* ユーザ入力の保存プリセット */}
          <div className="border border-gray-200 rounded-md p-3 space-y-2">
            <div className="flex items-center gap-2 text-xs text-gray-600">
              <span className="font-semibold">マイプリセット</span>
              <span className="text-gray-400">
                {eventCountPresets.length}/{MAX_EVENT_COUNT_PRESETS}
              </span>
            </div>

            {/* 呼び出し行: ボタンリスト + 削除 */}
            <div className="flex items-start gap-2">
              <div className="flex-1 flex flex-wrap gap-1 min-h-[28px] items-center">
                {eventCountPresets.length === 0 ? (
                  <span className="text-xs text-gray-400">プリセット未登録</span>
                ) : (
                  eventCountPresets.map((p) => {
                    const isSelected = selectedPresetName === p.name;
                    return (
                      <button
                        key={p.name}
                        type="button"
                        onClick={() => {
                          setSelectedPresetName(p.name);
                          setNewPresetName(p.name);
                          loadEventCountPreset(p.name);
                          trackEvent('event_count_preset_loaded', { preset_name: p.name });
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
                disabled={
                  !selectedPresetName ||
                  !eventCountPresets.some((p) => p.name === selectedPresetName)
                }
                onClick={() => {
                  if (!selectedPresetName) return;
                  deleteEventCountPreset(selectedPresetName);
                  setSelectedPresetName('');
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
                value={newPresetName}
                onChange={(e) => setNewPresetName(e.target.value)}
                placeholder="プリセット名"
                className="flex-1 border border-gray-300 rounded px-2 py-1 text-xs bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
              />
              <button
                type="button"
                disabled={
                  !newPresetName.trim() ||
                  (eventCountPresets.length >= MAX_EVENT_COUNT_PRESETS &&
                    !eventCountPresets.some((p) => p.name === newPresetName.trim()))
                }
                onClick={() => {
                  const name = newPresetName.trim();
                  if (!name) return;
                  saveEventCountPreset(name);
                  setSelectedPresetName(name);
                  setNewPresetName('');
                  trackEvent('event_count_preset_saved', { preset_name: name });
                }}
                className="text-xs border border-[var(--color-accent)] rounded px-2 py-1 text-[var(--color-accent)] hover:bg-[var(--color-accent)] hover:text-white disabled:border-gray-300 disabled:text-gray-300 disabled:hover:bg-transparent cursor-pointer disabled:cursor-not-allowed"
                title={
                  eventCountPresets.length >= MAX_EVENT_COUNT_PRESETS &&
                  !eventCountPresets.some((p) => p.name === newPresetName.trim())
                    ? `上限 ${MAX_EVENT_COUNT_PRESETS} 件に達しています`
                    : '現在のイベント回数をこの名前で保存'
                }
              >
                保存
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
