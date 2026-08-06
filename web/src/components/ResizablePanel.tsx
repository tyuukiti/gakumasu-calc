import { useLayoutEffect, useRef } from 'react';
import type { ReactNode } from 'react';

/** これ未満の高さは保存しない (誤操作でつぶれた状態を記憶しないため) */
const MIN_HEIGHT = 120;

/**
 * 右下のグリップで縦方向にリサイズでき、変更後の高さを localStorage に記憶するスクロール領域。
 * リサイズ非対応環境 (モバイル等) では defaultHeight 固定のスクロール領域として動作する。
 */
export default function ResizablePanel({
  storageKey,
  defaultHeight,
  className = '',
  children,
}: {
  /** 高さを保存する localStorage のキー */
  storageKey: string;
  /** 保存済みの高さがない場合の初期高さ (px) */
  defaultHeight: number;
  className?: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const stored = Number(localStorage.getItem(storageKey));
    el.style.height = `${stored >= MIN_HEIGHT ? stored : defaultHeight}px`;

    // CSS resize にはドラッグ完了イベントがないため、ResizeObserver + debounce で保存する
    let timer: number | undefined;
    const observer = new ResizeObserver(() => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        const h = Math.round(el.getBoundingClientRect().height);
        if (h >= MIN_HEIGHT) localStorage.setItem(storageKey, String(h));
      }, 200);
    });
    observer.observe(el);
    return () => {
      observer.disconnect();
      window.clearTimeout(timer);
    };
  }, [storageKey, defaultHeight]);

  return (
    <div
      ref={ref}
      className={`overflow-y-auto resize-y ${className}`}
      style={{ minHeight: MIN_HEIGHT }}
    >
      {children}
    </div>
  );
}
