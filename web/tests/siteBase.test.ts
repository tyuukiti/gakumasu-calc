import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { GITHUB_PAGES_PROJECT_BASE, siteBasePath } from '../src/services/siteBase';

/**
 * 同じビルドを旧 URL (tyuukiti.github.io/gakumasu-calc/) とカスタムドメインの両方で動かすための
 * 実行時ベースパス決定。index.html の <base> 注入スクリプトと規則が一致していることも確認する。
 */

const INDEX_HTML = readFileSync(fileURLToPath(new URL('../index.html', import.meta.url)), 'utf8');

describe('siteBasePath', () => {
  it('旧ホスト (github.io) ではプロジェクトパス、それ以外はルート', () => {
    expect(siteBasePath('tyuukiti.github.io')).toBe('/gakumasu-calc/');
    expect(siteBasePath('gakumasu.tyuukiti.com')).toBe('/');
    expect(siteBasePath('localhost')).toBe('/');
    expect(siteBasePath('')).toBe('/');
  });

  it('相対 URL が旧ホスト・カスタムドメインのどちらでも正しく解決される', () => {
    const old = new URL(`https://tyuukiti.github.io${siteBasePath('tyuukiti.github.io')}`);
    expect(new URL('./assets/index.js', old).pathname).toBe('/gakumasu-calc/assets/index.js');
    expect(new URL('hif', old).pathname).toBe('/gakumasu-calc/hif');
    expect(new URL('./', old).pathname).toBe('/gakumasu-calc/');
    expect(new URL('favicon.ico', old).pathname).toBe('/gakumasu-calc/favicon.ico');
    const custom = new URL(`https://gakumasu.tyuukiti.com${siteBasePath('gakumasu.tyuukiti.com')}`);
    expect(new URL('./assets/index.js', custom).pathname).toBe('/assets/index.js');
    expect(new URL('hif', custom).pathname).toBe('/hif');
    expect(new URL('./', custom).pathname).toBe('/');
  });
});

describe('index.html の <base> 注入スクリプト', () => {
  const script = /<script>\s*([^<]*?location\.hostname\.endsWith\('\.github\.io'\)[^<]*?)<\/script>/.exec(INDEX_HTML)?.[1];

  it('siteBasePath と同じ規則で <base> を作る', () => {
    expect(script).toBeDefined();
    expect(script).toContain(`'${GITHUB_PAGES_PROJECT_BASE}'`);
    expect(script).toContain("'/'");
    expect(script).toContain("createElement('base')");
  });

  it('アセットや favicon より前 (head の先頭) にある', () => {
    const at = INDEX_HTML.indexOf("location.hostname.endsWith('.github.io')");
    expect(at).toBeGreaterThan(0);
    expect(at).toBeLessThan(INDEX_HTML.indexOf('<link rel="icon"'));
    expect(at).toBeLessThan(INDEX_HTML.indexOf('<script type="module"'));
  });

  it('静的リンクと favicon は相対パス (base 経由で解決)', () => {
    expect(INDEX_HTML).toContain('href="favicon.ico"');
    expect(INDEX_HTML).toContain('<a href="hif">');
    expect(INDEX_HTML).not.toMatch(/<a href="\/(hif|legend|nia|inventory|usage)"/);
  });
});
