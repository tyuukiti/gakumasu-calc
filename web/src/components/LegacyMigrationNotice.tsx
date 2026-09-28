import { useState } from 'react';
import { consumeMigrationNotice } from '../services/legacyMigration';

/**
 * 旧 URL からブラウザ保存データを引き継いだ直後に一度だけ出す案内。
 * 引き継ぐものが無かった (新規の利用者) 場合は何も出さない。閉じるか次の読込で消える。
 */
export default function LegacyMigrationNotice() {
  const [count] = useState(() => consumeMigrationNotice());
  const [closed, setClosed] = useState(false);
  if (count <= 0 || closed) return null;
  return (
    <div className="bg-green-50 border-b border-green-200 text-green-900 text-sm px-4 py-2 flex flex-wrap items-center justify-center gap-x-4 gap-y-1">
      <span>以前の URL（tyuukiti.github.io）で登録していたサポカの所持情報や設定を、この URL に引き継ぎました。</span>
      <button type="button" onClick={() => setClosed(true)} className="underline hover:opacity-70">
        閉じる
      </button>
    </div>
  );
}
