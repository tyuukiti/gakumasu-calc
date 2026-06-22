import { useRef } from 'react';
import type { ExamAllocation } from '../../stores/hifStore';

type Stat = 'vo' | 'da' | 'vi';
const STAT_ORDER: Stat[] = ['vo', 'da', 'vi'];
const STAT_LABEL: Record<Stat, string> = { vo: 'Vo', da: 'Da', vi: 'Vi' };
/** バー塗り用（明るい配色） */
const STAT_FILL: Record<Stat, string> = {
  vo: 'var(--color-vo)',
  da: 'var(--color-da)',
  vi: 'var(--color-vi)',
};

/**
 * 全試験共通の配分比率を 1本のバーで編集するコントロール。合計は常に100。
 *
 * 配分が0の属性（2極・全振り時の対象外属性）はセグメント・ハンドルとも非表示にし、
 * 残った有効属性間の境界だけをドラッグできる。これにより VoVi 2極(Da=0) などで
 * 境界をドラッグしても 0 の属性が復活せず、0 のまま固定される。
 */
export default function ExamRatioBar({
  ratio,
  onChange,
}: {
  ratio: ExamAllocation;
  onChange: (r: ExamAllocation) => void;
}) {
  const ref = useRef<HTMLDivElement>(null);
  // ドラッグ中の境界 index と、開始時点の有効属性リストを固定保持する。
  // (ドラッグ中に属性が0になっても境界が壊れず、同一ドラッグ内で戻せるようにするため)
  const drag = useRef<{ i: number; active: Stat[] } | null>(null);

  // クリック位置 → 0..100 の値
  const toVal = (clientX: number) => {
    const el = ref.current;
    if (!el) return 0;
    const rect = el.getBoundingClientRect();
    const r = (clientX - rect.left) / rect.width;
    return Math.max(0, Math.min(100, Math.round(r * 100)));
  };

  // 配分が0より大きい属性のみ表示・操作対象にする (Vo→Da→Vi 順)
  const activeStats = STAT_ORDER.filter((s) => ratio[s] > 0);

  // 有効属性を左から並べたセグメント（左端 = それより前の有効属性の合計）
  const segs = activeStats.map((s, idx) => ({
    s,
    left: activeStats.slice(0, idx).reduce((sum, k) => sum + ratio[k], 0),
    width: ratio[s],
  }));
  // 隣り合う有効属性の境界（n-1 本）。位置 = 左側属性までの累積
  const boundaries = segs.slice(0, -1).map((seg, i) => ({
    i,
    left: seg.left + seg.width,
    a: activeStats[i],
    b: activeStats[i + 1],
  }));

  const onMove = (e: React.PointerEvent) => {
    const d = drag.current;
    if (!d) return;
    const a = d.active[d.i];
    const b = d.active[d.i + 1];
    if (a == null || b == null) return;
    // a の左端 = a より前の有効属性の合計（境界ドラッグでは不変）
    let leftBase = 0;
    for (let k = 0; k < d.i; k++) leftBase += ratio[d.active[k]];
    const sumAB = ratio[a] + ratio[b]; // a と b の合計は不変（両者の間でのみ移動）
    const v = toVal(e.clientX);
    const newA = Math.max(0, Math.min(sumAB, v - leftBase));
    onChange({ ...ratio, [a]: newA, [b]: sumAB - newA });
  };

  const start = (i: number) => (e: React.PointerEvent) => {
    e.preventDefault();
    drag.current = { i, active: activeStats };
    // ハンドルではなくコンテナで捕捉する（ドラッグ中にハンドル数が変わっても継続できる）
    ref.current?.setPointerCapture(e.pointerId);
  };
  const end = (e: React.PointerEvent) => {
    drag.current = null;
    try { ref.current?.releasePointerCapture(e.pointerId); } catch { /* noop */ }
  };

  return (
    <div
      ref={ref}
      onPointerMove={onMove}
      onPointerUp={end}
      className="relative h-9 rounded-md overflow-hidden select-none border border-gray-200"
    >
      {segs.map((g) => (
        <div
          key={g.s}
          className="absolute inset-y-0 flex items-center justify-center text-[11px] font-bold text-white"
          style={{ left: `${g.left}%`, width: `${g.width}%`, backgroundColor: STAT_FILL[g.s] }}
        >
          {g.width > 14 && <span className="drop-shadow">{STAT_LABEL[g.s]} {ratio[g.s]}%</span>}
        </div>
      ))}
      {boundaries.map((h) => (
        <div
          key={h.i}
          role="slider"
          aria-label={`${STAT_LABEL[h.a]}/${STAT_LABEL[h.b]} 境界`}
          aria-valuenow={h.left}
          aria-valuemin={0}
          aria-valuemax={100}
          tabIndex={0}
          onPointerDown={start(h.i)}
          className="absolute top-0 bottom-0 -ml-2 w-4 flex items-center justify-center cursor-ew-resize"
          style={{ left: `${h.left}%`, touchAction: 'none' }}
        >
          <span className="w-1 h-full bg-white/80 shadow-[0_0_0_1px_rgba(0,0,0,0.25)] rounded-full" />
        </div>
      ))}
    </div>
  );
}
