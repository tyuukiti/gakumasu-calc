import { describe, it, expect, vi, afterEach } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  ATTEMPTS_KEY,
  DONE_KEY,
  IN_FLIGHT_KEY,
  LEGACY_STORAGE_KEYS,
  MAX_ATTEMPTS,
  MIGRATE_PAGE_URL,
  MIGRATION_DEADLINE_MS,
  NEW_HOST,
  NOTICE_KEY,
  applyLegacyData,
  decodeLegacyPayload,
  encodeLegacyPayload,
  extractMigrationToken,
  runLegacyMigration,
  shouldAttemptMigration,
  stripMigrationParam,
} from '../src/services/legacyMigration';

/**
 * 旧 URL からのブラウザ保存データ引き継ぎ。
 * - token のエンコード/デコードと取り込みポリシー (未設定キーのみ書き込む)
 * - 起動時の往復判定 (window / navigator / fetch をスタブして分岐を確認)
 * - 引き継ぎページ (web/migrate/gakumasu-calc-migrate.html) のスクリプトを実行し、アプリ側で復号できることを確認
 */

class FakeStorage {
  private map = new Map<string, string>();
  get length(): number {
    return this.map.size;
  }
  key(i: number): string | null {
    return [...this.map.keys()][i] ?? null;
  }
  getItem(k: string): string | null {
    return this.map.has(k) ? (this.map.get(k) as string) : null;
  }
  setItem(k: string, v: string): void {
    this.map.set(k, String(v));
  }
  removeItem(k: string): void {
    this.map.delete(k);
  }
  clear(): void {
    this.map.clear();
  }
}

const SAMPLE: Record<string, string> = {
  gakumasu_inventory: JSON.stringify([{ card_id: 'SP_SSR_0001', owned: true, uncap: 2 }]),
  selectedCharacterId: 'hanami_saki',
  hifBonusLevels: JSON.stringify({ finalStatLimitLevel: 3 }),
};

const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/130.0';

describe('encodeLegacyPayload / decodeLegacyPayload', () => {
  it('deflate (z 形式) でラウンドトリップする', async () => {
    const token = await encodeLegacyPayload(SAMPLE);
    expect(token.startsWith('z')).toBe(true);
    expect(await decodeLegacyPayload(token)).toEqual(SAMPLE);
  });

  it('非圧縮 (j 形式) でもラウンドトリップする', async () => {
    const token = await encodeLegacyPayload(SAMPLE, false);
    expect(token.startsWith('j')).toBe(true);
    expect(await decodeLegacyPayload(token)).toEqual(SAMPLE);
  });

  it('対象外のキーと文字列でない値は捨てる', async () => {
    const token = await encodeLegacyPayload({ ...SAMPLE, somethingElse: 'x' });
    expect(await decodeLegacyPayload(token)).toEqual(SAMPLE);
    const raw = 'j' + Buffer.from(JSON.stringify({ v: 1, keys: { selectedCharacterId: 1 } })).toString('base64url');
    expect(await decodeLegacyPayload(raw)).toEqual({});
  });

  it('0・壊れた token・不明な形式・別バージョンは null', async () => {
    expect(await decodeLegacyPayload('0')).toBeNull();
    expect(await decodeLegacyPayload('')).toBeNull();
    expect(await decodeLegacyPayload('zAAAA')).toBeNull();
    expect(await decodeLegacyPayload('x' + (await encodeLegacyPayload(SAMPLE)).slice(1))).toBeNull();
    const badVersion = 'j' + Buffer.from(JSON.stringify({ v: 2, keys: SAMPLE })).toString('base64url');
    expect(await decodeLegacyPayload(badVersion)).toBeNull();
  });
});

describe('URL ハッシュ', () => {
  it('m= を取り出し、他のパラメータ (共有 URL の s=) は残す', () => {
    expect(extractMigrationToken('#m=zabc&s=tok')).toBe('zabc');
    expect(extractMigrationToken('#s=tok')).toBeNull();
    expect(extractMigrationToken('')).toBeNull();
    expect(extractMigrationToken('#m=')).toBe('');
    expect(stripMigrationParam('#m=zabc&s=tok')).toBe('#s=tok');
    expect(stripMigrationParam('#m=zabc')).toBe('');
    expect(stripMigrationParam('#s=tok')).toBe('#s=tok');
  });
});

