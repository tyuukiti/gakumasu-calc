import { useCalcStore } from '../../stores/calcStore';
import type { RoleType } from '../../types/enums';

const ROLES: RoleType[] = ['メイン1', 'メイン2', 'サブ'];

const STAT_CONFIG: {
  key: 'vo' | 'da' | 'vi';
  label: string;
  color: string;
  bgColor: string;
}[] = [
  { key: 'vo', label: 'Vocal', color: 'var(--color-vo)', bgColor: 'var(--color-vo-bg)' },
  { key: 'da', label: 'Dance', color: 'var(--color-da)', bgColor: 'var(--color-da-bg)' },
  { key: 'vi', label: 'Visual', color: 'var(--color-vi)', bgColor: 'var(--color-vi-bg)' },
];

export default function StatRoleConfig() {
  const voRole = useCalcStore((s) => s.voRole);
  const daRole = useCalcStore((s) => s.daRole);
  const viRole = useCalcStore((s) => s.viRole);
  const voSpCount = useCalcStore((s) => s.voSpCount);
  const daSpCount = useCalcStore((s) => s.daSpCount);
  const viSpCount = useCalcStore((s) => s.viSpCount);
  const setRole = useCalcStore((s) => s.setRole);
  const setSpCount = useCalcStore((s) => s.setSpCount);

  const roles = { vo: voRole, da: daRole, vi: viRole };
  const spCounts = { vo: voSpCount, da: daSpCount, vi: viSpCount };

  return (
    <div className="space-y-2">
      <label className="text-sm font-semibold text-gray-700">属性設定</label>
      <div className="space-y-2">
        {STAT_CONFIG.map(({ key, label, color, bgColor }) => (
          <div
            key={key}
            className="flex items-center gap-3 rounded-md px-3 py-2"
            style={{ backgroundColor: bgColor }}
          >
            <span
              className="w-16 text-sm font-bold"
              style={{ color }}
            >
              {label}
            </span>

            <select
              className="border border-gray-300 rounded px-2 py-1 text-sm bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
              value={roles[key]}
              onChange={(e) => setRole(key, e.target.value as RoleType)}
            >
              {ROLES.map((r) => (
                <option key={r} value={r}>{r}</option>
              ))}
            </select>

            <label className="flex items-center gap-1.5 text-sm text-gray-600 ml-auto">
              <span>SP率枚数</span>
              <input
                type="number"
                min={0}
                max={6}
                className="w-14 border border-gray-300 rounded px-2 py-1 text-sm text-center bg-white focus:outline-none focus:ring-1 focus:ring-[var(--color-accent)]"
                value={spCounts[key]}
                onChange={(e) => setSpCount(key, parseInt(e.target.value) || 0)}
              />
            </label>
          </div>
        ))}
      </div>
    </div>
  );
}
