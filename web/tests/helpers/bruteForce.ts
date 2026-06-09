import type { SupportCard, TrainingPlan } from '../../src/types/models';
import { scoreDeck, type ScoreOptions } from './scoreDeck';

/**
 * 総当たり最適オラクル (合成・小プール専用)。
 * スロット制約を満たす全カード組合せを列挙し scoreDeck で採点して、真の最大を返す。
 */

const WILDCARD_TYPES = new Set(['all', 'as']);
/** 組合せ爆発ガード: これを超える候補数なら fail させる。 */
const MAX_COMBINATIONS = 20000;

function nCk(n: number, k: number): number {
  if (k < 0 || k > n) return 0;
  let r = 1;
  for (let i = 0; i < k; i++) r = (r * (n - i)) / (i + 1);
  return Math.round(r);
}

/** size 個の組合せを列挙 (インデックス集合)。 */
function* combinations(n: number, size: number): Generator<number[]> {
  const idx = Array.from({ length: size }, (_, i) => i);
  if (size === 0) {
    yield [];
    return;
  }
  if (size > n) return;
  while (true) {
    yield idx.slice();
    let i = size - 1;
    while (i >= 0 && idx[i] === i + n - size) i--;
    if (i < 0) return;
    idx[i]++;
    for (let j = i + 1; j < size; j++) idx[j] = idx[j - 1] + 1;
  }
}

/**
 * 部分集合がスロット要件を満たせるか。
 * 各属性枠は「同属性カード または all/as ワイルド」で埋める必要がある。
 * 部分集合サイズ == 総スロット数 なので、属性枠さえ埋まれば残りはフリー枠に回せる。
 * → 実現可能 ⇔ Σ max(0, required[stat] - specific[stat]) ≤ wildcard数
 */
function isFeasible(
  cards: SupportCard[],
  cardTypeSlots: Record<string, number>,
): boolean {
  const specific: Record<string, number> = {};
  let wild = 0;
  for (const c of cards) {
    if (WILDCARD_TYPES.has(c.type)) wild += 1;
    else specific[c.type] = (specific[c.type] ?? 0) + 1;
  }
  let shortfall = 0;
  for (const [stat, req] of Object.entries(cardTypeSlots)) {
    shortfall += Math.max(0, req - (specific[stat] ?? 0));
  }
  return shortfall <= wild;
}

export interface BruteForceResult {
  bestTotal: number;
  bestDeck: SupportCard[];
  evaluated: number;
}

/**
 * 総当たりで最適デッキ (scoreDeck の cap 後合計が最大) を求める。
 *
 * @param cardTypeSlots 属性枠 (例 {vo:2, da:2})
 * @param freeSlots フリー枠数
 */
export function findOptimalDeck(
  plan: TrainingPlan,
  pool: SupportCard[],
  cardTypeSlots: Record<string, number>,
  freeSlots: number,
  mainStats: string[],
  opts: ScoreOptions = {},
): BruteForceResult {
  const totalSlots =
    Object.values(cardTypeSlots).reduce((a, b) => a + b, 0) + freeSlots;

  const combos = nCk(pool.length, totalSlots);
  if (combos > MAX_COMBINATIONS) {
    throw new Error(
      `brute force too large: C(${pool.length}, ${totalSlots}) = ${combos} > ${MAX_COMBINATIONS}`,
    );
  }

  let bestTotal = -Infinity;
  let bestDeck: SupportCard[] = [];
  let evaluated = 0;

  for (const idxs of combinations(pool.length, totalSlots)) {
    const deck = idxs.map((i) => pool[i]);
    if (!isFeasible(deck, cardTypeSlots)) continue;
    evaluated += 1;
    const { cappedTotal } = scoreDeck(plan, deck, mainStats, opts);
    if (cappedTotal > bestTotal) {
      bestTotal = cappedTotal;
      bestDeck = deck;
    }
  }

  return { bestTotal, bestDeck, evaluated };
}

export { isFeasible };

export interface RentalBruteForceResult {
  bestTotal: number;
  bestDeck: SupportCard[];
  bestRentalId: string | null;
  evaluated: number;
}

/**
 * レンタル枠対応の総当たり最適オラクル (実データ・要キュレーション小プール)。
 *
 * 所持のみモードの編成 = 「非レンタル(deckSize-1)枚 (所持凸数) + レンタル1枚 (4凸借用)」を
 * 全列挙し、外部 score 関数 (実 calculate / cap後合計) で採点して真の最大を返す。
 * `findOptimalDeck` がモデル化していないレンタル枠を扱うため、HIFのレンタル前提シナリオの
 * 最適性検証に使う。プールは寄与上位などにキュレーションして組合せ爆発を防ぐこと。
 *
 * @param ownedPool   非レンタル枠の候補 (凸数は score 側で解決)
 * @param rentalPool  レンタル枠の候補 (4凸借用は score 側で解決。所持/未所持どちらも可)
 * @param score       (deck, rentalId) -> cap後合計。レンタルカードを4凸として評価すること
 * @param isValid     SP枚数・必須など編成制約を満たすか (満たさない組合せは除外)
 */
export function bruteForceOptimalRental(
  ownedPool: SupportCard[],
  rentalPool: SupportCard[],
  deckSize: number,
  score: (deck: SupportCard[], rentalId: string) => number,
  isValid: (deck: SupportCard[]) => boolean,
  maxCombinations = MAX_COMBINATIONS,
): RentalBruteForceResult {
  const ownedSlots = deckSize - 1;
  // 概算: |rentalPool| × C(|ownedPool|, ownedSlots)
  const combos = rentalPool.length * nCk(ownedPool.length, ownedSlots);
  if (combos > maxCombinations) {
    throw new Error(
      `rental brute force too large: ${rentalPool.length} × C(${ownedPool.length}, ${ownedSlots}) ≈ ${combos} > ${maxCombinations}`,
    );
  }

  let bestTotal = -Infinity;
  let bestDeck: SupportCard[] = [];
  let bestRentalId: string | null = null;
  let evaluated = 0;

  for (const rental of rentalPool) {
    const ownedCandidates = ownedPool.filter((c) => c.id !== rental.id);
    for (const idxs of combinations(ownedCandidates.length, ownedSlots)) {
      const deck = [...idxs.map((i) => ownedCandidates[i]), rental];
      if (!isValid(deck)) continue;
      evaluated += 1;
      const total = score(deck, rental.id);
      if (total > bestTotal) {
        bestTotal = total;
        bestDeck = deck;
        bestRentalId = rental.id;
      }
    }
  }

  return { bestTotal, bestDeck, bestRentalId, evaluated };
}
