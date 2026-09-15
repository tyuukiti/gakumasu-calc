/**
 * 計算結果を X (旧Twitter) に投稿するための本文と intent URL を組み立てる。
 *
 * X の文字数制限は 280 で、日本語などの全角文字は 2、半角英数は 1、URL は長さに関わらず 23 と数えられる。
 * 本文 + URL + ハッシュタグ が制限内に収まるように、収まらない場合は編成行を落として短くする。
 */

export interface ShareCard {
  name: string;
  /** 計算に使われた凸数 (0-4)。レンタルは 4 */
  uncap: number;
  isRental: boolean;
}

export interface ShareTextInput {
  /** シナリオ表示名 (例: 'H.I.F', '初Legend', 'NIA マスター') */
  scenarioLabel: string;
  /** 育成タイプ表示名 (例: 'センス') */
  planTypeLabel: string;
  /** キャラ名。未選択なら null */
  characterName: string | null;
  /** cap 適用前の到達ステータス */
  finalStatus: { vo: number; da: number; vi: number };
  /** 各属性の上限 (HIF は本戦上限増加込み) */
  statCap: number;
  /** 選択編成 (表示順) */
  cards: ShareCard[];
}

export const X_MAX_WEIGHTED_LENGTH = 280;
/** X は URL を長さに関わらず 23 文字として数える (t.co 短縮) */
export const X_URL_WEIGHT = 23;
/**
 * 生成する本文 (URL・ハッシュタグ込み) の上限。X の 280 ぎりぎりではなく、
 * 投稿者が一言添える余白 (全角 20 文字ぶん) と、重み計算が X の実装と数単位ずれる可能性を見て 240 にする。
 */
export const SHARE_TEXT_BUDGET = 240;

/**
 * X の重み付き文字数。半角英数・基本ラテン等 (U+0000〜U+10FF, 一般句読点の一部) は 1、それ以外 (全角・絵文字) は 2。
 * @see https://developer.x.com/en/docs/counting-characters
 */
export function xWeightedLength(text: string): number {
  let total = 0;
  for (const ch of text) {
    const cp = ch.codePointAt(0)!;
    const light =
      cp <= 0x10ff ||
      (cp >= 0x2000 && cp <= 0x200d) ||
      (cp >= 0x2010 && cp <= 0x201f) ||
      (cp >= 0x2032 && cp <= 0x2037);
    total += light ? 1 : 2;
  }
  return total;
}

/** 理論値計算は 4凸 が既定なので、4凸の所持カードは凸数を省いて文字数を節約する。 */
function formatCard(c: ShareCard): string {
  if (c.isRental) return `${c.name}(レンタル)`;
  return c.uncap === 4 ? c.name : `${c.name}(${c.uncap}凸)`;
}

/** 編成行。先頭 count 枚を載せ、残りがあれば「他N枚」を付ける。count=0 なら null。 */
function buildCardsLine(cards: ShareCard[], count: number): string | null {
  if (count <= 0 || cards.length === 0) return null;
  const shown = cards.slice(0, count).map(formatCard).join(' / ');
  const rest = cards.length - count;
  return `編成: ${shown}${rest > 0 ? ` 他${rest}枚` : ''}`;
}

/** 本文の各行を組み立てる。cardCount は編成行に載せる枚数 (0 で編成行なし)。 */
function buildLines(input: ShareTextInput, cardCount: number): string[] {
  const cap = (v: number) => Math.min(v, input.statCap);
  const vo = cap(input.finalStatus.vo);
  const da = cap(input.finalStatus.da);
  const vi = cap(input.finalStatus.vi);
  const total = vo + da + vi;
  const mark = (v: number) => (v >= input.statCap ? `${v}(MAX)` : `${v}`);

  const cond = [input.planTypeLabel, input.characterName].filter((s): s is string => !!s).join('・');
  const lines = [
    `学マス ${input.scenarioLabel}（${cond}）サポカ編成の理論値`,
    `Vo ${mark(vo)} / Da ${mark(da)} / Vi ${mark(vi)}`,
    `合計 ${total}`,
  ];
  const cardsLine = buildCardsLine(input.cards, cardCount);
  if (cardsLine) lines.push(cardsLine);
  return lines;
}

/**
 * 投稿本文を返す。本文 + 半角スペース + URL(23) + 半角スペース + ハッシュタグ が SHARE_TEXT_BUDGET に収まる範囲で、
 * 編成行に載せるカード枚数を全枚数から 1 枚ずつ減らして探す (残りは「他N枚」)。1 枚も入らなければ編成行なし。
 * (intent の text / url / hashtags は X 側で「text URL #tag1 #tag2」の順に連結される)
 *
 * 注意: URL を 23 と数えるのは X がリンクとして認識する URL (本番の github.io) の場合。
 * 開発環境の http://localhost:5173/... は認識されず実長で数えられるため、dev では X 側の表示が上限を超えることがある。
 */
export function buildShareText(input: ShareTextInput, hashtags: string[]): string {
  const tagText = hashtags.map((h) => `#${h}`).join(' ');
  const extra = 1 + X_URL_WEIGHT + (tagText ? 1 + xWeightedLength(tagText) : 0);
  const fits = (text: string) => xWeightedLength(text) + extra <= SHARE_TEXT_BUDGET;

  for (let count = input.cards.length; count > 0; count--) {
    const text = buildLines(input, count).join('\n');
    if (fits(text)) return text;
  }
  return buildLines(input, 0).join('\n');
}

/** X の投稿画面を開く intent URL を返す。 */
export function buildShareIntentUrl(text: string, url: string, hashtags: string[]): string {
  const params = new URLSearchParams({ text, url });
  if (hashtags.length > 0) params.set('hashtags', hashtags.join(','));
  return `https://x.com/intent/tweet?${params.toString()}`;
}
