import { fromBase64Url, pipeThroughStream, toBase64Url } from './shareState';
import { trackEvent } from '../utils/analytics';

/**
 * 旧 URL (https://tyuukiti.github.io/gakumasu-calc/) のブラウザ保存データを、
 * 新ドメイン (https://gakumasu.tyuukiti.com/) へ利用者の操作なしで引き継ぐ。
 *
 * localStorage はホスト (origin) 単位なので、旧 URL で登録した所持カード・プリセット等は
 * 新ドメインからは読めない。ただし旧ホスト tyuukiti.github.io 自体は今も GitHub Pages として
 * 生きている (転送されるのは /gakumasu-calc/ 配下だけ) ので、同じホストで公開している
 * 別プロダクト gakumasu-anomaly-sim の配下に置いた静的ページからは旧データを読める。そこで:
 *
 *   1. 新ドメインを初めて開いたとき (引き継ぎ済みフラグなし)、引き継ぎページが公開されているか
 *      fetch で確かめてから location.replace で移動する (?to=<元のパス+クエリ+ハッシュ>)
 *   2. 引き継ぎページ (web/migrate/gakumasu-calc-migrate.html を anomaly-sim の public/ に置いて公開)
 *      が旧ホストの localStorage を読み、#m=<token> に載せて新ドメインの元のパスへ即座に戻す
 *   3. 戻ってきた新ドメイン側が token を復号し、未設定のキーだけ localStorage に書き込んで
 *      フラグを立て、ハッシュから m を外して再読込する (各ストアは起動時に localStorage を読むため)
 *
 * iframe + postMessage 方式は、近年のブラウザが第三者コンテキストのストレージを分離する
 * (旧ホストの実データが見えない) ため採用しない。トップレベル遷移の往復なら分離されない。
 *
 * token 形式: 'z' + base64url(deflate-raw(JSON)) / 'j' + base64url(JSON) / '0' (データなし・失敗)
 * JSON: { v: 1, keys: { <localStorage キー>: <値>, ... } }
 * token は URL のフラグメント (#) に載るのでサーバーには送られず、ブラウザの中だけを移動する。
 *
 * 期限 (MIGRATION_DEADLINE_MS) を過ぎたら往復しない。以降はこのモジュール・LegacyMigrationNotice・
 * web/migrate/ と anomaly-sim 側の引き継ぎページを削除してよい。
 */

export const NEW_HOST = 'gakumasu.tyuukiti.com';
/**
 * 旧ホスト上の引き継ぎページ。旧 URL と同じホストで公開中の別プロダクト配下に置いた静的ファイル。
 * ディレクトリではなくファイル名で指すので、末尾スラッシュ有無による 301 は起きない。
 * anomaly-sim にカスタムドメインを付けると旧ホストで動かなくなるので、期限まで付けないこと。
 */
export const MIGRATE_PAGE_URL = 'https://tyuukiti.github.io/gakumasu-anomaly-sim/gakumasu-calc-migrate.html';
export const MIGRATION_HASH_PARAM = 'm';
/** 引き継ぎを試みる期限: 2027-03-31 JST 終日 (= 2027-03-31T15:00Z)。 */
export const MIGRATION_DEADLINE_MS = Date.UTC(2027, 2, 31, 15, 0, 0);
export const LEGACY_PAYLOAD_VERSION = 1 as const;

/**
 * 引き継ぐ localStorage キー。旧 URL のデータはもう増えないので、この一覧が増えることはない。
 * web/migrate/gakumasu-calc-migrate.html の KEYS と同一であること (テストで照合)。
 */
export const LEGACY_STORAGE_KEYS: readonly string[] = [
  'gakumasu_inventory',
  'selectedCharacterId',
  'uncap3CharacterBonusEnabled',
  'uncap3BonusByChar',
  'step4BonusByChar',
  'memoryPresets',
  'eventCountPresets',
  'scheduleChoicePresets',
  'hifSchedulePresets',
  'hifBonusLevels',
  'hifOverflowPenalty',
  'hifConditionPresets',
  'schedulePanelHeight',
  'weekBreakdownPanelHeight',
  'hifSchedulePanelHeight',
];

