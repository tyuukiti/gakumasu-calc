import type { ReactNode, MouseEvent } from 'react';

interface Props {
  open: boolean;
  onToggle: (open: boolean) => void;
  title: ReactNode;
  /** 見出しの右に薄字で添える説明 (例: "（任意）"、"（一括設定で多くの週が設定済み）")。無ければ省略 */
  note?: ReactNode;
  /** 選択済みの値・現在の状態 (例: キャラ名、プリセット名、"設定済み")。説明より濃く強調して出す */
  status?: ReactNode;
  /** status が無いときに出す文言 (例: "未選択" / "未設定")。選択済みと区別できるよう薄めに出す */
  emptyStatus?: string;
  children?: ReactNode;
}

/**
 * 折りたたみセクション (全タブ共通)。
 * 構造はデジタル庁デザインシステム (DADS) のアコーディオンに倣う:
 * - details/summary でキーボード操作とスクリーンリーダー対応はブラウザ標準に任せる
 * - 左にシェブロン (閉: 下向き / 開: 上向き)、ヘッダー行全体がクリック対象でホバーで背景が付く
 * - 下に区切り線、本文はヘッダー文字の左端に揃える
 * 色・太さはこのアプリのトーン (グレー基調・アクセント色) に合わせ、DADS の丸枠や原色は使わない。
 * 開閉状態は呼び出し側で持ち (open / onToggle)、本文は open のときだけ描画してよい。
 */
export default function Accordion({ open, onToggle, title, note, status, emptyStatus, children }: Props) {
  const hasStatus = status != null && status !== '';
  const handleSummaryClick = (e: MouseEvent<HTMLElement>) => {
    // ブラウザ標準のトグルを止めて、呼び出し側の状態で開閉を制御する
    e.preventDefault();
    onToggle(!open);
  };

  return (
    <details open={open} className="group/accordion border-b border-gray-200">
      <summary
        onClick={handleSummaryClick}
        className="flex items-center gap-2 cursor-pointer select-none py-2.5 px-2 -mx-2 rounded-md text-sm font-semibold text-gray-800
          marker:[content:''] [&::-webkit-details-marker]:hidden
          hover:bg-gray-100
          focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--color-accent)]"
      >
        <svg
          aria-hidden="true"
          className="size-5 shrink-0 text-[var(--color-accent)] transition-transform group-open/accordion:rotate-180"
          viewBox="0 0 20 20"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M5 7.5l5 5 5-5" />
        </svg>
        <span>{title}</span>
        {note != null && note !== '' && (
          <span className="text-xs font-normal text-gray-400">{note}</span>
        )}
        {hasStatus ? (
          <span className="text-xs font-semibold text-gray-700">: {status}</span>
        ) : emptyStatus ? (
          <span className="text-xs font-normal text-gray-500">: {emptyStatus}</span>
        ) : null}
      </summary>
      <div className="pl-7 pr-2 pt-1 pb-4">
        {children}
      </div>
    </details>
  );
}
