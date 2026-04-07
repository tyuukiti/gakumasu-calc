import { useAppStore } from '../../stores/appStore';
import { useCalcStore } from '../../stores/calcStore';
import { trackEvent } from '../../utils/analytics';

export default function PlanSelector() {
  const plans = useAppStore((s) => s.plans);
  const selectedPlanId = useCalcStore((s) => s.selectedPlanId);
  const setSelectedPlanId = useCalcStore((s) => s.setSelectedPlanId);

  return (
    <div className="flex items-center gap-3">
      <label className="text-sm font-semibold text-gray-700 shrink-0">育成プラン</label>
      <select
        className="flex-1 border border-gray-300 rounded-md px-3 py-2 text-sm bg-white focus:outline-none focus:ring-2 focus:ring-[var(--color-accent)] focus:border-transparent"
        value={selectedPlanId}
        onChange={(e) => {
          trackEvent('plan_selected', { plan_id: e.target.value });
          setSelectedPlanId(e.target.value);
        }}
      >
        <option value="">-- 選択してください --</option>
        {plans.map((plan) => (
          <option key={plan.id} value={plan.id}>
            {plan.name}
          </option>
        ))}
      </select>
    </div>
  );
}