/** localStorage: 引き継ぎ済み (ISO 日時)。あれば二度と往復しない。 */
export const DONE_KEY = 'legacyMigrationDone';
/** localStorage: 事前確認の失敗回数。MAX_ATTEMPTS に達したら諦める (ページ未公開・ネットワーク不調)。 */
export const ATTEMPTS_KEY = 'legacyMigrationAttempts';
/** sessionStorage: 往復中。m= 無しで戻ってきたらページ側の不調とみなして打ち切る (無限往復の防止)。 */
export const IN_FLIGHT_KEY = 'legacyMigrationInFlight';
/** sessionStorage: 引き継いだキー数。再読込後に LegacyMigrationNotice が一度だけ読んで消す。 */
export const NOTICE_KEY = 'legacyMigrationNotice';
export const MAX_ATTEMPTS = 3;
const PROBE_TIMEOUT_MS = 3000;
/** クローラー・プレビュー生成・自動テストは往復させない (SEO と計測を汚さない)。 */
const BOT_UA = /bot|crawl|spider|slurp|facebookexternalhit|preview|lighthouse|headless/i;

export type LegacyKeyValues = Record<string, string>;
export type StorageLike = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>;

// ---------------------------------------------------------------------------
// token のエンコード / デコード
// ---------------------------------------------------------------------------

/** 引き継ぎページと同じ手順の参照実装 (テスト用)。compress=false は CompressionStream 非対応時の 'j' 形式。 */
export async function encodeLegacyPayload(keys: LegacyKeyValues, compress = true): Promise<string> {
  const json = new TextEncoder().encode(JSON.stringify({ v: LEGACY_PAYLOAD_VERSION, keys }));
  if (!compress) return `j${toBase64Url(json)}`;
  const deflated = await pipeThroughStream(json, new CompressionStream('deflate-raw'));
  return `z${toBase64Url(deflated)}`;
}

function sanitizeLegacyPayload(raw: unknown): LegacyKeyValues | null {
  if (typeof raw !== 'object' || raw === null) return null;
  const r = raw as Record<string, unknown>;
  if (r.v !== LEGACY_PAYLOAD_VERSION || typeof r.keys !== 'object' || r.keys === null) return null;
  const out: LegacyKeyValues = {};
  for (const [k, v] of Object.entries(r.keys as Record<string, unknown>)) {
    if (LEGACY_STORAGE_KEYS.includes(k) && typeof v === 'string') out[k] = v;
  }
  return out;
}

/** token を復号・検証し、引き継ぎ対象キーだけを返す。'0'・壊れた token・非対応形式は null。 */
export async function decodeLegacyPayload(token: string): Promise<LegacyKeyValues | null> {
  const kind = token.charAt(0);
  const body = token.slice(1);
  if ((kind !== 'z' && kind !== 'j') || !/^[A-Za-z0-9_-]+$/.test(body)) return null;
  try {
    const bytes = fromBase64Url(body);
    const json = kind === 'z' ? await pipeThroughStream(bytes, new DecompressionStream('deflate-raw')) : bytes;
    return sanitizeLegacyPayload(JSON.parse(new TextDecoder().decode(json)));
  } catch {
    return null;
  }
}

// ---------------------------------------------------------------------------
// URL ハッシュ
// ---------------------------------------------------------------------------

/** location.hash から引き継ぎ token を取り出す。m= が無ければ null (空文字は「付いているが空」)。 */
export function extractMigrationToken(hash: string): string | null {
  const h = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!h) return null;
  return new URLSearchParams(h).get(MIGRATION_HASH_PARAM);
}

/** ハッシュから m= だけを外す。共有 URL の s= など他のパラメータは残す。 */
export function stripMigrationParam(hash: string): string {
  const h = hash.startsWith('#') ? hash.slice(1) : hash;
  const params = new URLSearchParams(h);
  params.delete(MIGRATION_HASH_PARAM);
  const rest = params.toString();
  return rest ? `#${rest}` : '';
}

// ---------------------------------------------------------------------------
// 取り込み・判定
// ---------------------------------------------------------------------------

/** 未設定のキーだけ書き込む (新ドメインで先に登録した内容は上書きしない)。戻り値は書き込んだ数。 */
export function applyLegacyData(data: LegacyKeyValues, storage: StorageLike): number {
  let count = 0;
  for (const [k, v] of Object.entries(data)) {
    if (!LEGACY_STORAGE_KEYS.includes(k)) continue;
    if (storage.getItem(k) != null) continue;
    storage.setItem(k, v);
    count++;
  }
  return count;
}

