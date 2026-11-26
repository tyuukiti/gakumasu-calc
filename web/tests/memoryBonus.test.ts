import { describe, it, expect } from 'vitest';
import { summarizeMemoryBonuses } from '../src/services/memoryBonus';
import { emptyMemoryBonus, type MemoryBonus, type MemoryBonusType } from '../src/types/models';

type Attr = [number, MemoryBonusType];

function mem(vo: Attr, da: Attr, vi: Attr): MemoryBonus {
  return {
    vo: { value: vo[0], type: vo[1] },
    da: { value: da[0], type: da[1] },
    vi: { value: vi[0], type: vi[1] },
  };
}

const EMPTY: Attr = [0, 'flat'];

/**
 * 持ち込みメモリーの見出し要約 (MemoryBonusInput の Accordion status)。
 * 計算と同じく実数値は floor 済み合計、パラボ% は合計で属性別にまとめる。枚数は出さない。
 */
describe('summarizeMemoryBonuses', () => {
  it('設定が無ければ null (未設定表示に任せる)', () => {
    expect(summarizeMemoryBonuses(null)).toBeNull();
    expect(summarizeMemoryBonuses(undefined)).toBeNull();
    expect(summarizeMemoryBonuses([])).toBeNull();
    expect(summarizeMemoryBonuses([emptyMemoryBonus(), emptyMemoryBonus()])).toBeNull();
  });

  it('4枚同一 (Vo 実数 / Da・Vi パラボ) は属性別合計、枚数は含めない', () => {
    const m = mem([25, 'flat'], [3.5, 'para'], [3.5, 'para']);
    expect(summarizeMemoryBonuses([m, m, m, m])).toEqual([
      { stat: 'vo', text: '+100' },
      { stat: 'da', text: '+14%' },
      { stat: 'vi', text: '+14%' },
    ]);
  });

  it('合計 0 の属性は出さない', () => {
    const list = [mem([25, 'flat'], EMPTY, EMPTY), emptyMemoryBonus(), emptyMemoryBonus(), emptyMemoryBonus()];
    expect(summarizeMemoryBonuses(list)).toEqual([{ stat: 'vo', text: '+25' }]);
  });

  it('同じ属性に実数とパラボが混在すれば両方並べる', () => {
    const list = [mem(EMPTY, [25, 'flat'], EMPTY), mem(EMPTY, [3.5, 'para'], EMPTY)];
    expect(summarizeMemoryBonuses(list)).toEqual([{ stat: 'da', text: '+25 +3.5%' }]);
  });

  it('パラボ合計の浮動小数の端数を落とす', () => {
    const m = mem(EMPTY, EMPTY, [3.3, 'para']);
    expect(summarizeMemoryBonuses([m, m, m])).toEqual([{ stat: 'vi', text: '+9.9%' }]);
  });

  it('実数値は計算と同じく合計を floor する', () => {
    expect(summarizeMemoryBonuses([mem([12.5, 'flat'], EMPTY, EMPTY)])).toEqual([{ stat: 'vo', text: '+12' }]);
    expect(
      summarizeMemoryBonuses([mem([12.5, 'flat'], EMPTY, EMPTY), mem([12.5, 'flat'], EMPTY, EMPTY)]),
    ).toEqual([{ stat: 'vo', text: '+25' }]);
  });

  it('負の値は符号付きで出す', () => {
    expect(summarizeMemoryBonuses([mem([-10, 'flat'], [-1.5, 'para'], EMPTY)])).toEqual([
      { stat: 'vo', text: '-10' },
      { stat: 'da', text: '-1.5%' },
    ]);
  });

  it('設定はあるが値が相殺して全属性 0 なら空配列 (null とは区別する)', () => {
    const list = [mem([10, 'flat'], EMPTY, EMPTY), mem([-10, 'flat'], EMPTY, EMPTY)];
    expect(summarizeMemoryBonuses(list)).toEqual([]);
  });
});
