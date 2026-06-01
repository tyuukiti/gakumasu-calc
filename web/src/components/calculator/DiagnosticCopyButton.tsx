import { useState } from 'react';
import { useCalcStore } from '../../stores/calcStore';
import { useAppStore } from '../../stores/appStore';
import { useHifStore } from '../../stores/hifStore';
import { buildDiagnosticReport, buildHifDiagnosticReport } from '../../services/diagnostics';
import { trackEvent } from '../../utils/analytics';

interface Props {
  /** 'calc' = 通常計算タブ / 'hif' = HIFタブ。診断レポートの内容を切り替える。 */
  mode?: 'calc' | 'hif';
}

/**
 * 現在の設定・選択編成・計算結果を平文でクリップボードへコピーするボタン。
 * 問題報告時に編成・設定・結果をそのまま貼り付けてもらう用途（常設）。
 */
export default function DiagnosticCopyButton({ mode = 'calc' }: Props = {}) {
  const [copied, setCopied] = useState(false);
  const [failed, setFailed] = useState(false);

  const buildReport = () =>
    mode === 'hif'
      ? buildHifDiagnosticReport(useHifStore.getState(), useCalcStore.getState(), useAppStore.getState())
      : buildDiagnosticReport(useCalcStore.getState(), useAppStore.getState());

  const handleCopy = async () => {
    const report = buildReport();
    try {
      await navigator.clipboard.writeText(report);
      trackEvent('diagnostic_copied', { length: report.length, mode });
      setCopied(true);
      setFailed(false);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // clipboard API 不可環境向けフォールバック
      try {
        const ta = document.createElement('textarea');
        ta.value = report;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        trackEvent('diagnostic_copied', { length: report.length, mode, fallback: true });
        setCopied(true);
        setFailed(false);
        setTimeout(() => setCopied(false), 2000);
      } catch {
        setFailed(true);
        setTimeout(() => setFailed(false), 2000);
      }
    }
  };

  return (
    <button
      type="button"
      onClick={handleCopy}
      className="text-sm px-3 py-1.5 border border-gray-300 rounded text-gray-600 hover:bg-gray-50 cursor-pointer"
      title="現在の設定・編成・計算結果を平文でコピーします（問題報告用）"
    >
      {copied ? '✓ コピーしました' : failed ? 'コピー失敗' : '📋 診断情報をコピー'}
    </button>
  );
}
