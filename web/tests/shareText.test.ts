import { describe, it, expect } from 'vitest';
import {
  buildShareIntentUrl,
  buildShareText,
  xWeightedLength,
  SHARE_TEXT_BUDGET,
  X_MAX_WEIGHTED_LENGTH,
  X_URL_WEIGHT,
  type ShareTextInput,
} from '../src/services/shareText';

const HASHTAGS = ['学マス', 'GakumasuCalc'];

function input(over: Partial<ShareTextInput> = {}): ShareTextInput {
  return {
    scenarioLabel: 'H.I.F',
    planTypeLabel: 'センス',
    characterName: '花海咲季',
    finalStatus: { vo: 3000, da: 2760, vi: 2520 },
    statCap: 3000,
    cards: [
      { name: 'カードA', uncap: 4, isRental: false },
      { name: 'カードB', uncap: 3, isRental: false },
      { name: 'カードC', uncap: 4, isRental: true },
    ],
    ...over,
  };
}

/** intent で連結される「本文 + ' ' + URL + ' ' + #tag...」の重み付き長さ */
function totalWeight(text: string): number {
  const tags = HASHTAGS.map((h) => `#${h}`).join(' ');
  return xWeightedLength(text) + 1 + X_URL_WEIGHT + 1 + xWeightedLength(tags);
}

describe('xWeightedLength', () => {
  it('半角英数は1、全角は2で数える', () => {
    expect(xWeightedLength('abc 123')).toBe(7);
    expect(xWeightedLength('あいう')).toBe(6);
    expect(xWeightedLength('Vo 3000 / 合計')).toBe(10 + 4);
  });
});

describe('buildShareText', () => {
  it('シナリオ・育成タイプ・キャラ・cap適用後ステータス・合計・編成を含む', () => {
    const text = buildShareText(input({ finalStatus: { vo: 3200, da: 2760, vi: 2520 } }), HASHTAGS);
    expect(text).toContain('学マス H.I.F（センス・花海咲季）');
    // cap 超過分 (3200→3000) は切り捨てて表示し、上限到達は MAX を付ける
    expect(text).toContain('Vo 3000(MAX)');
    expect(text).toContain('Da 2760 / Vi 2520');
    expect(text).toContain('合計 8280');
    // 4凸 (既定) は凸数を省き、それ以外の凸数とレンタルは明示する
    expect(text).toContain('編成: カードA / カードB(3凸) / カードC(レンタル)');
  });

  it('キャラ未選択なら育成タイプだけを括弧に入れる', () => {
    const text = buildShareText(input({ characterName: null }), HASHTAGS);
    expect(text).toContain('（センス）');
  });

  it('全枚数が入らない場合は入る枚数だけ載せて「他N枚」を付け、制限内に収める', () => {
    const longCards = Array.from({ length: 6 }, (_, i) => ({
      name: `「とても長いサポートカードの名前がここに入ります」キャラクター名${i}`,
      uncap: 4,
      isRental: i === 5,
    }));
    const text = buildShareText(input({ cards: longCards }), HASHTAGS);
    expect(text).toContain('編成: 「とても長いサポートカードの名前がここに入ります」キャラクター名0');
    expect(text).toMatch(/他[1-5]枚$/);
    expect(totalWeight(text)).toBeLessThanOrEqual(SHARE_TEXT_BUDGET);
  });

  it('1枚も入らない場合は編成行を落とす', () => {
    const huge = [{ name: 'あ'.repeat(200), uncap: 4, isRental: false }];
    const text = buildShareText(input({ cards: huge }), HASHTAGS);
    expect(text).not.toContain('編成:');
    expect(totalWeight(text)).toBeLessThanOrEqual(SHARE_TEXT_BUDGET);
  });

  it('実際のカード名の長さ (13文字前後) 6枚なら一部を載せて他N枚で補う', () => {
    const realistic = Array.from({ length: 6 }, (_, i) => ({
      name: `お姉ちゃん直伝メニュー${i}`,
      uncap: 4,
      isRental: i === 5,
    }));
    const text = buildShareText(input({ cards: realistic }), HASHTAGS);
    expect(text).toContain('編成:');
    expect(totalWeight(text)).toBeLessThanOrEqual(SHARE_TEXT_BUDGET);
  });

  it('制限内なら編成行を残す', () => {
    const text = buildShareText(input(), HASHTAGS);
    expect(text).toContain('編成:');
    expect(totalWeight(text)).toBeLessThanOrEqual(SHARE_TEXT_BUDGET);
  });

  it('上限は X の 280 より余白を残した値で、実例 (HIF 6枚) が余白込みで収まる', () => {
    expect(SHARE_TEXT_BUDGET).toBeLessThan(X_MAX_WEIGHTED_LENGTH);
    // 2026-09-15 に実際に生成された HIF の編成 (全カード名を載せると 278/280 で余白がなかった例)
    const real = input({
      characterName: '花海咲季',
      finalStatus: { vo: 3200, da: 1553, vi: 1126 },
      cards: [
        { name: '一時休戦です', uncap: 4, isRental: false },
        { name: 'もう一度、最初から！', uncap: 3, isRental: false },
        { name: '嬉し恥ずかし夢心地', uncap: 4, isRental: false },
        { name: 'ほっぺた、ぷに', uncap: 4, isRental: false },
        { name: '利用し合うのが友達！', uncap: 4, isRental: false },
        { name: 'もうすぐ本番ですね', uncap: 4, isRental: true },
      ],
    });
    const text = buildShareText(real, HASHTAGS);
    expect(text).toContain('編成: 一時休戦です');
    expect(text).toMatch(/他[1-5]枚$/);
    expect(totalWeight(text)).toBeLessThanOrEqual(SHARE_TEXT_BUDGET);
  });
});

describe('buildShareIntentUrl', () => {
  it('x.com の intent URL に text / url / hashtags を載せる', () => {
    const url = buildShareIntentUrl('本文', 'https://tyuukiti.github.io/gakumasu-calc/hif', HASHTAGS);
    expect(url.startsWith('https://x.com/intent/tweet?')).toBe(true);
    const params = new URL(url).searchParams;
    expect(params.get('text')).toBe('本文');
    expect(params.get('url')).toBe('https://tyuukiti.github.io/gakumasu-calc/hif');
    expect(params.get('hashtags')).toBe('学マス,GakumasuCalc');
  });
});
