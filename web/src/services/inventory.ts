import type { CardInventoryEntry } from '../types/inventory'
import type { SupportCard } from '../types/models'

const STORAGE_KEY = 'gakumasu_inventory'

export function loadInventory(): CardInventoryEntry[] {
  try {
    const json = localStorage.getItem(STORAGE_KEY)
    if (!json) return []
    return JSON.parse(json) as CardInventoryEntry[]
  } catch {
    return []
  }
}

export function saveInventory(entries: CardInventoryEntry[]): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(entries))
}

export function initializeFromCards(
  allCards: SupportCard[],
  existing: CardInventoryEntry[]
): CardInventoryEntry[] {
  const existingMap = new Map(existing.map(e => [e.card_id, e]))
  return allCards.map(card => {
    const entry = existingMap.get(card.id)
    if (entry) return entry
    return { card_id: card.id, owned: false, uncap: 4 }
  })
}

export function exportInventoryJson(entries: CardInventoryEntry[]): string {
  return JSON.stringify(entries, null, 2)
}

export function importInventoryJson(json: string): CardInventoryEntry[] {
  const parsed = JSON.parse(json)
  if (!Array.isArray(parsed)) throw new Error('Invalid format')
  return parsed.map((e: CardInventoryEntry) => ({
    card_id: e.card_id,
    owned: !!e.owned,
    uncap: Math.min(Math.max(e.uncap ?? 4, 0), 4),
  }))
}
