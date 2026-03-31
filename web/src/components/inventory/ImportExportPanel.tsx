import { useRef } from 'react';

interface Props {
  onExport: () => void;
  onImport: (json: string) => void;
  onSelectAll: () => void;
  onDeselectAll: () => void;
}

export default function ImportExportPanel({ onExport, onImport, onSelectAll, onDeselectAll }: Props) {
  const fileRef = useRef<HTMLInputElement>(null);

  const handleImport = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        onImport(reader.result);
      }
    };
    reader.readAsText(file);
    if (fileRef.current) fileRef.current.value = '';
  };

  const btnClass = 'px-3 py-1.5 text-xs rounded cursor-pointer';

  return (
    <div className="flex flex-wrap gap-2 items-center">
      <button onClick={onSelectAll} className={`${btnClass} bg-gray-200 hover:bg-gray-300`}>
        表示を全所持
      </button>
      <button onClick={onDeselectAll} className={`${btnClass} bg-gray-200 hover:bg-gray-300`}>
        表示を全未所持
      </button>
      <button onClick={onExport} className={`${btnClass} bg-[var(--color-accent)] text-white hover:opacity-90`}>
        JSONエクスポート
      </button>
      <label className={`${btnClass} bg-[var(--color-accent)] text-white hover:opacity-90`}>
        JSONインポート
        <input ref={fileRef} type="file" accept=".json" onChange={handleImport} className="hidden" />
      </label>
    </div>
  );
}
