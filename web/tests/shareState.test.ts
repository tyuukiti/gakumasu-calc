import { describe, it, expect } from 'vitest';
import {
  SHARE_PAYLOAD_VERSION,
  buildShareUrl,
  decodeSharePayload,
  encodeSharePayload,
  extractShareToken,
  isShareEncodingSupported,
  sanitizeSharePayload,
  type SharePayload,
} from '../src/services/shareState';

/**
 * 共有 URL ペイロードのエンコード/デコードと検証。
 * 実データに依存しない純粋なラウンドトリップと、壊れた入力の拒否を確認する。
 */

function samplePayload(): SharePayload {
  return {
    v: SHARE_PAYLOAD_VERSION,
    plan: 'hif',
    at: 1758191622,
    label: 'Vocal2 + フリー3',
    deck: [
      { id: '0094', uncap: 4, rental: false, required: false },
      { id: '0030', uncap: 2, rental: false, required: true },
      { id: '0120', uncap: 4, rental: true, required: false },
    ],
    calc: {
      planType: 'anomaly',
      characterId: 'tsukimura_temari',
      uncap3: true,
      step4: false,
      spCounts: { vo: 2, da: 1, vi: 0 },
      additionalCounts: { p_drink_acquire: 6, skill_acquire: 12 },
      templateName: 'センス',
      contestMode: false,
      requiredCardIds: ['0030'],
      excludedCardIds: ['0001'],
      memoryBonuses: [
        { vo: { value: 300, type: 'flat' }, da: { value: 10, type: 'para' }, vi: { value: 0, type: 'flat' } },
      ],
    },
    hif: {
      scheduleChoices: {
        '1': { action: 'vo_lesson', sub_stat: 'da' },
        '2': { action: 'da_class' },
        '3': { action: 'outing' },
      },
      examAllocations: { '10': { vo: 40, da: 40, vi: 0 } },
      bonusLevels: { voUpLevel: 5, daUpLevel: 5, viUpLevel: 0, finalStatLimitLevel: 6 },
      bulkLessonDefault: { mainStat: 'da', subStat: 'vi' },
      bulkClassStat: 'vo',
    },
  };
}

describe('shareState: エンコード/デコード', () => {
  it('この環境では CompressionStream が使える', () => {
    expect(isShareEncodingSupported()).toBe(true);
  });

  it('ラウンドトリップで同じペイロードに戻る (HIFボーナスの Lv0 も保持)', async () => {
    const payload = samplePayload();
    const token = await encodeSharePayload(payload);
    expect(token).toMatch(/^[A-Za-z0-9_-]+$/);
    const decoded = await decodeSharePayload(token);
    expect(decoded).toEqual(payload);
  });

  it('日程方式 (sched) のペイロードも往復する', async () => {
    const payload: SharePayload = {
      ...samplePayload(),
      plan: 'nia',
      hif: undefined,
      sched: {
        scheduleChoices: { '1': { action: 'vo_lesson' }, '2': { action: 'rest' } },
        niaAuditionTierByWeek: { '12': 'FINALE' },
        bulkLessonStat: 'da',
        bulkClassStat: 'vi',
      },
    };
    delete payload.hif;
    const decoded = await decodeSharePayload(await encodeSharePayload(payload));
    expect(decoded).toEqual(payload);
  });

  it('トークンは URL に載せられる長さに収まる (実運用相当の 29 日スケジュール)', async () => {
    const payload = samplePayload();
    payload.deck = Array.from({ length: 6 }, (_, i) => ({
      id: `01${String(i).padStart(2, '0')}`,
      uncap: i % 5,
      rental: i === 5,
      required: false,
    }));
    const sched: Record<string, { action: string; sub_stat?: string }> = {};
    for (let d = 1; d <= 29; d++) {
      sched[String(d)] = d % 3 === 0 ? { action: 'vo_lesson', sub_stat: 'vi' } : { action: 'da_class' };
    }
    payload.hif!.scheduleChoices = sched;
    const token = await encodeSharePayload(payload);
    expect(token.length).toBeLessThan(1500);
  });

  it('壊れたトークン・不正文字は null', async () => {
    expect(await decodeSharePayload('')).toBeNull();
    expect(await decodeSharePayload('not base64url!!')).toBeNull();
    expect(await decodeSharePayload('AAAAAAAA')).toBeNull();
  });
});

