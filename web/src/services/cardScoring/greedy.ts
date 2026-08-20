import type { Character } from '../../types/models';
import type { CardScore } from '../../types/results';
import type { OverflowPenaltyConfig } from './types';
import { selectBestCard } from './results';

// --- Greedy fill owned slots from checkpoint ---

export function greedyFillOwned(
  contributions: CardScore[],
  selectedInit: CardScore[],
  usedIdsInit: Set<string>,
  accVoInit: number,
  accDaInit: number,
  accViInit: number,
  remainingSlotsInit: Record<string, number>,
  remainingFreeInit: number,
  ownedSlots: number,
  statCap: number,
  character?: Character | null,
  overflowPenalty?: OverflowPenaltyConfig,
): {
  selected: CardScore[];
  usedIds: Set<string>;
  accVo: number;
  accDa: number;
  accVi: number;
} {
  const sel = [...selectedInit];
  const used = new Set(usedIdsInit);
  let aVo = accVoInit,
    aDa = accDaInit,
    aVi = accViInit;

  // キャラの para_bonus はカード貢献にも乗るので、accumulator 更新も同じ倍率で行う
  const voMul = 1 + (character?.para_bonus.vo ?? 0) / 100;
  const daMul = 1 + (character?.para_bonus.da ?? 0) / 100;
  const viMul = 1 + (character?.para_bonus.vi ?? 0) / 100;

  // 属性枠
  const sortedSlots = Object.entries(remainingSlotsInit).sort(
    (a, b) => b[1] - a[1],
  );
  for (const [type, count] of sortedSlots) {
    if (count <= 0) continue;
    const candidates = contributions.filter(
      (cs) =>
        (cs.card.type === type || cs.card.type === 'all' || cs.card.type === 'as') &&
        !used.has(cs.card.id),
    );
    for (let i = 0; i < count && sel.length < ownedSlots; i++) {
      const best = selectBestCard(candidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo * voMul;
      aDa += best.raw_da * daMul;
      aVi += best.raw_vi * viMul;
    }
  }

  // フリー枠
  for (let i = 0; i < remainingFreeInit && sel.length < ownedSlots; i++) {
    const freeCandidates = contributions.filter(
      (cs) => !used.has(cs.card.id),
    );
    const best = selectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
    if (best == null) break;
    sel.push(best);
    used.add(best.card.id);
    aVo += best.raw_vo * voMul;
    aDa += best.raw_da * daMul;
    aVi += best.raw_vi * viMul;
  }

  // 補充
  if (sel.length < ownedSlots) {
    const remaining = contributions.filter((cs) => !used.has(cs.card.id));
    while (sel.length < ownedSlots) {
      const best = selectBestCard(remaining, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
      if (best == null) break;
      sel.push(best);
      used.add(best.card.id);
      aVo += best.raw_vo * voMul;
      aDa += best.raw_da * daMul;
      aVi += best.raw_vi * viMul;
    }
  }

  return { selected: sel, usedIds: used, accVo: aVo, accDa: aDa, accVi: aVi };
}

