import { useCalcStore } from '../../stores/calcStore';

export default function OwnedOnlyToggle() {
  const ownedOnly = useCalcStore((s) => s.ownedOnly);
  const setOwnedOnly = useCalcStore((s) => s.setOwnedOnly);

  return (
    <label className="flex items-center gap-2 cursor-pointer select-none">
      <input
        type="checkbox"
        checked={ownedOnly}
        onChange={(e) => setOwnedOnly(e.target.checked)}
        className="w-4 h-4 accent-[var(--color-accent)] rounded"
      />
      <span className="text-sm text-gray-700">所持カードのみで計算</span>
    </label>
  );
}
