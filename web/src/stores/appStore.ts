import { create } from 'zustand'
import type { SupportCard, TrainingPlan, EventCountTemplate } from '../types/models'
import type { CardInventoryEntry } from '../types/inventory'
import { loadSupportCards, loadTrainingPlans, loadEventCountTemplates } from '../services/yamlLoader'
import { loadInventory } from '../services/inventory'
import { trackEvent, setUserProperties } from '../utils/analytics'

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
    const startTime = Date.now()
    try {
      const [cards, plans, templates] = await Promise.all([
        loadSupportCards(),
        loadTrainingPlans(),
        loadEventCountTemplates(),
      ])
      const inventory = loadInventory()
      set({ cards, plans, templates, inventory, isLoading: false })

      // ユーザープロパティ設定
      const ownedCount = inventory.filter(e => e.owned).length;
      const avgUncap = ownedCount > 0
        ? +(inventory.filter(e => e.owned).reduce((s, e) => s + e.uncap, 0) / ownedCount).toFixed(1)
        : 0;
      setUserProperties({
        owned_card_count: ownedCount,
        avg_uncap_level: avgUncap,
        total_card_count: cards.length,
        has_inventory: ownedCount > 0,
      });

      trackEvent('data_load_completed', {
        load_time_ms: Date.now() - startTime,
        cards_count: cards.length,
        plans_count: plans.length,
        templates_count: templates.length,
      })
    } catch (e) {
      set({ error: (e as Error).message, isLoading: false })
      trackEvent('data_load_error', { error_message: (e as Error).message })
    }
  },

  setInventory: (inventory) => set({ inventory }),
}))