describe('applyLegacyData', () => {
  it('未設定のキーだけ書き込み、既存値は上書きせず、対象外は無視する', () => {
    const s = new FakeStorage();
    s.setItem('selectedCharacterId', 'already');
    const n = applyLegacyData({ ...SAMPLE, notAKey: 'x' }, s);
    expect(n).toBe(2);
    expect(s.getItem('selectedCharacterId')).toBe('already');
    expect(s.getItem('gakumasu_inventory')).toBe(SAMPLE.gakumasu_inventory);
    expect(s.getItem('hifBonusLevels')).toBe(SAMPLE.hifBonusLevels);
    expect(s.getItem('notAKey')).toBeNull();
  });
});

describe('shouldAttemptMigration', () => {
  const base = () => ({
    hostname: NEW_HOST,
    userAgent: UA,
    webdriver: false,
    now: MIGRATION_DEADLINE_MS - 1000,
    storage: new FakeStorage(),
  });

  it('新ドメイン・期限内・未実施なら試みる', () => {
    expect(shouldAttemptMigration(base())).toBe(true);
  });

  it('新ドメイン以外 (dev / preview) では試みない', () => {
    expect(shouldAttemptMigration({ ...base(), hostname: 'localhost' })).toBe(false);
    expect(shouldAttemptMigration({ ...base(), hostname: 'tyuukiti.github.io' })).toBe(false);
  });

  it('期限後は試みない', () => {
    expect(shouldAttemptMigration({ ...base(), now: MIGRATION_DEADLINE_MS + 1 })).toBe(false);
  });

  it('クローラー・自動操作では試みない', () => {
    expect(shouldAttemptMigration({ ...base(), userAgent: 'Mozilla/5.0 (compatible; Googlebot/2.1)' })).toBe(false);
    expect(shouldAttemptMigration({ ...base(), userAgent: 'Twitterbot/1.0' })).toBe(false);
    expect(shouldAttemptMigration({ ...base(), webdriver: true })).toBe(false);
  });

  it('引き継ぎ済み・失敗上限なら試みない', () => {
    const done = base();
    done.storage.setItem(DONE_KEY, '2026-09-28T00:00:00Z');
    expect(shouldAttemptMigration(done)).toBe(false);
    const failed = base();
    failed.storage.setItem(ATTEMPTS_KEY, String(MAX_ATTEMPTS));
    expect(shouldAttemptMigration(failed)).toBe(false);
  });
});

interface BrowserStub {
  local: FakeStorage;
  session: FakeStorage;
  replace: ReturnType<typeof vi.fn>;
  fetch: ReturnType<typeof vi.fn>;
}

function stubBrowser(
  loc: { pathname?: string; search?: string; hash?: string; hostname?: string },
  fetchImpl?: () => Promise<unknown>,
): BrowserStub {
  const local = new FakeStorage();
  const session = new FakeStorage();
  const replace = vi.fn();
  const fetch = vi.fn(fetchImpl ?? (() => Promise.reject(new TypeError('Failed to fetch'))));
  vi.stubGlobal('window', {
    localStorage: local,
    sessionStorage: session,
    location: {
      hostname: loc.hostname ?? NEW_HOST,
      pathname: loc.pathname ?? '/hif',
      search: loc.search ?? '',
      hash: loc.hash ?? '',
      replace,
    },
  });
  vi.stubGlobal('navigator', { userAgent: UA, webdriver: false });
  vi.stubGlobal('fetch', fetch);
  return { local, session, replace, fetch };
}

