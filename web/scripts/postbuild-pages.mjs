// ビルド後処理: GitHub Pages 向けにルート別の静的 index.html / 404.html / sitemap.xml を生成する。
//
// 背景:
//   GitHub Pages は「フォルダにあるファイルをそのまま返す」だけなので、SPA(BrowserRouter) の
//   /hif や /legend に直接アクセスすると該当ファイルが無く HTTP 404 になる。404.html で
//   アプリ自体は起動するが、ステータスが 404 のままなので検索エンジンにはインデックスされない。
//
// やること:
//   1. dist/<route>.html を生成 (GitHub Pages は /hif へのリクエストに hif.html を 200 で返す。
//      hif/index.html 形式だと /hif → /hif/ の 301 が挟まり、canonical や sitemap の URL とずれる)。
//      中身は dist/index.html のコピーに、ルート固有の title / description / canonical / OGP と、
//      #root 内の静的説明文 (seo:start〜seo:end) を差し替えたもの。React がマウントすると
//      #root 配下は置き換わるので、アプリの表示・挙動は全ルートで同一。
//   2. dist/404.html = dist/index.html (未知パスの SPA フォールバック。従来の copy-404 相当)
//   3. dist/sitemap.xml を全ルート分生成 (public/ に静的ファイルは置かず、ここで一元管理)
//
// vite の base が絶対パス (/gakumasu-calc/) なので、どのパスから読まれてもアセットは正しく解決される。
import { readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const DIST = 'dist'
const SITE = 'https://tyuukiti.github.io/gakumasu-calc'
const SITE_NAME = 'GakumasuCalc'

/**
 * ルート定義。path は先頭スラッシュなし。
 * title: 60字前後。検索語 (シナリオ名・サポカ・編成・理論値) を前方に置く。
 * description: 120字前後。
 * intro: #root 内に置く静的 HTML (検索エンジンが読む本文)。<div> を入れ子にしないこと (置換の都合)。
 */
const ROUTES = [
  {
    path: 'hif',
    title: 'H.I.F サポカ編成 最適化・理論値計算 | GakumasuCalc（学マス）',
    description:
      '学マスの H.I.F（Hatsuboshi IDOL FESTIVAL）で、所持サポカと凸数からステータス理論値が最大になる6枚編成を自動算出。選抜試験20日＋本戦9行程のスケジュール、試験配分、HIFボーナスLv、キャラ補正、持ち込みメモリーに対応。',
    heading: 'H.I.F（Hatsuboshi IDOL FESTIVAL）サポカ編成の最適化・理論値計算',
    intro: [
      '学園アイドルマスター（学マス）の H.I.F シナリオ向けに、所持しているサポートカードと凸数から、到達ステータスの理論値が最大になる6枚編成（所持5枚＋レンタル1枚）を自動で選び出します。',
      '選抜試験（Day 1〜20）と本戦（Day 21〜29）の各日の行動選択、試験でのパラメータ配分、HIFボーナスLv、SPレッスン回数、キャラ固有補正、持ち込みメモリーを入力条件として計算します。入力条件一式は「全体プリセット」として保存し、Vo全踏み・Da全踏みのような条件比較を1クリックで行えます。',
      '各属性の上限に張り付いた分は切り捨てて評価するため、上限超過分を別の属性に回せるカードが優先されます。',
    ],
  },
  {
    path: 'legend',
    title: '初レジェンド サポカ編成 最適化・理論値計算 | GakumasuCalc（学マス）',
    description:
      '学マスの初レジェンド（初Legend）で、所持サポカと凸数からステータス理論値が最大になる6枚編成を自動算出。18週のスケジュール選択、SPレッスン回数、キャラ補正、持ち込みメモリーに対応。',
    heading: '初レジェンド サポカ編成の最適化・理論値計算',
    intro: [
      '学園アイドルマスター（学マス）の初レジェンド（初Legend）シナリオ向けに、所持しているサポートカードと凸数から、到達ステータスの理論値が最大になる6枚編成（所持5枚＋レンタル1枚）を自動で選び出します。',
      '18週の各週でレッスン・授業・お出かけなどの行動を選択し、SPレッスン回数、キャラ固有補正、持ち込みメモリーを含めて到達ステータスを算出します。週ごとの上昇内訳と、編成パターン（メイン1／メイン2／フリーの配分）の比較も表示します。',
    ],
  },
  {
    path: 'nia',
    title: 'N.I.A サポカ編成 最適化・理論値計算 | GakumasuCalc（学マス）',
    description:
      '学マスの N.I.A（NIAマスター）で、所持サポカと凸数からステータス理論値が最大になる6枚編成を自動算出。26週のスケジュール選択、オーディション獲得パラメータ、キャラの流行・審査基準、持ち込みメモリーに対応。',
    heading: 'N.I.A（NIAマスター）サポカ編成の最適化・理論値計算',
    intro: [
      '学園アイドルマスター（学マス）の N.I.A（NIAマスター）シナリオ向けに、所持しているサポートカードと凸数から、到達ステータスの理論値が最大になる6枚編成（所持5枚＋レンタル1枚）を自動で選び出します。',
      '26週の各週の行動選択に加え、オーディションで獲得できるパラメータをキャラごとの流行・審査基準に基づいて計算に含めます。SPレッスン回数、キャラ固有補正、持ち込みメモリーにも対応しています。',
    ],
  },
  {
    path: 'inventory',
    title: 'サポカ所持管理 | GakumasuCalc（学マス サポカ最適化計算ツール）',
    description:
      '学マスの所持サポートカードと凸数を登録・管理するページ。登録内容はブラウザに保存され、各シナリオの編成計算で「所持カードのみで計算」に使われます。',
    heading: 'サポカ所持管理',
    intro: [
      '所持しているサポートカードと凸数（0〜4凸）を登録するページです。登録した内容はブラウザ内に保存され、H.I.F・初レジェンド・N.I.A の各計算タブで「所持カードのみで計算」を有効にすると、この所持データを元に編成が選ばれます。',
      '未所持のカードは計算時にレンタル枠（4凸借用）の候補になります。',
    ],
  },
  {
    path: 'usage',
    title: '使い方 | GakumasuCalc（学マス サポカ最適化計算ツール）',
    description:
      'GakumasuCalc の使い方。シナリオを選び、育成タイプや条件を設定して「計算実行」を押すと、最適なサポカ編成と到達ステータスの理論値が表示されます。コンテストモードの説明も掲載。',
    heading: 'GakumasuCalc の使い方',
    intro: [
      '上部のタブでシナリオ（H.I.F／初レジェンド／N.I.A）を選び、育成タイプやスケジュールなどの条件を設定して「計算実行」を押すと、最適なサポートカード編成と各属性の到達ステータスが表示されます。',
      'コンテストモードを有効にすると、スキルカード付き・コンテストアイテム付きのサポートカードを編成候補から除外して、メモリー育成向けの編成を計算します。ご要望や不具合は GitHub の Issue で受け付けています。',
    ],
  },
]

const escapeHtml = (s) =>
  s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

/** pattern に一致する箇所を replacement で置換する。一致しなければビルドを止める (index.html の構造変更を検知するため)。 */
function replaceOrThrow(html, pattern, replacement, what) {
  if (!pattern.test(html)) {
    throw new Error(`postbuild-pages: ${what} が dist/index.html に見つかりません。index.html の構造が変わっていないか確認してください。`)
  }
  return html.replace(pattern, replacement)
}

function setTitle(html, title) {
  return replaceOrThrow(html, /<title>[^<]*<\/title>/, `<title>${escapeHtml(title)}</title>`, '<title>')
}

/** <meta name="..."> / <meta property="..."> の content を差し替える。 */
function setMeta(html, attr, key, content) {
  const pattern = new RegExp(`<meta ${attr}="${key}" content="[^"]*"\\s*/?>`)
  return replaceOrThrow(html, pattern, `<meta ${attr}="${key}" content="${escapeHtml(content)}" />`, `<meta ${attr}="${key}">`)
}

function setCanonical(html, url) {
  return replaceOrThrow(html, /<link rel="canonical" href="[^"]*"\s*\/?>/, `<link rel="canonical" href="${url}" />`, 'canonical')
}

function setSeoBlock(html, inner) {
  return replaceOrThrow(
    html,
    /<!-- seo:start -->[\s\S]*?<!-- seo:end -->/,
    `<!-- seo:start -->\n${inner}\n      <!-- seo:end -->`,
    'seo:start〜seo:end',
  )
}

/** ルート固有の静的本文。ルートの index.html と同じ見た目 (インラインスタイル) にそろえる。 */
function buildIntro(route) {
  const paragraphs = route.intro.map((p) => `        <p>${escapeHtml(p)}</p>`).join('\n')
  const links = ROUTES.filter((r) => r.path !== route.path)
    .map((r) => `          <li><a href="/gakumasu-calc/${r.path}">${escapeHtml(r.heading)}</a></li>`)
    .join('\n')
  return [
    `      <div class="seo-intro" style="max-width: 64rem; margin: 0 auto; padding: 1.5rem 1rem; font-family: 'Segoe UI', 'Hiragino Kaku Gothic ProN', 'Meiryo', sans-serif; color: #333; line-height: 1.7;">`,
    `        <h1 style="font-size: 1.25rem;">${escapeHtml(route.heading)}</h1>`,
    paragraphs,
    `        <ul>`,
    `          <li><a href="/gakumasu-calc/">${SITE_NAME} トップ（学マス サポカ編成 最適化計算ツール）</a></li>`,
    links,
    `        </ul>`,
    `      </div>`,
  ].join('\n')
}

function buildRoutePage(rootHtml, route) {
  const url = `${SITE}/${route.path}`
  let html = rootHtml
  html = setTitle(html, route.title)
  html = setMeta(html, 'name', 'description', route.description)
  html = setCanonical(html, url)
  html = setMeta(html, 'property', 'og:title', route.title)
  html = setMeta(html, 'property', 'og:description', route.description)
  html = setMeta(html, 'property', 'og:url', url)
  html = setMeta(html, 'name', 'twitter:title', route.title)
  html = setMeta(html, 'name', 'twitter:description', route.description)
  html = setSeoBlock(html, buildIntro(route))
  return html
}

function buildSitemap() {
  const today = new Date().toISOString().slice(0, 10)
  const entry = (loc, priority) =>
    `  <url>\n    <loc>${loc}</loc>\n    <lastmod>${today}</lastmod>\n    <changefreq>weekly</changefreq>\n    <priority>${priority}</priority>\n  </url>`
  const urls = [
    entry(`${SITE}/`, '1.0'),
    ...ROUTES.map((r) => entry(`${SITE}/${r.path}`, r.path === 'usage' ? '0.5' : '0.8')),
  ]
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${urls.join('\n')}\n</urlset>\n`
}

const rootHtml = readFileSync(join(DIST, 'index.html'), 'utf8')

for (const route of ROUTES) {
  const file = join(DIST, `${route.path}.html`)
  writeFileSync(file, buildRoutePage(rootHtml, route))
  console.log(`Created ${file}`)
}

writeFileSync(join(DIST, '404.html'), rootHtml)
console.log('Created dist/404.html (SPA fallback for GitHub Pages)')

writeFileSync(join(DIST, 'sitemap.xml'), buildSitemap())
console.log(`Created dist/sitemap.xml (${ROUTES.length + 1} urls)`)
