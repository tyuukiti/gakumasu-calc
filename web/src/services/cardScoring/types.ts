/**
 * overflow罰則オプション。指定された場合、合計overflow が threshold を超えた時のみ
 * × 2 罰則を適用 (cap を大幅に超過するピックを抑制し、別属性カードへの差し替えを誘導)。
 * undefined の場合は罰則無し。
 */
export interface OverflowPenaltyConfig {
  threshold: number;
}