describe('runLegacyMigration', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('帰還時: token を取り込み、フラグと件数を記録し、m= だけ外して再読込する', async () => {
    const token = await encodeLegacyPayload(SAMPLE);
    const env = stubBrowser({ pathname: '/hif', search: '?x=1', hash: `#m=${token}&s=tok` });
    expect(await runLegacyMigration()).toBe(true);
    expect(env.local.getItem('gakumasu_inventory')).toBe(SAMPLE.gakumasu_inventory);
    expect(env.local.getItem('selectedCharacterId')).toBe(SAMPLE.selectedCharacterId);
    expect(env.local.getItem(DONE_KEY)).not.toBeNull();
    expect(env.session.getItem(NOTICE_KEY)).toBe('3');
    expect(env.replace).toHaveBeenCalledWith('/hif?x=1#s=tok');
    expect(env.fetch).not.toHaveBeenCalled();
  });

  it('帰還時: データなし (m=0) でも引き継ぎ済みにし、案内は出さない', async () => {
    const env = stubBrowser({ hash: '#m=0' });
    expect(await runLegacyMigration()).toBe(true);
    expect(env.local.getItem(DONE_KEY)).not.toBeNull();
    expect(env.session.getItem(NOTICE_KEY)).toBeNull();
    expect(env.replace).toHaveBeenCalledWith('/hif');
  });

  it('初回訪問: 引き継ぎページが公開されていれば、元のパス・クエリ・ハッシュを添えて往復に出る', async () => {
    const env = stubBrowser({ pathname: '/legend', search: '?x=1', hash: '#s=tok' }, () => Promise.resolve({ ok: true }));
    expect(await runLegacyMigration()).toBe(true);
    expect(env.fetch).toHaveBeenCalledTimes(1);
    expect(env.fetch.mock.calls[0][0]).toBe(MIGRATE_PAGE_URL);
    expect(env.replace).toHaveBeenCalledWith(`${MIGRATE_PAGE_URL}?to=${encodeURIComponent('/legend?x=1#s=tok')}`);
    expect(env.session.getItem(IN_FLIGHT_KEY)).toBe('1');
  });

  it('初回訪問: ページが未公開 (fetch 失敗) なら往復せず失敗回数を数え、上限で以後は確認もしない', async () => {
    const env = stubBrowser({});
    expect(await runLegacyMigration()).toBe(false);
    expect(env.replace).not.toHaveBeenCalled();
    expect(env.local.getItem(ATTEMPTS_KEY)).toBe('1');
    env.local.setItem(ATTEMPTS_KEY, String(MAX_ATTEMPTS - 1));
    expect(await runLegacyMigration()).toBe(false);
    expect(env.local.getItem(ATTEMPTS_KEY)).toBe(String(MAX_ATTEMPTS));
    env.fetch.mockClear();
    expect(await runLegacyMigration()).toBe(false);
    expect(env.fetch).not.toHaveBeenCalled();
  });

  it('404 応答でも往復しない', async () => {
    const env = stubBrowser({}, () => Promise.resolve({ ok: false }));
    expect(await runLegacyMigration()).toBe(false);
    expect(env.replace).not.toHaveBeenCalled();
    expect(env.local.getItem(ATTEMPTS_KEY)).toBe('1');
  });

  it('引き継ぎ済みなら確認も往復もしない', async () => {
    const env = stubBrowser({}, () => Promise.resolve({ ok: true }));
    env.local.setItem(DONE_KEY, 'x');
    expect(await runLegacyMigration()).toBe(false);
    expect(env.fetch).not.toHaveBeenCalled();
    expect(env.replace).not.toHaveBeenCalled();
  });

  it('往復に出たのに m= 無しで戻ってきたら打ち切って引き継ぎ済みにする (無限往復の防止)', async () => {
    const env = stubBrowser({}, () => Promise.resolve({ ok: true }));
    env.session.setItem(IN_FLIGHT_KEY, '1');
    expect(await runLegacyMigration()).toBe(false);
    expect(env.local.getItem(DONE_KEY)).not.toBeNull();
    expect(env.session.getItem(IN_FLIGHT_KEY)).toBeNull();
    expect(env.fetch).not.toHaveBeenCalled();
    expect(env.replace).not.toHaveBeenCalled();
  });

  it('新ドメイン以外 (ローカル開発) では何もしない', async () => {
    const env = stubBrowser({ hostname: 'localhost' }, () => Promise.resolve({ ok: true }));
    expect(await runLegacyMigration()).toBe(false);
    expect(env.fetch).not.toHaveBeenCalled();
    expect(env.replace).not.toHaveBeenCalled();
  });
});

