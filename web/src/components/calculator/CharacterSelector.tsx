import { useState } from 'react';
import { useAppStore } from '../../stores/appStore';
import { useCalcStore } from '../../stores/calcStore';
import { applyCharacterToggles } from '../../services/characterBonus';

// 背景色の明度から白/黒の文字色を選ぶ（YIQ式）
function foregroundFor(hex: string): string {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex);
  if (!m) return '#FFFFFF';
  const v = parseInt(m[1], 16);
  const r = (v >> 16) & 0xff;
  const g = (v >> 8) & 0xff;
  const b = v & 0xff;
  const yiq = (r * 299 + g * 587 + b * 114) / 1000;
  return yiq >= 160 ? '#1F2937' : '#FFFFFF';
}

export default function CharacterSelector() {
  const characters = useAppStore((s) => s.characters);
  const selectedCharacterId = useCalcStore((s) => s.selectedCharacterId);
  const setSelectedCharacter = useCalcStore((s) => s.setSelectedCharacter);
  const uncap3Enabled = useCalcStore((s) => s.uncap3BonusEnabled);
  const setUncap3Enabled = useCalcStore((s) => s.setUncap3BonusEnabled);
  const step4Enabled = useCalcStore((s) => s.step4BonusEnabled);
  const setStep4Enabled = useCalcStore((s) => s.setStep4BonusEnabled);
  const [isOpen, setIsOpen] = useState(false);

  if (characters.length === 0) return null;

  const selected = characters.find((c) => c.id === selectedCharacterId) ?? null;
  // 計算と同じトグル（3凸 OFFで減算 / STEP4 ONで加算）を反映した表示値
  const effective = applyCharacterToggles(selected, uncap3Enabled, step4Enabled);
  const effectiveBase = effective?.base_status_bonus ?? null;
  const effectivePara = effective?.para_bonus ?? null;

  return (
    <div className="border-t border-gray-100 pt-3">
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between text-sm font-semibold text-gray-700 hover:text-gray-900 cursor-pointer"
      >
        <span>
          キャラ選択
          <span className="text-xs text-gray-400 ml-1">（任意）</span>
          {selected && (
            <span className="text-xs text-gray-600 ml-2">: {selected.name}</span>
          )}
        </span>
        <span
          className={`text-xs text-gray-400 transition-transform ${isOpen ? 'rotate-90' : ''}`}
        >
          &#9654;
        </span>
      </button>

      {isOpen && (
        <div className="mt-3">
          <div className="grid grid-cols-7 gap-2">
            {characters.map((c) => {
              const isSelected = c.id === selectedCharacterId;
              const fg = foregroundFor(c.color);
              return (
                <button
                  key={c.id}
                  type="button"
                  onClick={() => setSelectedCharacter(isSelected ? null : c.id)}
                  className={`aspect-square rounded-md border border-gray-200 flex flex-col items-center justify-center cursor-pointer transition-all ${
                    isSelected
                      ? 'ring-2 ring-[var(--color-accent)] ring-offset-1'
                      : 'hover:opacity-90'
                  }`}
                  style={{ backgroundColor: c.color, color: fg }}
                  title={c.name}
                >
                  <span className="text-xl font-bold">{c.initial}</span>
                  <span className="text-[10px] truncate w-full px-1 text-center opacity-90">
                    {c.name}
                  </span>
                </button>
              );
            })}
          </div>

          {selected && selected.uncap3_bonus && (
            <label className="mt-3 flex items-center gap-2 text-xs text-gray-700 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={uncap3Enabled}
                onChange={(e) => setUncap3Enabled(e.target.checked)}
                className="cursor-pointer"
              />
              <span>
                3凸レッスンボーナス込み
                <span className="text-gray-500 ml-1">
                  (うち +{selected.uncap3_bonus.vo}/{selected.uncap3_bonus.da}/
                  {selected.uncap3_bonus.vi}%、OFFで減算)
                </span>
              </span>
            </label>
          )}

          {selected && selected.step4_bonus && (
            <label className="mt-2 flex items-center gap-2 text-xs text-gray-700 cursor-pointer select-none">
              <input
                type="checkbox"
                checked={step4Enabled}
                onChange={(e) => setStep4Enabled(e.target.checked)}
                className="cursor-pointer"
              />
              <span>
                STEP4ボーナス込み
                <span className="text-gray-500 ml-1">
                  (基礎+{selected.step4_bonus.base_status_bonus.vo}/
                  {selected.step4_bonus.base_status_bonus.da}/
                  {selected.step4_bonus.base_status_bonus.vi}、パラボ+
                  {selected.step4_bonus.para_bonus.vo}/{selected.step4_bonus.para_bonus.da}/
                  {selected.step4_bonus.para_bonus.vi}%、ONで加算)
                </span>
              </span>
            </label>
          )}

          {selected && effectiveBase && effectivePara && (
            <div className="mt-2 text-xs text-gray-600 bg-gray-50 rounded p-2 leading-relaxed">
              <div>
                基礎+{effectiveBase.vo}/
                {effectiveBase.da}/
                {effectiveBase.vi}
              </div>
              <div>
                パラボ Vo+{effectivePara.vo}% Da+{effectivePara.da}% Vi+{effectivePara.vi}%
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
