import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/index.css'
import App from './App'
import { runLegacyMigration } from './services/legacyMigration'

function render() {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}

// 旧 URL のブラウザ保存データを引き継ぐ往復 (別ページへの遷移・取り込み後の再読込) の間は描画しない。
// 引き継ぎ処理が失敗しても通常どおり描画する。
runLegacyMigration()
  .catch(() => false)
  .then((navigating) => {
    if (!navigating) render()
  })
