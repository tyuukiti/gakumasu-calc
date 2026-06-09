import { describe, it, expect } from 'vitest';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { loadAllCards, loadPlan, REPO_ROOT } from './helpers/loadRealData';
import { computeParity, type ParityConfig } from './helpers/parity';
import type { TrainingPlan } from '../src/types/models';

/**
 * L4: クロス実装パリティ。
 * TS版とC#版が同じ設定 (configs.json) から同じ編成・合計を出すことを保証する。
 *
 * expected.json は本テストが初回に生成する。生成後はコミットし、以後 TS・C# 双方が
 * このファイルへ突合する。差分が出たら 2 実装が乖離した証拠 (= 要修正)。
 */

const PARITY_DIR = resolve(REPO_ROOT, 'TestFixtures', 'parity');
const CONFIG_PATH = resolve(PARITY_DIR, 'configs.json');
const EXPECTED_PATH = resolve(PARITY_DIR, 'expected.json');

const config = JSON.parse(readFileSync(CONFIG_PATH, 'utf-8')) as ParityConfig;
const allCards = loadAllCards();
const getPlan = (id: string): TrainingPlan => loadPlan(id);

describe('L4: クロス実装パリティ (TS基準)', () => {
  const actual = computeParity(config, getPlan, allCards);

  it('expected.json と一致する (なければ生成)', () => {
    if (!existsSync(EXPECTED_PATH)) {
      writeFileSync(EXPECTED_PATH, JSON.stringify(actual, null, 2) + '\n', 'utf-8');
      console.warn(`[parity] expected.json を生成しました: ${EXPECTED_PATH}`);
      return; // 初回生成時はアサートしない
    }
    const expected = JSON.parse(readFileSync(EXPECTED_PATH, 'utf-8'));
    expect(actual).toEqual(expected);
  });

  it('各シナリオが空でない結果を返す', () => {
    for (const sc of config.scenarios) {
      expect(actual[sc.id]?.length ?? 0).toBeGreaterThan(0);
    }
  });
});
