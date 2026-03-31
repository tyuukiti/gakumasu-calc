interface Props {
  filterText: string;
  setFilterText: (v: string) => void;
  filterRarity: string;
  setFilterRarity: (v: string) => void;
  filterType: string;
  setFilterType: (v: string) => void;
  filterOwned: string;
  setFilterOwned: (v: string) => void;
}

export default function InventoryFilters({
  filterText, setFilterText,
  filterRarity, setFilterRarity,
  filterType, setFilterType,
  filterOwned, setFilterOwned,
}: Props) {
  const selectClass = 'border rounded px-2 py-1 text-sm';
  return (
    <div className="flex flex-wrap gap-3 items-center bg-white rounded-lg px-4 py-3 shadow-sm mb-3">
      <label className="flex items-center gap-1 text-sm text-gray-500">
        検索
        <input type="text" value={filterText} onChange={e => setFilterText(e.target.value)}
          className="border rounded px-2 py-1 text-sm w-44" placeholder="カード名..." />
      </label>
      <label className="flex items-center gap-1 text-sm text-gray-500">
        レア
        <select value={filterRarity} onChange={e => setFilterRarity(e.target.value)} className={selectClass}>
          <option value="すべて">すべて</option>
          <option value="SSR">SSR</option>
          <option value="SR">SR</option>
          <option value="R">R</option>
        </select>
      </label>
      <label className="flex items-center gap-1 text-sm text-gray-500">
        属性
        <select value={filterType} onChange={e => setFilterType(e.target.value)} className={selectClass}>
          <option value="すべて">すべて</option>
          <option value="vo">Vo</option>
          <option value="da">Da</option>
          <option value="vi">Vi</option>
          <option value="all">As</option>
        </select>
      </label>
      <label className="flex items-center gap-1 text-sm text-gray-500">
        所持
        <select value={filterOwned} onChange={e => setFilterOwned(e.target.value)} className={selectClass}>
          <option value="すべて">すべて</option>
          <option value="所持">所持</option>
          <option value="未所持">未所持</option>
        </select>
      </label>
    </div>
  );
}
