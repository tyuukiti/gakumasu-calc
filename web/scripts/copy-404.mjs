// GitHub Pages は存在しないパスへのアクセス時に 404.html を返す。
// SPA(BrowserRouter)では /hif や /legend などの直接アクセス・リロード時に
// 該当ファイルが無く 404 になってしまうため、index.html を 404.html として
// 複製し、どのルートに直接アクセスしてもアプリが起動するようにする。
// (vite の base が絶対パスのため、深いパスから 404.html が返ってもアセットは正しく解決される)
import { copyFileSync } from 'node:fs'

copyFileSync('dist/index.html', 'dist/404.html')
console.log('Created dist/404.html (SPA fallback for GitHub Pages)')
