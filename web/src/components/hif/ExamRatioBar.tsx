import { useRef } from 'react';
import type { ExamAllocation } from '../../stores/hifStore';

type Stat = 'vo' | 'da' | 'vi';
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
/** バー塗り用（明るい配色） */
const STAT_FILL: Record<Stat, string> = {
  vo: 'var(--color-vo)',
  da: 'var(--color-da)',
  vi: 'var(--color-vi)',
};

/**
 * 全試験共通の配分比率を 1本のバーで編集するコントロール。
 * 合計は常に100で固定（2つの境界ハンドルをドラッグして Vo/Da/Vi を配分）。
 */
export default function ExamRatioBar({
  ratio,
  onChange,
}: {
  ratio: ExamAllocation;
  onChange: (r: ExamAllocation) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const dragging = useRef<1 | 2 | null>(null);

  // クリック位置 → 0..100 の値
  const toVal = (clientX: number) => {
    const el = ref.current;
    if (!el) return 0;
    const rect = el.getBoundingClientRect();
    const r = (clientX - rect.left) / rect.width;
    return Math.max(0, Math.min(100, Math.round(r * 100)));
  };

  const onMove = (e: React.PointerEvent) => {
    if (!dragging.current) return;
    const v = toVal(e.clientX);
    if (dragging.current === 1) {
      // Vo|Da 境界: v = vo（Vi 固定、Da が補償）
      const voPlusDa = 100 - ratio.vi;
      const vo = Math.min(v, voPlusDa);
      onChange({ vo, da: voPlusDa - vo, vi: ratio.vi });
    } else {
      // Da|Vi 境界: v = vo+da（Vo 固定、Vi が補償）
      const voPlusDa = Math.max(ratio.vo, v);
      onChange({ vo: ratio.vo, da: voPlusDa - ratio.vo, vi: 100 - voPlusDa });
    }
  };

  const start = (which: 1 | 2) => (e: React.PointerEvent) => {
    e.preventDefault();
    dragging.current = which;
    (e.target as Element).setPointerCapture(e.pointerId);
  };
  const end = (e: React.PointerEvent) => {
    dragging.current = null;
    try { (e.target as Element).releasePointerCapture(e.pointerId); } catch { /* noop */ }
  };

  const b2 = ratio.vo + ratio.da;
  const segs: Array<{ s: Stat; left: number; width: number; val: number }> = [
    { s: 'vo', left: 0, width: ratio.vo, val: ratio.vo },
    { s: 'da', left: ratio.vo, width: ratio.da, val: ratio.da },
    { s: 'vi', left: b2, width: 100 - b2, val: ratio.vi },
  ];
  const handles: Array<{ left: number; which: 1 | 2; now: number }> = [
    { left: ratio.vo, which: 1, now: ratio.vo },
    { left: b2, which: 2, now: b2 },
  ];

  return (
    <div ref={ref} className="relative h-9 rounded-md overflow-hidden select-none border border-gray-200">
      {segs.map((g) => (
        <div
          key={g.s}
          className="absolute inset-y-0 flex items-center justify-center text-[11px] font-bold text-white"
          style={{ left: `${g.left}%`, width: `${g.width}%`, backgroundColor: STAT_FILL[g.s] }}
        >
          {g.width > 14 && <span className="drop-shadow">{STAT_LABEL[g.s]} {g.val}%</span>}
        </div>
      ))}
      {handles.map((h) => (
        <div
          key={h.which}
          role="slider"
          aria-label={h.which === 1 ? 'Vo/Da 境界' : 'Da/Vi 境界'}
          aria-valuenow={h.now}
          aria-valuemin={0}
          aria-valuemax={100}
          tabIndex={0}
          onPointerDown={start(h.which)}
          onPointerMove={onMove}
          onPointerUp={end}
          className="absolute top-0 bottom-0 -ml-2 w-4 flex items-center justify-center cursor-ew-resize"
          style={{ left: `${h.left}%`, touchAction: 'none' }}
        >
          <span className="w-1 h-full bg-white/80 shadow-[0_0_0_1px_rgba(0,0,0,0.25)] rounded-full" />
        </div>
      ))}
    </div>
  );
}
