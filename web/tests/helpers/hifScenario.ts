import type {
  TrainingPlan,
  TurnChoice,
  StatusValues,
  WeekSchedule,
  Character,
} from '../../src/types/models';
import type { ActionType } from '../../src/types/enums';
import { applyCharacterToggles } from '../../src/services/characterBonus';
import {
  defaultHifBonusLevels,
  HIF_BONUS_MAX_LEVELS,
  getVoFlatBonus,
  getDaFlatBonus,
  getViFlatBonus,
  getVoParaBonus,
  getDaParaBonus,
  getViParaBonus,
  getFinalCapBonus,
  type HifBonusLevels,
} from '../../src/types/hifBonus';

/**
 * HIF シナリオ再構築ヘルパ (hifStore の private 関数の複製。C# 版 Helpers/HifScenario.cs と対)。
 * 共有ペイロード / フィクスチャ (week は文字列キー) から、計算エンジンに渡す
 * TrainingPlan・TurnChoice[]・effective Character を hifStore と同じ規則で組み立てる。
 * ストア経由の復元 (applySharedResult) と結果が一致することはテスト側で検証する。
 */

export interface HifScenarioChoice {
  action: string;
  sub_stat?: string;
}

type Stat = 'vo' | 'da' | 'vi';

/** hifStore.buildPlanAndChoices の複製 */
export function buildHifPlanAndChoices(
  hifPlan: TrainingPlan,
  choices: Record<string, HifScenarioChoice>,
  examAllocations: Record<string, StatusValues>,
): { plan: TrainingPlan; turnChoices: TurnChoice[] } {
  const newSchedule: WeekSchedule[] = hifPlan.schedule.map((w) => {
    if (w.type === 'public_lesson') {
      const choice = choices[String(w.week)];
      if (!choice || !choice.sub_stat) return { ...w, lessons: [...w.lessons] };
      const mainStat = choice.action.split('_')[0] as Stat;
      const subStat = choice.sub_stat as Stat;
      const mainValue = (w.lessons.find((l) => l.type === mainStat)?.sp_bonus[mainStat] ?? 0) as number;
      const subValue = w.hif_sub_value ?? 0;
      const newLessons = w.lessons.map((l) => {
        if (l.type !== mainStat) return l;
        const sp: StatusValues = { vo: 0, da: 0, vi: 0 };
        sp[mainStat] = mainValue;
        sp[subStat] = (sp[subStat] ?? 0) + subValue;
        return { ...l, sp_bonus: sp };
      });
      return { ...w, lessons: newLessons };
    }
    // 試験日: 基礎値(全属性同値) + ユーザ配分値 を status_gain に反映
    if (w.type === 'audition' && (w.hif_exam_base != null || w.hif_exam_distributed != null)) {
      const base = w.hif_exam_base ?? 0;
      const alloc = examAllocations[String(w.week)] ?? { vo: 0, da: 0, vi: 0 };
      const status_gain: StatusValues = {
        vo: base + Math.max(0, Math.floor(alloc.vo)),
        da: base + Math.max(0, Math.floor(alloc.da)),
        vi: base + Math.max(0, Math.floor(alloc.vi)),
      };
      return { ...w, status_gain };
    }
    return w;
  });

  const newPlan: TrainingPlan = { ...hifPlan, schedule: newSchedule };
  const turnChoices: TurnChoice[] = [];
  for (const w of newSchedule) {
    if (w.type === 'audition' || w.type === 'fixed_event' || w.type === 'exam') continue;
    if (w.available_actions.length === 0) continue;
    const choice = choices[String(w.week)];
    if (!choice) continue;
    turnChoices.push({ week: w.week, chosen_action: choice.action as ActionType });
  }
  return { plan: newPlan, turnChoices };
}

/** 共有ペイロードのボーナスLv (欠落/範囲外は既定=MAX) を解決 (hifStore.applySharedResult と同じ) */
export function resolveHifBonusLevels(raw: Record<string, number>): HifBonusLevels {
  const levels = defaultHifBonusLevels();
  for (const key of Object.keys(HIF_BONUS_MAX_LEVELS) as (keyof HifBonusLevels)[]) {
    const v = raw[key];
    if (typeof v === 'number' && Number.isInteger(v) && v >= 0 && v <= HIF_BONUS_MAX_LEVELS[key]) {
      levels[key] = v;
    }
  }
  return levels;
}

/** 本戦上限増加を status_limit に加算した動的プラン (hifStore.executeCalculate と同じ) */
export function applyHifFinalCap(plan: TrainingPlan, levels: HifBonusLevels): TrainingPlan {
  const bonus = getFinalCapBonus(levels.finalStatLimitLevel);
  return bonus > 0 ? { ...plan, status_limit: plan.status_limit + bonus } : plan;
}

/** キャラ補正トグル (3凸/STEP4) + HIF 上昇パネルを合算した effective Character (hifStore.executeCalculate と同じ) */
export function buildHifEffectiveCharacter(
  character: Character | null | undefined,
  uncap3: boolean,
  step4: boolean,
  levels: HifBonusLevels,
): Character | null {
  const base = applyCharacterToggles(character, uncap3, step4);
  const voFlat = getVoFlatBonus(levels.voUpLevel);
  const daFlat = getDaFlatBonus(levels.daUpLevel);
  const viFlat = getViFlatBonus(levels.viUpLevel);
  const voPara = getVoParaBonus(levels.voUpLevel);
  const daPara = getDaParaBonus(levels.daUpLevel);
  const viPara = getViParaBonus(levels.viUpLevel);
  if (!base && !voFlat && !daFlat && !viFlat && !voPara && !daPara && !viPara) return null;
  return {
    id: base?.id ?? '__hif_bonus__',
    name: base?.name ?? 'HIF Bonus',
    color: base?.color ?? '#000000',
    initial: base?.initial ?? '',
    base_status_bonus: {
      vo: (base?.base_status_bonus.vo ?? 0) + voFlat,
      da: (base?.base_status_bonus.da ?? 0) + daFlat,
      vi: (base?.base_status_bonus.vi ?? 0) + viFlat,
    },
    para_bonus: {
      vo: (base?.para_bonus.vo ?? 0) + voPara,
      da: (base?.para_bonus.da ?? 0) + daPara,
      vi: (base?.para_bonus.vi ?? 0) + viPara,
    },
    uncap3_bonus: base?.uncap3_bonus,
    step4_bonus: base?.step4_bonus,
  };
}
