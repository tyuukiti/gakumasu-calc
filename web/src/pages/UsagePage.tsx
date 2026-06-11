import type { ReactNode } from 'react';

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="bg-white rounded-lg p-4 shadow-sm">
      <h3 className="font-bold text-base mb-2 text-gray-800">{title}</h3>
      {children}
    </section>
  );
}

/** 「用語 — 説明」形式の項目リスト */
function TermList({ items }: { items: { term: string; desc: ReactNode }[] }) {
  return (
    <ul className="space-y-2 text-sm text-gray-700">
      {items.map(({ term, desc }) => (
        <li key={term}>
          <span className="font-semibold text-gray-900">{term}</span>
          <span className="text-gray-400"> — </span>
          {desc}
        </li>
      ))}
    </ul>
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

      <Section title="シナリオタブ">
        <TermList
          items={[
            { term: 'HIF', desc: 'Hatsuboshi IDOL FESTIVAL。スケジュールを自分で組むシナリオ（起動時のデフォルト表示）。' },
            { term: '初レジェンド', desc: '初Legend育成プラン。' },
            { term: 'NIA', desc: 'NIA マスター育成プラン。' },
          ]}
        />
        <p className="text-xs text-gray-500 mt-2">
          各タブはシナリオ専用の計算画面です。設定値（育成タイプ・属性設定など）はタブ間で共有されます。
        </p>
      </Section>

      <Section title="共通の設定項目">
        <TermList
          items={[
            { term: '育成タイプ', desc: 'センス／ロジック／アノマリーから選択します。' },
            {
              term: '属性設定',
              desc: 'Vocal / Dance / Visual ごとに、役割（メイン1・メイン2・サブ）と SP率枚数（SP特訓を狙う枚数, 0〜6）を指定します。',
            },
            {
              term: 'レッスン内イベント回数',
              desc: 'Pドリンク獲得などのイベント回数。テンプレートを選ぶとよくある値が入り、必要なら微調整できます。',
            },
            { term: '必須カード / 除外カード', desc: '必ず編成に入れたいカード／外したいカードを指定します。' },
            { term: 'キャラ選択', desc: '育成するアイドルを選びます（キャラ補正が計算に反映されます）。' },
            { term: '持ち込みメモリー', desc: '育成に持ち込むメモリーのボーナス（最大4枚分）を入力します。' },
            {
              term: '所持カードのみで計算',
              desc: 'ONにすると、所持管理で登録したカード＋レンタル1枚の範囲で編成します。',
            },
            { term: 'コンテストモード', desc: 'スキルカード・コンテストアイテムのサポカを編成から除外します。' },
          ]}
        />
      </Section>

      <Section title="HIFモードの追加設定">
        <p className="text-sm text-gray-700 mb-2">
          HIFはスケジュールを自分で組むシナリオのため、専用の設定があります。
        </p>
        <TermList
          items={[
            {
              term: '一括設定',
              desc: '公開レッスン・授業の方針や、試験配分バー（Vo/Da/Viの比率を1本のバーで指定）をまとめて設定します。',
            },
            { term: 'スケジュール調整', desc: '各日の行動（レッスン／授業／お出かけ等）を個別に選べます。' },
            { term: 'プリセット', desc: '組んだスケジュールを保存・呼び出しできます。' },
            { term: 'HIFボーナス', desc: 'HIFのボーナスパネルのレベルを設定します。' },
            { term: 'SP枚数', desc: 'Vo / Da / Vi それぞれのSP枚数を個別に指定します。' },
            { term: '超過ペナルティ', desc: 'ステータス上限を大きく超える配分に補正をかけるオプションです。' },
          ]}
        />
      </Section>

      <Section title="所持管理">
        <p className="text-sm text-gray-700 leading-relaxed">
          持っているサポートカードを登録するタブです。カードをタップして所持／未所持を切り替え、凸数（限界突破レベル）も設定できます。絞り込み・並び替え、JSONでのインポート／エクスポートに対応しています。登録後に
          <span className="font-semibold">「所持カードのみで計算」</span>
          を使うと、手持ち前提の編成が出せます。
        </p>
      </Section>

      <Section title="結果の見方">
        <TermList
          items={[
            { term: '到達ステータス', desc: 'Vo / Da / Vi と合計の到達値（理論値の目安）。' },
            { term: '編成パターン', desc: '条件を満たす複数の編成案。それぞれの内訳カードを確認できます。' },
            { term: '週別内訳', desc: '各週・各日でどう行動し、ステータスがどう伸びるかの内訳。' },
          ]}
        />
      </Section>

      <div className="text-xs text-gray-500 leading-relaxed px-1">
        <p>※ 表示されるのは最適行動を前提とした理論値の目安で、実際のプレイ結果とは差が出ることがあります。</p>
        <p>※ カード・シナリオのデータはゲームの更新に合わせて順次追加・調整しています。</p>
      </div>
    </div>
  );
}
