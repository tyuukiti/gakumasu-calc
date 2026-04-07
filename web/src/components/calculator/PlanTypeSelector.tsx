import { useCalcStore } from '../../stores/calcStore';
import type { PlanType } from '../../types/enums';
import { trackEvent } from '../../utils/analytics';

const PLAN_TYPES: { value: PlanType; label: string }[] = [
  { value: 'sense', label: 'センス' },
  { value: 'logic', label: 'ロジック' },
  { value: 'anomaly', label: 'アノマリー' },
];

export default function PlanTypeSelector() {
  const selectedPlanType = useCalcStore((s) => s.selectedPlanType);
  const setSelectedPlanType = useCalcStore((s) => s.setSelectedPlanType);

  return (
    <div className="flex items-center gap-3">
      <label className="text-sm font-semibold text-gray-700 shrink-0">育成タイプ</label>
      <div className="flex gap-4">
        {PLAN_TYPES.map(({ value, label }) => (
          <label key={value} className="flex items-center gap-1.5 cursor-pointer">
            <input
              type="radio"
              name="planType"
              value={value}
              checked={selectedPlanType === value}
              onChange={() => {
                trackEvent('plan_type_selected', { plan_type: value });
                setSelectedPlanType(value);
              }}
              className="accent-[var(--color-accent)]"
            />
            <span className="text-sm">{label}</span>
          </label>
        ))}
      </div>
    </div>
  );
}