describe('shareState: sanitizeSharePayload', () => {
  it('バージョン違い・plan 欠落・編成なしは拒否', () => {
    const p = samplePayload();
    expect(sanitizeSharePayload({ ...p, v: 2 })).toBeNull();
    expect(sanitizeSharePayload({ ...p, plan: '' })).toBeNull();
    expect(sanitizeSharePayload({ ...p, deck: [] })).toBeNull();
    expect(sanitizeSharePayload({ ...p, deck: Array(7).fill(p.deck[0]) })).toBeNull();
  });

  it('レンタル2枚・重複ID・凸数範囲外は拒否', () => {
    const p = samplePayload();
    expect(
      sanitizeSharePayload({ ...p, deck: [{ ...p.deck[0], rental: true }, { ...p.deck[1], rental: true }] }),
    ).toBeNull();
    expect(sanitizeSharePayload({ ...p, deck: [p.deck[0], { ...p.deck[0] }] })).toBeNull();
    expect(sanitizeSharePayload({ ...p, deck: [{ ...p.deck[0], uncap: 5 }] })).toBeNull();
  });

  it('calc の欠落フィールドは既定値で補う (STEP4 は ON、凸3 は OFF)', () => {
    const p = samplePayload();
    const out = sanitizeSharePayload({
      v: 1,
      plan: p.plan,
      deck: p.deck,
      calc: { planType: 'sense' },
    });
    expect(out).not.toBeNull();
    expect(out!.calc.step4).toBe(true);
    expect(out!.calc.uncap3).toBe(false);
    expect(out!.calc.characterId).toBeNull();
    expect(out!.calc.spCounts).toEqual({ vo: 0, da: 0, vi: 0 });
    expect(out!.calc.memoryBonuses).toEqual([]);
    expect(out!.at).toBe(0);
    expect(out!.hif).toBeUndefined();
  });

  it('育成タイプが不正なら拒否', () => {
    const p = samplePayload();
    expect(sanitizeSharePayload({ ...p, calc: { ...p.calc, planType: 'free' } })).toBeNull();
  });

  it('一括設定はメイン=サブ・不正値なら捨てる', () => {
    const p = samplePayload();
    const out = sanitizeSharePayload({
      ...p,
      hif: { ...p.hif, bulkLessonDefault: { mainStat: 'vo', subStat: 'vo' }, bulkClassStat: 'all' },
    });
    expect(out!.hif!.bulkLessonDefault).toBeUndefined();
    expect(out!.hif!.bulkClassStat).toBeUndefined();
  });

  it('スケジュールの数値でないキー・action 欠落は捨てる', () => {
    const p = samplePayload();
    const out = sanitizeSharePayload({
      ...p,
      hif: {
        ...p.hif,
        scheduleChoices: { '1': { action: 'vo_lesson', sub_stat: 'da' }, x: { action: 'outing' }, '2': {} },
      },
    });
    expect(Object.keys(out!.hif!.scheduleChoices)).toEqual(['1']);
  });
});

describe('shareState: URL ハッシュ', () => {
  it('buildShareUrl / extractShareToken が往復する', () => {
    const url = buildShareUrl('https://example.test/gakumasu-calc/hif', 'abc-_123');
    expect(url).toBe('https://example.test/gakumasu-calc/hif#s=abc-_123');
    expect(extractShareToken(new URL(url).hash)).toBe('abc-_123');
  });

  it('トークンが無いハッシュは null', () => {
    expect(extractShareToken('')).toBeNull();
    expect(extractShareToken('#')).toBeNull();
    expect(extractShareToken('#foo=bar')).toBeNull();
    expect(extractShareToken('#s=')).toBeNull();
  });
});
