import yaml from 'js-yaml'
import type { SupportCard, SupportCardFile, TrainingPlan, TrainingPlanFile, EventCountTemplate, EventCountTemplateFile, WeekSchedule } from '../types/models'
import type { ActionType } from '../types/enums'

const BASE = import.meta.env.BASE_URL

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
  }
}

function normalizePlan(plan: TrainingPlan): TrainingPlan {
  return {
    ...plan,
    status_limit: plan.status_limit ?? 2800,
    base_status: plan.base_status ?? { vo: 0, da: 0, vi: 0 },
    schedule: (plan.schedule ?? []).map(normalizeWeek),
    activity_supply: plan.activity_supply,
  }
}

function normalizeCard(card: SupportCard): SupportCard {
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
  const files = ['Plans/hatsu_legend.yaml', 'Plans/nia.yaml']
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

function normalizeTemplate(t: EventCountTemplate): EventCountTemplate {
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
