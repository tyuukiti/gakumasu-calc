import {
  isEmptyMemoryBonus,
  sumMemoryFlat,
  sumMemoryParaBonus,
  type MemoryBonus,
} from '../types/models';

export type MemoryStat = 'vo' | 'da' | 'vi';

/** 属性 1 つ分の要約。text は "+100" / "+14%" / "+25 +3.5%" (実数とパラボの混在) のような符号付き文字列 */
export interface MemoryStatSummary {
  stat: MemoryStat;
  text: string;
}

const STATS: readonly MemoryStat[] = ['vo', 'da', 'vi'];

function signed(v: number): string {
  return v > 0 ? `+${v}` : `${v}`;
}

/** 3.3×3 = 9.899999… のような浮動小数の端数を落とす (表示用、小数2桁) */
function roundPercent(v: number): number {
  return Math.round(v * 100) / 100;
}

/**
 * 持ち込みメモリーの設定内容を属性別に要約する (折りたたみ見出しの状態表示用)。
 * 計算に効く形 (実数値は floor 済みの合計、パラボ% は合計) でまとめ、合計 0 の属性は含めない。
 * 枚数は出さない (合算値を 1 枚に入れる使い方もあるため)。
 * 設定が 1 枚も無ければ null、設定はあるが全属性の合計が 0 なら空配列。
 */
export function summarizeMemoryBonuses(list: MemoryBonus[] | undefined | null): MemoryStatSummary[] | null {
  if (!list || list.every(isEmptyMemoryBonus)) return null;

  const flat = sumMemoryFlat(list);
  const para = sumMemoryParaBonus(list);
  const out: MemoryStatSummary[] = [];
  for (const stat of STATS) {
    const values: string[] = [];
    if (flat[stat] !== 0) values.push(signed(flat[stat]));
    const p = roundPercent(para[stat]);
    if (p !== 0) values.push(`${signed(p)}%`);
    if (values.length > 0) out.push({ stat, text: values.join(' ') });
  }
  return out;
}
