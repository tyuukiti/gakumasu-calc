import { useState, useMemo, useCallback } from 'react';
import { useAppStore } from '../stores/appStore';
import { saveInventory, initializeFromCards, exportInventoryJson, importInventoryJson } from '../services/inventory';
import { trackEvent } from '../utils/analytics';
import type { CardInventoryEntry } from '../types/inventory';
import InventoryFilters from '../components/inventory/InventoryFilters';
import CardTile from '../components/inventory/CardTile';
import ImportExportPanel from '../components/inventory/ImportExportPanel';

const RARITY_ORDER: Record<string, number> = { SSR: 0, SR: 1, R: 2 };
const TYPE_ORDER: Record<string, number> = { vo: 0, da: 1, vi: 2, all: 3 };

function cardIdNumber(id: string): number {
  const m = id.match(/(\d+)$/);
  return m ? parseInt(m[1], 10) : 0;
}

export default function InventoryPage() {
  const { cards, inventory, setInventory } = useAppStore();
  const [filterText, setFilterText] = useState('');
  const [filterRarity, setFilterRarity] = useState('すべて');
  const [filterType, setFilterType] = useState('すべて');
  const [filterOwned, setFilterOwned] = useState('すべて');
  const [statusMessage, setStatusMessage] = useState('');

  // Initialize inventory from cards if needed
  const entries = useMemo(() => {
    return initializeFromCards(cards, inventory);
  }, [cards, inventory]);

  const entryMap = useMemo(() => {
    return new Map(entries.map(e => [e.card_id, e]));
  }, [entries]);

  const filtered = useMemo(() => {
    return cards
      .filter(card => {
        if (filterText && !card.name.toLowerCase().includes(filterText.toLowerCase())) return false;
        if (filterRarity !== 'すべて' && card.rarity !== filterRarity) return false;
        if (filterType !== 'すべて' && card.type !== filterType) return false;
        const entry = entryMap.get(card.id);
        if (filterOwned === '所持' && !entry?.owned) return false;
        if (filterOwned === '未所持' && entry?.owned) return false;
        return true;
      })
      .sort((a, b) => {
        const r = (RARITY_ORDER[a.rarity] ?? 3) - (RARITY_ORDER[b.rarity] ?? 3);
        if (r !== 0) return r;
        const t = (TYPE_ORDER[a.type] ?? 3) - (TYPE_ORDER[b.type] ?? 3);
        if (t !== 0) return t;
        return cardIdNumber(b.id) - cardIdNumber(a.id);
      });
  }, [cards, filterText, filterRarity, filterType, filterOwned, entryMap]);

  const updateEntry = useCallback((cardId: string, updater: (e: CardInventoryEntry) => CardInventoryEntry) => {
    const newEntries = entries.map(e => e.card_id === cardId ? updater({ ...e }) : e);
    setInventory(newEntries);
    saveInventory(newEntries);
  }, [entries, setInventory]);

  const ownedCount = entries.filter(e => e.owned).length;

  const handleExport = () => {
    const json = exportInventoryJson(entries);
    const blob = new Blob([json], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'gakumasu_inventory.json';
    a.click();
    URL.revokeObjectURL(url);
    setStatusMessage('エクスポートしました');
    trackEvent('inventory_exported', { owned_count: entries.filter(e => e.owned).length });
  };

  const handleImport = (json: string) => {
    try {
      const imported = importInventoryJson(json);
      const merged = initializeFromCards(cards, imported);
      setInventory(merged);
      saveInventory(merged);
      const ownedCount = imported.filter(e => e.owned).length;
      setStatusMessage(`インポートしました (${ownedCount}枚所持)`);
      trackEvent('inventory_imported', { owned_count: ownedCount });
    } catch {
      setStatusMessage('インポートエラー: 不正なJSONファイルです');
      trackEvent('inventory_import_error');
    }
  };

  const handleSelectAll = () => {
    const filteredIds = new Set(filtered.map(c => c.id));
    const newEntries = entries.map(e =>
      filteredIds.has(e.card_id) ? { ...e, owned: true } : e
    );
    setInventory(newEntries);
    saveInventory(newEntries);
    trackEvent('select_all_visible', { count: filtered.length });
  };

  const handleDeselectAll = () => {
    const filteredIds = new Set(filtered.map(c => c.id));
    const newEntries = entries.map(e =>
      filteredIds.has(e.card_id) ? { ...e, owned: false } : e
    );
    setInventory(newEntries);
    saveInventory(newEntries);
    trackEvent('deselect_all_visible', { count: filtered.length });
  };

  return (
    <div>
      <div className="mb-3">
        <h2 className="text-xl font-bold">サポートカード所持管理</h2>
        <p className="text-sm text-gray-500">
          所持: {ownedCount} / {entries.length} 枚 (表示: {filtered.length}枚)
        </p>
      </div>

      <InventoryFilters
        filterText={filterText} setFilterText={setFilterText}
        filterRarity={filterRarity} setFilterRarity={setFilterRarity}
        filterType={filterType} setFilterType={setFilterType}
        filterOwned={filterOwned} setFilterOwned={setFilterOwned}
      />

      <div className="flex items-center justify-between mb-3">
        <ImportExportPanel
          onExport={handleExport}
          onImport={handleImport}
          onSelectAll={handleSelectAll}
          onDeselectAll={handleDeselectAll}
        />
      </div>

      <div className="bg-white rounded-lg p-3 shadow-sm">
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-2">
          {filtered.map(card => {
            const entry = entryMap.get(card.id) ?? { card_id: card.id, owned: false, uncap: 4 };
            return (
              <CardTile
                key={card.id}
                card={card}
                entry={entry}
                onToggleOwned={() => {
                  const current = entryMap.get(card.id);
                  trackEvent('card_ownership_toggled', { card_id: card.id, owned_after: !current?.owned });
                  updateEntry(card.id, e => ({ ...e, owned: !e.owned }));
                }}
                onSetUncap={(uncap) => {
                  trackEvent('card_uncap_changed', { card_id: card.id, uncap_level: uncap });
                  updateEntry(card.id, e => ({ ...e, uncap }));
                }}
              />
            );
          })}
        </div>
      </div>

      {statusMessage && (
        <p className="text-sm text-green-600 mt-2">{statusMessage}</p>
      )}
    </div>
  );
}
