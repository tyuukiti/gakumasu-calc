import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import yaml from 'js-yaml';
import { normalizeCard, normalizePlan, normalizeTemplate } from '../../src/services/yamlLoader';
import { emptyAdditionalCounts } from '../../src/types/models';
import type {
  SupportCard,
  SupportCardFile,
  TrainingPlan,
  TrainingPlanFile,
  Character,
  CharacterFile,
  EventCountTemplate,
  EventCountTemplateFile,
  AdditionalCounts,
} from '../../src/types/models';

/**
 * node(vitest) 環境で実 YAML データ (`Data/`) を読み込むテスト用ローダ。
 * ブラウザ実行時の {@link yamlLoader} と同じ normalize 関数を再利用するので、
 * 正規化の挙動はアプリ本体と一致する。
 */

const HERE = dirname(fileURLToPath(import.meta.url)); // web/tests/helpers
const REPO_ROOT = resolve(HERE, '..', '..', '..'); // gakumasu_tool/
const DATA_DIR = resolve(REPO_ROOT, 'Data');

function loadYaml<T>(relPath: string): T {
  const text = readFileSync(resolve(DATA_DIR, relPath), 'utf-8');
  return yaml.load(text) as T;
}

const CARD_FILES = [
  'SupportCards/ssr_cards.yaml',
  'SupportCards/sr_cards.yaml',
  'SupportCards/r_cards.yaml',
];

const PLAN_FILES = [
  'Plans/hatsu_legend.yaml',
  'Plans/nia.yaml',
  'Plans/hif.yaml',
];

let _cards: SupportCard[] | null = null;
let _plans: TrainingPlan[] | null = null;
let _characters: Character[] | null = null;
let _templates: EventCountTemplate[] | null = null;

/** 全サポートカード (SSR/SR/R) を読み込む。結果はキャッシュされる。 */
export function loadAllCards(): SupportCard[] {
  if (_cards) return _cards;
  const out: SupportCard[] = [];
  for (const file of CARD_FILES) {
    const data = loadYaml<SupportCardFile>(file);
    if (data.support_cards) out.push(...data.support_cards.map(normalizeCard));
  }
  // あえて本番(Web)と同じ読込順 (ssr→sr→r) のまま渡す。最適化器が内部で ID 昇順に
  // 正準化するため、パリティが通れば「実装が入力順非依存」であることの証明になる。
  _cards = out;
  return out;
}

/** 全育成プランを読み込む。結果はキャッシュされる。 */
export function loadPlans(): TrainingPlan[] {
  if (_plans) return _plans;
  const out: TrainingPlan[] = [];
  for (const file of PLAN_FILES) {
    const data = loadYaml<TrainingPlanFile>(file);
    if (data.plan) out.push(normalizePlan(data.plan));
  }
  _plans = out;
  return out;
}

/** id で 1 プランを取得 (見つからなければ例外)。 */
export function loadPlan(id: string): TrainingPlan {
  const plan = loadPlans().find((p) => p.id === id);
  if (!plan) throw new Error(`plan not found: ${id}`);
  return plan;
}

/** id でカードを取得 (見つからなければ例外)。 */
export function getCard(id: string): SupportCard {
  const card = loadAllCards().find((c) => c.id === id);
  if (!card) throw new Error(`card not found: ${id}`);
  return card;
}

/** 全キャラクターを読み込む。結果はキャッシュされる。 */
export function loadCharacters(): Character[] {
  if (_characters) return _characters;
  const data = loadYaml<CharacterFile>('Characters/characters.yaml');
  _characters = data.characters ?? [];
  return _characters;
}

/** 全イベント回数テンプレートを読み込む。結果はキャッシュされる。 */
export function loadTemplates(): EventCountTemplate[] {
  if (_templates) return _templates;
  const data = loadYaml<EventCountTemplateFile>('Templates/event_count_templates.yaml');
  _templates = (data.templates ?? []).map(normalizeTemplate);
  return _templates;
}

/** (planId, name) でテンプレートを取得 (見つからなければ例外)。 */
export function getTemplate(planId: string, name: string): EventCountTemplate {
  const t = loadTemplates().find((x) => x.plan_id === planId && x.name === name);
  if (!t) throw new Error(`template not found: ${planId} / ${name}`);
  return t;
}

/** テンプレートの counts を完全な AdditionalCounts に変換 (未指定キーは 0)。 */
export function templateAdditionalCounts(planId: string, name: string): AdditionalCounts {
  return { ...emptyAdditionalCounts(), ...getTemplate(planId, name).counts };
}

export { DATA_DIR, REPO_ROOT };
