import { BrowserRouter, Routes, Route, NavLink, useLocation } from 'react-router-dom'
import { useEffect } from 'react'
import { useAppStore } from './stores/appStore'
import CalculatorPage from './pages/CalculatorPage'
import InventoryPage from './pages/InventoryPage'
import { trackEvent } from './utils/analytics'

function PageViewTracker() {
  const location = useLocation()
  useEffect(() => {
    trackEvent('page_view', { page_path: location.pathname })
  }, [location.pathname])
  return null
}

function Header() {
  return (
    <header className="bg-[var(--color-accent)] text-white px-6 py-3 flex items-center gap-6">
      <h1 className="text-lg font-bold">学マス 育成計算ツール</h1>
      <nav className="flex gap-4">
        <NavLink to="/" end className={({ isActive }) =>
          `hover:opacity-80 ${isActive ? 'border-b-2 border-white' : 'opacity-70'}`
        }>計算ツール</NavLink>
        <NavLink to="/inventory" className={({ isActive }) =>
          `hover:opacity-80 ${isActive ? 'border-b-2 border-white' : 'opacity-70'}`
        }>所持管理</NavLink>
      </nav>
    </header>
  )
}

export default function App() {
  const { isLoading, error, initialize } = useAppStore()

  useEffect(() => {
    initialize()
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
      <PageViewTracker />
      <Header />
      <main className="max-w-5xl mx-auto px-4 py-6">
        <Routes>
          <Route path="/" element={<CalculatorPage />} />
          <Route path="/inventory" element={<InventoryPage />} />
        </Routes>
      </main>
    </BrowserRouter>
  )
}
