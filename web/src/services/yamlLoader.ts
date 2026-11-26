import yaml from 'js-yaml'
import type { SupportCard, SupportCardFile, TrainingPlan, TrainingPlanFile, EventCountTemplate, EventCountTemplateFile, WeekSchedule, Character, CharacterFile, HifPouchDefinition, HifPouchFile, HifPouchStageEffect, HifPouchStageWindow, HifPouchTrigger } from '../types/models'
import { emptyAdditionalCounts } from '../types/models'
import type { ActionType } from '../types/enums'
import { siteBasePath } from './siteBase'

const BASE = siteBasePath()

async function fetchYaml<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}Data/${path}`)
  if (!res.ok) throw new Error(`Failed to fetch ${path}: ${res.status}`)
  const text = await res.text()
  return yaml.load(text) as T
}

/** YAMLで省略されたフィールドにデフォルト値を補完する */
function normalizeWeek(w: Partial<WeekSchedule>): WeekSchedule {
  return {
    week: w.week ?? 0,
    type: w.type ?? 'free',
    available_actions: w.available_actions ?? [],
    lessons: w.lessons ?? [],
    classes: w.classes ?? [],
    event_name: w.event_name,
    status_gain: w.status_gain,
    outing_effect: w.outing_effect,
    class_effect: w.class_effect,
    consultation_effect: w.consultation_effect,
    special_training_effect: w.special_training_effect,
    hif_sub_value: w.hif_sub_value,
    hif_exam_base: w.hif_exam_base,
    hif_exam_distributed: w.hif_exam_distributed,
    nia_audition_tiers: w.nia_audition_tiers,
  }
}

export function normalizePlan(plan: TrainingPlan): TrainingPlan {
  return {
    ...plan,
    status_limit: plan.status_limit ?? 2800,
    base_status: plan.base_status ?? { vo: 0, da: 0, vi: 0 },
    schedule: (plan.schedule ?? []).map(normalizeWeek),
    activity_supply: plan.activity_supply,
  }
}

export function normalizeCard(card: SupportCard): SupportCard {
  return {
    ...card,
    plan: card.plan ?? '',
    effects: (card.effects ?? []).map(e => ({
      ...e,
      values: e.values ?? [],
      value_type: e.value_type ?? 'flat',
    })),
  }
}

export async function loadSupportCards(): Promise<SupportCard[]> {
  const files = ['SupportCards/ssr_cards.yaml', 'SupportCards/sr_cards.yaml', 'SupportCards/r_cards.yaml']
  const results: SupportCard[] = []
  for (const file of files) {
    try {
      const data = await fetchYaml<SupportCardFile>(file)
      if (data.support_cards) {
        results.push(...data.support_cards.map(normalizeCard))
      }
    } catch (e) {
      console.warn(`YAML読み込みエラー (${file}):`, e)
    }
  }
  return results
}

export async function loadTrainingPlans(): Promise<TrainingPlan[]> {
  const files = ['Plans/hatsu_legend.yaml', 'Plans/nia.yaml', 'Plans/hif.yaml']
  const results: TrainingPlan[] = []
  for (const file of files) {
    try {
      const data = await fetchYaml<TrainingPlanFile>(file)
      if (data.plan) {
        results.push(normalizePlan(data.plan))
      }
    } catch (e) {
      console.warn(`YAML読み込みエラー (${file}):`, e)
    }
  }
  return results
}

export function normalizeTemplate(t: EventCountTemplate): EventCountTemplate {
  if (!t.week_actions) return t
  const norm: Record<number, ActionType> = {}
  for (const [k, v] of Object.entries(t.week_actions)) {
    const weekNum = Number(k)
    if (!Number.isNaN(weekNum)) {
      norm[weekNum] = v as ActionType
    }
  }
  return { ...t, week_actions: norm }
}

export async function loadEventCountTemplates(): Promise<EventCountTemplate[]> {
  try {
    const data = await fetchYaml<EventCountTemplateFile>('Templates/event_count_templates.yaml')
    return (data.templates ?? []).map(normalizeTemplate)
  } catch (e) {
    console.warn('テンプレート読み込みエラー:', e)
    return []
  }
}

export async function loadCharacters(): Promise<Character[]> {
  try {
    const data = await fetchYaml<CharacterFile>('Characters/characters.yaml')
    return data.characters ?? []
  } catch (e) {
    console.warn('キャラデータ読み込みエラー:', e)
    return []
  }
}

// --- HIF カスタムPアイテム (ポーチ) ---

const HIF_POUCH_TRIGGERS: readonly HifPouchTrigger[] = ['lesson_end', 'consultation', 'activity_supply', 'outing_end']

/** stage_windows が省略されたときの既定 (選抜①〜② / ②〜③ / 本戦) */
const DEFAULT_HIF_POUCH_WINDOWS: readonly HifPouchStageWindow[] = [
  { stage: 0, from_week: 8, to_week: 12 },
  { stage: 1, from_week: 14, to_week: 19 },
  { stage: 2, from_week: 21, to_week: 26 },
]

/** 1段階分の効果を正規化。未知の trigger は発動しない (max_count 0)、未知の effects キーは警告して除外 */
function normalizeHifPouchEffect(e: Partial<HifPouchStageEffect> | undefined, label: string): HifPouchStageEffect {
  const known = emptyAdditionalCounts()
  const effects: Record<string, number> = {}
  for (const [k, v] of Object.entries(e?.effects ?? {})) {
    if (!(k in known)) {
      console.warn(`ポーチ効果の未知キーを無視 (${label}): ${k}`)
      continue
    }
    if (typeof v === 'number' && v > 0) effects[k] = v
  }
  const triggerOk = HIF_POUCH_TRIGGERS.includes(e?.trigger as HifPouchTrigger)
  if (!triggerOk) console.warn(`ポーチ効果の未知トリガー (${label}): ${String(e?.trigger)}`)
  return {
    trigger: triggerOk ? (e!.trigger as HifPouchTrigger) : 'lesson_end',
    max_count: triggerOk ? Math.max(0, Math.floor(e?.max_count ?? 0)) : 0,
    effects,
    description: e?.description,
  }
}

export function normalizeHifPouch(def: Partial<HifPouchDefinition> | undefined | null): HifPouchDefinition {
  const colors = (def?.colors ?? []).map((c) => ({
    id: c.id,
    name: c.name ?? c.id,
    base: normalizeHifPouchEffect(c.base, c.id),
    plushes: (c.plushes ?? []).map((p) => ({
      ...normalizeHifPouchEffect(p, `${c.id}/${p.id}`),
      id: p.id,
      name: p.name ?? p.id,
      decorations: (p.decorations ?? []).map((d) => ({
        ...normalizeHifPouchEffect(d, `${c.id}/${p.id}/${d.id}`),
        id: d.id,
        name: d.name ?? d.id,
      })),
    })),
  }))
  const rawWindows = def?.stage_windows ?? []
  const stage_windows = rawWindows.length > 0
    ? rawWindows.map((w) => ({ stage: w.stage ?? 0, from_week: w.from_week ?? 0, to_week: w.to_week ?? 0 }))
    : [...DEFAULT_HIF_POUCH_WINDOWS]
  return { stage_windows, colors }
}

export async function loadHifPouch(): Promise<HifPouchDefinition | null> {
  try {
    const data = await fetchYaml<HifPouchFile>('Hif/pouch.yaml')
    return normalizeHifPouch(data.hif_pouch)
  } catch (e) {
    console.warn('ポーチ定義読み込みエラー:', e)
    return null
  }
}