export interface MigrationEnv {
  hostname: string;
  userAgent: string;
  webdriver: boolean;
  now: number;
  storage: StorageLike;
}

/** 往復を試みるべきか。新ドメイン以外 (dev/preview)・期限後・クローラー・引き継ぎ済み・失敗上限では false。 */
export function shouldAttemptMigration(env: MigrationEnv): boolean {
  if (env.hostname !== NEW_HOST) return false;
  if (env.now > MIGRATION_DEADLINE_MS) return false;
  if (env.webdriver || BOT_UA.test(env.userAgent)) return false;
  if (env.storage.getItem(DONE_KEY) != null) return false;
  if (Number(env.storage.getItem(ATTEMPTS_KEY) ?? 0) >= MAX_ATTEMPTS) return false;
  return true;
}

/**
 * 引き継ぎページが公開されているか。GitHub Pages は 200 に Access-Control-Allow-Origin: * を付けるが
 * 404 には付けないので、未公開なら CORS エラーとして reject される → false。
 */
async function migratePageExists(): Promise<boolean> {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), PROBE_TIMEOUT_MS);
  try {
    const res = await fetch(MIGRATE_PAGE_URL, {
      method: 'GET',
      mode: 'cors',
      cache: 'no-store',
      redirect: 'error',
      signal: ctrl.signal,
    });
    return res.ok;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * 起動時に main.tsx から呼ぶ。true を返したときは別ページへ遷移中 (または再読込中) なので描画しない。
 * どこかで例外が出ても false を返し、通常どおり描画させる。
 */
export async function runLegacyMigration(): Promise<boolean> {
  if (typeof window === 'undefined') return false;
  try {
    const local = window.localStorage;
    const session = window.sessionStorage;
    const { location } = window;

    // 帰還: token を取り込んでフラグを立て、m= を外して再読込
    const token = extractMigrationToken(location.hash);
    if (token !== null) {
      session.removeItem(IN_FLIGHT_KEY);
      const data = await decodeLegacyPayload(token);
      const count = data ? applyLegacyData(data, local) : 0;
      local.setItem(DONE_KEY, new Date().toISOString());
      if (count > 0) session.setItem(NOTICE_KEY, String(count));
      trackEvent('legacy_migration', { keys: count });
      location.replace(`${location.pathname}${location.search}${stripMigrationParam(location.hash)}`);
      return true;
    }

    const env: MigrationEnv = {
      hostname: location.hostname,
      userAgent: navigator.userAgent,
      webdriver: navigator.webdriver === true,
      now: Date.now(),
      storage: local,
    };
    if (!shouldAttemptMigration(env)) return false;

    if (session.getItem(IN_FLIGHT_KEY) != null) {
      // 往復したのに m= を付けずに戻ってきた → 引き継ぎページの不調。以後は試みない
      session.removeItem(IN_FLIGHT_KEY);
      local.setItem(DONE_KEY, new Date().toISOString());
      return false;
    }

    if (!(await migratePageExists())) {
      local.setItem(ATTEMPTS_KEY, String(Number(local.getItem(ATTEMPTS_KEY) ?? 0) + 1));
      return false;
    }

    session.setItem(IN_FLIGHT_KEY, '1');
    const to = `${location.pathname}${location.search}${location.hash}`;
    location.replace(`${MIGRATE_PAGE_URL}?to=${encodeURIComponent(to)}`);
    return true;
  } catch {
    return false;
  }
}

let noticeCache: number | null = null;

/** 引き継ぎ直後の再読込で一度だけ返す件数。同じページ読込内では何度呼んでも同じ値 (StrictMode の二重呼び出し対策)。 */
export function consumeMigrationNotice(): number {
  if (noticeCache !== null) return noticeCache;
  if (typeof window === 'undefined') return 0;
  try {
    const v = window.sessionStorage.getItem(NOTICE_KEY);
    if (v != null) window.sessionStorage.removeItem(NOTICE_KEY);
    noticeCache = v == null ? 0 : Number(v) || 0;
  } catch {
    noticeCache = 0;
  }
  return noticeCache;
}
