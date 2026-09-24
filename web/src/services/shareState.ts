/**
 * 計算結果の共有URL。
 *
 * 「X で結果をシェア」で開く URL のハッシュ (#s=...) に、結果を再現するために必要な入力一式
 * (シナリオ・条件・確定した6枚編成と凸数) を JSON → deflate-raw → base64url で載せる。
 * 開いた側は自分の所持カードに依らず、この編成を固定して計算だけを走らせ、共有元と同じ結果を表示する。
 *
 * - サーバー不要 (GitHub Pages の静的配信のまま)。
 * - ハッシュに載せるのでサーバーログ・GA4 のランディングページには乗らない。
 * - X は URL を長さに関わらず 23 文字として数え、t.co はハッシュ込みの元 URL へ戻す。
 * - デコード側は型・範囲を検証し、不正なデータは null (表示しない) にする。カード ID の実在確認は
 *   ストア側 (データ読込後) で行う。
 */
import type { PlanType } from '../types/enums';
import type { MemoryBonus } from '../types/models';

export const SHARE_PAYLOAD_VERSION = 1 as const;
/** URL ハッシュ内のキー (#s=<token>) */
export const SHARE_HASH_PARAM = 's';

/** 共有された編成の1枚 */
export interface SharedDeckCard {
  id: string;
  /** 計算に使われた凸数 (0-4)。レンタルは 4 */
  uncap: number;
  rental: boolean;
  required: boolean;
}

/** 1週(1日)分の選択。HIF の公開レッスン日は sub_stat 付き、日程方式は action のみ */
export interface SharedChoice {
  action: string;
  sub_stat?: string;
}

export interface SharedStatTriple {
  vo: number;
  da: number;
  vi: number;
}

/** calcStore 側の入力条件 (全タブ共通) */
export interface SharePayloadCalc {
  planType: PlanType;
  characterId: string | null;
  /** 共有元の 3凸トグル (キャラ毎の永続設定ではなく共有時の値をそのまま使う) */
  uncap3: boolean;
  /** 共有元の STEP4 トグル */
  step4: boolean;
  spCounts: SharedStatTriple;
  /** イベント回数 (0 のキーは省略) */
  additionalCounts: Record<string, number>;
  templateName: string | null;
  contestMode: boolean;
  requiredCardIds: string[];
  excludedCardIds: string[];
  /** 持ち込みメモリー (全て空なら []) */
  memoryBonuses: MemoryBonus[];
}

export type SharedStat = 'vo' | 'da' | 'vi';

/** HIF タブ固有の条件 */
export interface SharePayloadHif {
  scheduleChoices: Record<string, SharedChoice>;
  examAllocations: Record<string, SharedStatTriple>;
  bonusLevels: Record<string, number>;
  /** 一括設定の表示値 (計算には日ごとの選択が使われる。UI の見た目を共有元に揃えるため) */
  bulkLessonDefault?: { mainStat: SharedStat; subStat: SharedStat };
  bulkClassStat?: SharedStat;
}

/** 日程方式 (初レジェンド / NIA) 固有の条件 */
export interface SharePayloadSchedule {
  scheduleChoices: Record<string, SharedChoice>;
  niaAuditionTierByWeek: Record<string, string>;
  /** 一括設定の表示値 */
  bulkLessonStat?: SharedStat;
  bulkClassStat?: SharedStat;
}

/** ストアが保持する「共有結果を表示中」の情報 (バナー表示用) */
export interface ShareViewInfo {
  planId: string;
  /** 共有時刻 (unix 秒)。0 なら不明 */
  sharedAt: number;
  /** 編成パターンのラベル */
  label: string;
}

export interface SharePayload {
  v: typeof SHARE_PAYLOAD_VERSION;
  /** プラン ID ('hif' | 'hatsu_legend' | 'nia') */
  plan: string;
  /** 共有時刻 (unix 秒)。不明なら 0 */
  at: number;
  /** 編成パターンのラベル (表示用) */
  label: string;
  deck: SharedDeckCard[];
  calc: SharePayloadCalc;
  hif?: SharePayloadHif;
  sched?: SharePayloadSchedule;
}

// ---------------------------------------------------------------------------
// 検証
// ---------------------------------------------------------------------------

function isRecord(v: unknown): v is Record<string, unknown> {
  return v != null && typeof v === 'object' && !Array.isArray(v);
}

function isInt(v: unknown, min: number, max: number): v is number {
  return typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max;
}

function isStat(v: unknown): v is SharedStat {
  return v === 'vo' || v === 'da' || v === 'vi';
}

function toStringArray(v: unknown): string[] {
  if (!Array.isArray(v)) return [];
  return [...new Set(v.filter((x): x is string => typeof x === 'string' && x.length > 0))];
}