// ---------------------------------------------------------------------------
// 引き継ぎページ (旧ホストで動く素の HTML) のスクリプトをそのまま実行して互換性を確認する
// ---------------------------------------------------------------------------

const PAGE_HTML = readFileSync(fileURLToPath(new URL('../migrate/gakumasu-calc-migrate.html', import.meta.url)), 'utf8');
const PAGE_SCRIPT = /<script>([\s\S]*?)<\/script>/.exec(PAGE_HTML)?.[1] ?? '';

/** location / localStorage / document を差し替えてページのスクリプトを実行し、location.replace の引数を返す。 */
function runMigratePage(search: string, storage: FakeStorage, opts: { compression?: boolean } = {}): Promise<string> {
  return new Promise<string>((resolve, reject) => {
    const location = { search, replace: (url: string) => resolve(url) };
    const document = { getElementById: () => ({ href: '' }) };
    try {
      const fn = new Function('location', 'localStorage', 'document', 'CompressionStream', PAGE_SCRIPT);
      fn(location, storage, document, opts.compression === false ? undefined : globalThis.CompressionStream);
    } catch (e) {
      reject(e);
    }
  });
}

describe('引き継ぎページ (web/migrate/gakumasu-calc-migrate.html)', () => {
  it('スクリプトが取り出せ、KEYS がアプリ側の LEGACY_STORAGE_KEYS と一致する', () => {
    expect(PAGE_SCRIPT.length).toBeGreaterThan(0);
    const m = /var KEYS = \[([\s\S]*?)\];/.exec(PAGE_SCRIPT);
    expect(m).not.toBeNull();
    const keys = [...(m?.[1] ?? '').matchAll(/'([^']+)'/g)].map((x) => x[1]);
    expect(keys).toEqual([...LEGACY_STORAGE_KEYS]);
  });

  it('旧データを z 形式の token に載せ、戻り先のパス・クエリ・ハッシュを保って新ドメインへ戻す', async () => {
    const s = new FakeStorage();
    for (const [k, v] of Object.entries(SAMPLE)) s.setItem(k, v);
    s.setItem('unrelated', 'x');
    const url = await runMigratePage(`?to=${encodeURIComponent('/hif?x=1#s=tok')}`, s);
    const u = new URL(url);
    expect(u.origin).toBe(`https://${NEW_HOST}`);
    expect(u.pathname).toBe('/hif');
    expect(u.search).toBe('?x=1');
    const token = extractMigrationToken(u.hash);
    expect(token?.startsWith('z')).toBe(true);
    expect(stripMigrationParam(u.hash)).toBe('#s=tok');
    expect(await decodeLegacyPayload(token as string)).toEqual(SAMPLE);
  });

  it('CompressionStream 非対応なら j 形式で送る', async () => {
    const s = new FakeStorage();
    for (const [k, v] of Object.entries(SAMPLE)) s.setItem(k, v);
    const url = await runMigratePage('?to=%2Fnia', s, { compression: false });
    const token = extractMigrationToken(new URL(url).hash);
    expect(token?.startsWith('j')).toBe(true);
    expect(await decodeLegacyPayload(token as string)).toEqual(SAMPLE);
  });

  it('旧データが無ければ m=0 で戻す', async () => {
    const url = await runMigratePage('?to=%2Fnia', new FakeStorage());
    expect(url).toBe(`https://${NEW_HOST}/nia#m=0`);
  });

  it('戻り先に外部 URL やプロトコル相対パスは使えない (常に新ドメイン配下)', async () => {
    const s = new FakeStorage();
    expect(await runMigratePage(`?to=${encodeURIComponent('//evil.example/x')}`, s)).toBe(`https://${NEW_HOST}/#m=0`);
    expect(await runMigratePage(`?to=${encodeURIComponent('https://evil.example/')}`, s)).toBe(`https://${NEW_HOST}/#m=0`);
    expect(await runMigratePage('', s)).toBe(`https://${NEW_HOST}/#m=0`);
  });
});
