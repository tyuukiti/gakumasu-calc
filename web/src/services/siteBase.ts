/**
 * 配信場所のベースパス。同じビルドを旧 URL (https://tyuukiti.github.io/gakumasu-calc/) と
 * カスタムドメイン (https://gakumasu.tyuukiti.com/) の両方で動かすため、ビルド時ではなく実行時に決める。
 * index.html 冒頭の <base> 注入スクリプトと同じ規則 (テストで照合)。
 *
 * これにより、カスタムドメインに問題が出たときは GitHub の設定でドメインを外すだけで旧 URL に戻せる
 * (リバートや再デプロイが不要)。
 */
export const GITHUB_PAGES_PROJECT_BASE = '/gakumasu-calc/';

/** ホスト名から配信場所のベースパスを返す。*.github.io ならプロジェクトパス配下、それ以外 (カスタムドメイン・ローカル) はルート。 */
export function siteBasePath(
  hostname: string = typeof window === 'undefined' ? '' : window.location.hostname,
): string {
  return hostname.endsWith('.github.io') ? GITHUB_PAGES_PROJECT_BASE : '/';
}