function toStatTriple(v: unknown, max: number): SharedStatTriple {
  const o = isRecord(v) ? v : {};
  const pick = (k: 'vo' | 'da' | 'vi') => (isInt(o[k], 0, max) ? o[k] : 0);
  return { vo: pick('vo'), da: pick('da'), vi: pick('vi') };
}

function toChoiceRecord(v: unknown): Record<string, SharedChoice> {
  const out: Record<string, SharedChoice> = {};
  if (!isRecord(v)) return out;
  for (const [k, raw] of Object.entries(v)) {
    if (!/^\d+$/.test(k) || !isRecord(raw) || typeof raw.action !== 'string') continue;
    const choice: SharedChoice = { action: raw.action };
    if (typeof raw.sub_stat === 'string') choice.sub_stat = raw.sub_stat;
    out[k] = choice;
  }
  return out;
}

function toTripleRecord(v: unknown, max: number): Record<string, SharedStatTriple> {
  const out: Record<string, SharedStatTriple> = {};
  if (!isRecord(v)) return out;
  for (const [k, raw] of Object.entries(v)) {
    if (!/^\d+$/.test(k) || !isRecord(raw)) continue;
    out[k] = toStatTriple(raw, max);
  }
  return out;
}

/** 整数値レコード。keepZero=false なら 0 を省く (イベント回数)。HIFボーナスLv は 0 (効果なし) も意味を持つので残す */
function toIntRecord(v: unknown, max: number, keepZero: boolean): Record<string, number> {
  const out: Record<string, number> = {};
  if (!isRecord(v)) return out;
  for (const [k, raw] of Object.entries(v)) {
    if (isInt(raw, 0, max) && (keepZero || raw > 0)) out[k] = raw;
  }
  return out;
}

function toMemoryBonuses(v: unknown): MemoryBonus[] {
  if (!Array.isArray(v)) return [];
  const toAttr = (raw: unknown) => {
    const o = isRecord(raw) ? raw : {};
    return {
      value: typeof o.value === 'number' && Number.isFinite(o.value) ? o.value : 0,
      type: o.type === 'para' ? ('para' as const) : ('flat' as const),
    };
  };
  return v.slice(0, 4).map((m) => {
    const o = isRecord(m) ? m : {};
    return { vo: toAttr(o.vo), da: toAttr(o.da), vi: toAttr(o.vi) };
  });
}

/**
 * デコード直後の生オブジェクトを検証して SharePayload にする。構造が壊れていれば null。
 * 実在チェック (カード ID・週番号・キャラ ID) はデータ読込後にストア側で行う。
 */
export function sanitizeSharePayload(raw: unknown): SharePayload | null {
  if (!isRecord(raw) || raw.v !== SHARE_PAYLOAD_VERSION) return null;
  if (typeof raw.plan !== 'string' || raw.plan.length === 0) return null;
  if (!Array.isArray(raw.deck) || raw.deck.length === 0 || raw.deck.length > 6) return null;

  const deck: SharedDeckCard[] = [];
  for (const d of raw.deck) {
    if (!isRecord(d) || typeof d.id !== 'string' || d.id.length === 0 || !isInt(d.uncap, 0, 4)) return null;
    deck.push({ id: d.id, uncap: d.uncap, rental: d.rental === true, required: d.required === true });
  }
  if (new Set(deck.map((d) => d.id)).size !== deck.length) return null;
  if (deck.filter((d) => d.rental).length > 1) return null;

  const c = raw.calc;
  if (!isRecord(c)) return null;
  const planType = c.planType;
  if (planType !== 'sense' && planType !== 'logic' && planType !== 'anomaly') return null;

  const calc: SharePayloadCalc = {
    planType,
    characterId: typeof c.characterId === 'string' && c.characterId.length > 0 ? c.characterId : null,
    uncap3: c.uncap3 === true,
    // STEP4 は既定 ON なので、欠落時は ON 扱い
    step4: c.step4 !== false,
    spCounts: toStatTriple(c.spCounts, 6),
    additionalCounts: toIntRecord(c.additionalCounts, 999, false),
    templateName: typeof c.templateName === 'string' ? c.templateName : null,
    contestMode: c.contestMode === true,
    requiredCardIds: toStringArray(c.requiredCardIds),
    excludedCardIds: toStringArray(c.excludedCardIds),
    memoryBonuses: toMemoryBonuses(c.memoryBonuses),
  };

  const payload: SharePayload = {
    v: SHARE_PAYLOAD_VERSION,
    plan: raw.plan,
    at: typeof raw.at === 'number' && Number.isFinite(raw.at) && raw.at > 0 ? Math.floor(raw.at) : 0,
    label: typeof raw.label === 'string' ? raw.label : '',
    deck,
    calc,
  };

  if (isRecord(raw.hif)) {
    payload.hif = {
      scheduleChoices: toChoiceRecord(raw.hif.scheduleChoices),
      examAllocations: toTripleRecord(raw.hif.examAllocations, 9999),
      bonusLevels: toIntRecord(raw.hif.bonusLevels, 6, true),
    };
    const bld = raw.hif.bulkLessonDefault;
    if (isRecord(bld) && isStat(bld.mainStat) && isStat(bld.subStat) && bld.mainStat !== bld.subStat) {
      payload.hif.bulkLessonDefault = { mainStat: bld.mainStat, subStat: bld.subStat };
    }
    if (isStat(raw.hif.bulkClassStat)) payload.hif.bulkClassStat = raw.hif.bulkClassStat;
  }
  if (isRecord(raw.sched)) {
    const tiers: Record<string, string> = {};
    if (isRecord(raw.sched.niaAuditionTierByWeek)) {
      for (const [k, v] of Object.entries(raw.sched.niaAuditionTierByWeek)) {
        if (/^\d+$/.test(k) && typeof v === 'string') tiers[k] = v;
      }
    }
    payload.sched = {
      scheduleChoices: toChoiceRecord(raw.sched.scheduleChoices),
      niaAuditionTierByWeek: tiers,
    };
    if (isStat(raw.sched.bulkLessonStat)) payload.sched.bulkLessonStat = raw.sched.bulkLessonStat;
    if (isStat(raw.sched.bulkClassStat)) payload.sched.bulkClassStat = raw.sched.bulkClassStat;
  }
  return payload;
}

