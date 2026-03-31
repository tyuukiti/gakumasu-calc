import { create } from 'zustand'
import type { SupportCard, TrainingPlan, EventCountTemplate } from '../types/models'
import type { CardInventoryEntry } from '../types/inventory'
import { loadSupportCards, loadTrainingPlans, loadEventCountTemplates } from '../services/yamlLoader'
import { loadInventory } from '../services/inventory'

interface AppState {
  cards: SupportCard[]
  plans: TrainingPlan[]
  templates: EventCountTemplate[]
  inventory: CardInventoryEntry[]
  isLoading: boolean
  error: string | null
  initialize: () => Promise<void>
  setInventory: (inventory: CardInventoryEntry[]) => void
}

export const useAppStore = create<AppState>((set) => ({
  cards: [],
  plans: [],
  templates: [],
  inventory: [],
  isLoading: true,
  error: null,

  initialize: async () => {
    try {
      const [cards, plans, templates] = await Promise.all([
        loadSupportCards(),
        loadTrainingPlans(),
        loadEventCountTemplates(),
      ])
      const inventory = loadInventory()
      set({ cards, plans, templates, inventory, isLoading: false })
      console.log(`読み込み完了: カード${cards.length}枚, プラン${plans.length}件, テンプレート${templates.length}件`)
    } catch (e) {
      set({ error: (e as Error).message, isLoading: false })
    }
  },

  setInventory: (inventory) => set({ inventory }),
}))
