import { BrowserRouter, Routes, Route, NavLink, Navigate } from 'react-router-dom'
import { useEffect } from 'react'
import { useAppStore } from './stores/appStore'
import { useCalcStore } from './stores/calcStore'
import CalculatorPage from './pages/CalculatorPage'
import InventoryPage from './pages/InventoryPage'
import HifPage from './pages/HifPage'
import UsagePage from './pages/UsagePage'

// デフォルト表示タブ。新シナリオが増えたらここを変更する。
const DEFAULT_PATH = '/hif'

function Header() {
  const navClass = ({ isActive }: { isActive: boolean }) =>
    `hover:opacity-80 ${isActive ? 'border-b-2 border-white' : 'opacity-70'}`
  return (
    <header className="bg-[var(--color-accent)] text-white px-6 py-3 flex items-center gap-6">
      <h1 className="text-lg font-bold">学マス 育成計算ツール</h1>
      <nav className="flex gap-4">
        <NavLink to="/hif" className={navClass}>HIF</NavLink>
        <NavLink to="/legend" className={navClass}>初レジェンド</NavLink>
        <NavLink to="/nia" className={navClass}>NIA</NavLink>
        <NavLink to="/inventory" className={navClass}>所持管理</NavLink>
        <NavLink to="/usage" className={navClass}>使い方</NavLink>
      </nav>
    </header>
  )
}

function Footer() {
  return (
    <footer className="text-center text-sm text-gray-400 py-4 mt-8 border-t border-gray-200">
      <div className="flex items-center justify-center gap-4">
        <a
          href="https://github.com/tyuukiti/gakumasu-calc"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:text-gray-600 transition-colors"
        >
          GitHub
        </a>
        <a
          href="https://x.com/nakayoshi_2nd"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:text-gray-600 transition-colors"
        >
          X @中吉
        </a>
      </div>
    </footer>
  )
}

export default function App() {
  const { isLoading, error, initialize } = useAppStore()

  useEffect(() => {
    // 所持データがあれば「所持カードのみで計算」を自動ON（デスクトップ版 MainViewModel と整合）
    initialize().then(() => {
      const inventory = useAppStore.getState().inventory
      if (inventory.some((e) => e.owned)) {
        useCalcStore.getState().setOwnedOnly(true)
      }
    })
  }, [initialize])

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-screen">
        <p className="text-lg text-gray-500">データを読み込み中...</p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex items-center justify-center h-screen">
        <p className="text-lg text-red-500">読み込みエラー: {error}</p>
      </div>
    )
  }

  return (
    <BrowserRouter basename="/gakumasu-calc/">
      <Header />
      <main className="max-w-5xl mx-auto px-4 py-6">
        <Routes>
          <Route path="/" element={<Navigate to={DEFAULT_PATH} replace />} />
          <Route path="/hif" element={<HifPage />} />
          <Route path="/legend" element={<CalculatorPage fixedPlanId="hatsu_legend" heading="初レジェンド" />} />
          <Route path="/nia" element={<CalculatorPage fixedPlanId="nia" heading="NIA" />} />
          <Route path="/inventory" element={<InventoryPage />} />
          <Route path="/usage" element={<UsagePage />} />
        </Routes>
      </main>
      <Footer />
    </BrowserRouter>
  )
}
