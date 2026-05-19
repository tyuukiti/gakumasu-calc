import { useState } from 'react';
import { useHifStore } from '../../stores/hifStore';
import {
  type HifBonusLevels,
  HIF_BONUS_MAX_LEVELS,
  HIF_STAT_UP_FLAT, HIF_STAT_UP_PARA,
  HIF_FINAL_CAP_BONUS,
} from '../../types/hifBonus';

interface PanelDef {
  key: keyof HifBonusLevels;
  label: string;
  /** 各 Lv の効果テキストを返す。Lv 0 (未解放) は別表示 */
  effectAt: (lv: number) => string;
}

/** 計算に関与するパネルのみ表示 (Vo/Da/Vi 上昇 + 本戦パラメータ上限増加) */
const PANELS: PanelDef[] = [
  {
    key: 'voUpLevel', label: 'ボーカル上昇',
    effectAt: (lv) => `+${HIF_STAT_UP_FLAT[lv]} / +${HIF_STAT_UP_PARA[lv]}%`,
  },
  {
    key: 'daUpLevel', label: 'ダンス上昇',
    effectAt: (lv) => `+${HIF_STAT_UP_FLAT[lv]} / +${HIF_STAT_UP_PARA[lv]}%`,
  },
  {
    key: 'viUpLevel', label: 'ビジュアル上昇',
    effectAt: (lv) => `+${HIF_STAT_UP_FLAT[lv]} / +${HIF_STAT_UP_PARA[lv]}%`,
  },
  {
    key: 'finalStatLimitLevel', label: '【本戦】パラメータ上限増加',
    effectAt: (lv) => `+${HIF_FINAL_CAP_BONUS[lv]}`,
  },
];

/**
 * HIFモード固有のボーナス設定UI。
 * - 各パネルのレベルをスライダーで設定
 * - デフォルトMAX、下げたいユーザだけ下げる
 * - 計算に影響しないパネルもUIには表示 (進行度トラッキング目的)
 */
export default function HifBonusConfig() {
  const levels = useHifStore((s) => s.bonusLevels);
  const setLevel = useHifStore((s) => s.setBonusLevel);
  const reset = useHifStore((s) => s.resetBonusLevels);
  const [expanded, setExpanded] = useState(false);

  // 全パネルが MAX か判定
  const allMax = PANELS.every((p) => levels[p.key] >= HIF_BONUS_MAX_LEVELS[p.key]);

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
        HIFボーナス
        <span className="text-xs font-normal text-gray-500">
          {allMax ? '— 全パネル MAX' : '— 一部 MAX 未満'}
        </span>
      </button>
      {expanded && (
        <div className="border border-gray-200 rounded-md bg-gray-50 p-3 space-y-2">
          <div className="flex items-center justify-between text-xs text-gray-600">
            <span>各パネルのレベルを設定 (デフォルト MAX)</span>
            <button
              type="button"
              onClick={reset}
              className="px-2 py-0.5 bg-white border border-gray-300 rounded text-xs hover:border-[var(--color-accent)] cursor-pointer"
            >
              全パネル MAX に戻す
            </button>
          </div>

          <table className="w-full text-xs">
            <thead>
              <tr className="text-left text-gray-500 border-b border-gray-200">
                <th className="py-1">パネル</th>
                <th className="py-1 w-32 text-center">Lv</th>
                <th className="py-1 w-32 text-right">効果</th>
              </tr>
            </thead>
            <tbody>
              {PANELS.map((p) => {
                const maxLv = HIF_BONUS_MAX_LEVELS[p.key];
                const lv = levels[p.key];
                return (
                  <tr key={p.key} className="border-b border-gray-100 last:border-b-0">
                    <td className="py-1.5">
                      <span className="text-gray-700">{p.label}</span>
                    </td>
                    <td className="py-1.5 text-center">
                      <div className="flex items-center justify-center gap-1">
                        <button
                          type="button"
                          onClick={() => setLevel(p.key, Math.max(0, lv - 1))}
                          disabled={lv <= 0}
                          className="w-5 h-5 bg-white border border-gray-300 rounded text-xs cursor-pointer hover:border-[var(--color-accent)] disabled:opacity-30 disabled:cursor-not-allowed"
                        >
                          −
                        </button>
                        <span className="w-8 text-center font-mono">
                          {lv}/{maxLv}
                        </span>
                        <button
                          type="button"
                          onClick={() => setLevel(p.key, Math.min(maxLv, lv + 1))}
                          disabled={lv >= maxLv}
                          className="w-5 h-5 bg-white border border-gray-300 rounded text-xs cursor-pointer hover:border-[var(--color-accent)] disabled:opacity-30 disabled:cursor-not-allowed"
                        >
                          ＋
                        </button>
                      </div>
                    </td>
                    <td className="py-1.5 text-right font-mono text-gray-600">
                      {lv > 0 ? p.effectAt(lv) : <span className="text-gray-400">未解放</span>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