// ---------------------------------------------------------------------------
// エンコード / デコード
// ---------------------------------------------------------------------------

/** CompressionStream 非対応ブラウザ (古い Safari 等) では共有 URL に結果を載せない。 */
export function isShareEncodingSupported(): boolean {
  return typeof CompressionStream !== 'undefined' && typeof DecompressionStream !== 'undefined';
}

async function pipeThroughStream(
  bytes: Uint8Array<ArrayBuffer>,
  stream: { readable: ReadableStream<Uint8Array>; writable: WritableStream<BufferSource> },
): Promise<Uint8Array<ArrayBuffer>> {
  // 読み出しを先に始めてから書き込む (単一チャンクでも書き込み待ちで詰まらないように)。
  // 壊れた入力では readable 側が先に失敗するので、両方を同時に待って未処理の reject を残さない。
  const collected = new Response(stream.readable).arrayBuffer();
  const writer = stream.writable.getWriter();
  const written = writer.write(bytes).then(() => writer.close());
  const [buf] = await Promise.all([collected, written]);
  return new Uint8Array(buf);
}

function toBase64Url(bytes: Uint8Array): string {
  let bin = '';
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function fromBase64Url(token: string): Uint8Array<ArrayBuffer> {
  const b64 = token.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (token.length % 4)) % 4);
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/** ペイロードを URL ハッシュに載せるトークン (base64url) にする。 */
export async function encodeSharePayload(payload: SharePayload): Promise<string> {
  const json = new TextEncoder().encode(JSON.stringify(payload));
  const compressed = await pipeThroughStream(json, new CompressionStream('deflate-raw'));
  return toBase64Url(compressed);
}

/** トークンを復号・検証する。壊れている／非対応なら null。 */
export async function decodeSharePayload(token: string): Promise<SharePayload | null> {
  if (!token || !/^[A-Za-z0-9_-]+$/.test(token)) return null;
  try {
    const compressed = fromBase64Url(token);
    const json = await pipeThroughStream(compressed, new DecompressionStream('deflate-raw'));
    return sanitizeSharePayload(JSON.parse(new TextDecoder().decode(json)));
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// URL
// ---------------------------------------------------------------------------

/** 共有 URL (ページ URL + #s=token) を組み立てる。 */
export function buildShareUrl(pageUrl: string, token: string): string {
  return `${pageUrl}#${SHARE_HASH_PARAM}=${token}`;
}

/** location.hash から共有トークンを取り出す。無ければ null。 */
export function extractShareToken(hash: string): string | null {
  const h = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!h) return null;
  const token = new URLSearchParams(h).get(SHARE_HASH_PARAM);
  return token && token.length > 0 ? token : null;
}

/**
 * 現在の URL から共有トークンのハッシュを外す (履歴は増やさない)。
 * 共有結果の表示を終えたあと、リロードや再共有で古い結果が復活しないようにする。
 */
export function clearShareHashFromUrl(): void {
  if (typeof window === 'undefined') return;
  if (!extractShareToken(window.location.hash)) return;
  try {
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`);
  } catch {
    /* 履歴操作が拒否される環境では何もしない */
  }
}
