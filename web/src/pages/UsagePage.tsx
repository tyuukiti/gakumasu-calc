import type { ReactNode } from 'react';

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="bg-white rounded-lg p-4 shadow-sm">
      <h3 className="font-bold text-base mb-2 text-gray-800">{title}</h3>
      {children}
    </section>
  );
}

export default function UsagePage() {
  return (
    <div className="space-y-4">
      <h2 className="text-xl font-bold">使い方</h2>

      <Section title="このツールについて">
        <p className="text-sm text-gray-700 leading-relaxed">
          学園アイドルマスター（学マス）の育成で、
          <span className="font-semibold">サポートカード編成の理論値（到達ステータスの目安）</span>
          を計算するツールです。シナリオ・育成タイプ・各種条件を設定すると、最適なサポカ編成と各属性の到達ステータスを自動で算出します。
        </p>
      </Section>

      <Section title="基本的な使い方">
        <ol className="list-decimal list-inside space-y-1.5 text-sm text-gray-700">
          <li>上部のタブで<span className="font-semibold">シナリオ</span>（HIF / 初レジェンド / NIA）を選ぶ</li>
          <li><span className="font-semibold">育成タイプ</span>や<span className="font-semibold">属性設定</span>など、各項目を設定する</li>
          <li><span className="font-semibold">「計算実行」</span>ボタンを押す</li>
          <li><span className="font-semibold">到達ステータス・編成パターン・週別内訳</span>が表示される</li>
        </ol>
      </Section>

      <Section title="ご要望・不具合の報告">
        <p className="text-sm text-gray-700 leading-relaxed">
          ご要望や不具合の報告は <span className="font-semibold">GitHub の Issue</span> でお知らせください。GitHubの操作が難しい場合は、X（旧Twitter）へのご連絡でも構いません。
        </p>
        <div className="flex flex-wrap gap-2 mt-3">
          <a
            href="https://github.com/tyuukiti/gakumasu-calc/issues/new"
            target="_blank"
            rel="noopener noreferrer"
            className="px-4 py-2 bg-[var(--color-accent)] text-white rounded text-sm font-bold hover:opacity-90"
          >
            GitHubでIssueを立てる
          </a>
          <a
            href="https://x.com/nakayoshi_2nd"
            target="_blank"
            rel="noopener noreferrer"
            className="px-4 py-2 border border-gray-300 text-gray-700 rounded text-sm font-bold hover:bg-gray-50"
          >
            X（@中吉）に連絡
          </a>
        </div>
      </Section>
    </div>
  );
}
