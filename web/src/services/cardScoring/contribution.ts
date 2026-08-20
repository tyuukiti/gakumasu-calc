import type { SupportCard, TrainingPlan, StatusValues, TurnChoice } from '../../types/models';
import type { CardScore, EffectBreakdown, TeamBonusContributor } from '../../types/results';
import { sv } from '../../utils/statusValues';
import { getUncapLevel, getEffectValue } from '../statusCalculation';
import { isFixedEvent, getLesson, triggerDisplayName, buildReasonText, calculateFlatValue } from './helpers';

// --- Count triggers ---

export function countTriggers(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
  _mainStats: string[],
  turnChoices?: TurnChoice[],
): Record<string, number> {
  const counts: Record<string, number> = {};

  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => a.week - b.week);

  const totalLessons = Object.values(lessonAllocation).reduce(
    (sum, v) => sum + v,
    0,
  );
  counts['sp_end'] = Math.min(totalLessons, lessonWeeks.length);
  counts['lesson_end'] = counts['sp_end'];

  // 属性別SP終了・レッスン終了トリガー
  for (const [key, value] of Object.entries(lessonAllocation)) {
    if (value <= 0) continue;
    counts[`${key}_sp_end`] = value;
    counts[`${key}_lesson_end`] = value;
  }

  // 試験イベント数はスケジュールから確定
  for (const week of plan.schedule) {
    if (isFixedEvent(week)) {
      counts['exam_end'] = (counts['exam_end'] ?? 0) + 1;
    }
  }

  // HIFモード等、ユーザがターン選択を明示している場合は実選択ベースで集計する。
  // available_actions の優先度ベースだと「Day を 活動支給→お出かけ に変えても活動支給回数が減らない」
  // という不整合が起きるため。
  if (turnChoices != null) {
    for (const tc of turnChoices) {
      const a = tc.chosen_action as string;
      if (a === 'vo_lesson' || a === 'da_lesson' || a === 'vi_lesson') continue;
      if (a === 'vo_class' || a === 'da_class' || a === 'vi_class') {
        counts['class_end'] = (counts['class_end'] ?? 0) + 1;
      } else if (a === 'outing') {
        counts['outing_end'] = (counts['outing_end'] ?? 0) + 1;
      } else if (a === 'consultation') {
        counts['consultation'] = (counts['consultation'] ?? 0) + 1;
      } else if (a === 'activity_supply') {
        counts['activity_supply'] = (counts['activity_supply'] ?? 0) + 1;
      } else if (a === 'special_training') {
        counts['special_training'] = (counts['special_training'] ?? 0) + 1;
      } else if (a === 'rest') {
        counts['rest'] = (counts['rest'] ?? 0) + 1;
      }
    }
    return counts;
  }

  for (const week of plan.schedule) {
    if (isFixedEvent(week)) continue;
    if (week.lessons.length > 0) continue;

    const actions = week.available_actions;
    if (actions.includes('activity_supply')) {
      counts['activity_supply'] = (counts['activity_supply'] ?? 0) + 1;
    } else if (actions.includes('outing')) {
      counts['outing_end'] = (counts['outing_end'] ?? 0) + 1;
    } else if (actions.includes('consultation')) {
      counts['consultation'] = (counts['consultation'] ?? 0) + 1;
    } else if (actions.includes('special_training')) {
      counts['special_training'] = (counts['special_training'] ?? 0) + 1;
    } else if (
      actions.includes('vo_class') ||
      actions.includes('da_class') ||
      actions.includes('vi_class')
    ) {
      counts['class_end'] = (counts['class_end'] ?? 0) + 1;
    }
  }

  return counts;
}

// --- Estimate base stats ---

/**
 * スケジュール方式 (初レジェンド/NIA) で、ユーザが各レッスン週に指定した属性を
 * week→stat の Map で返す。turnChoices 未指定 (自動ピックモード) では null を返し、
 * 呼び出し側は従来の配分回数ベース近似にフォールバックする。
 */
export function lessonStatByWeek(
  turnChoices: TurnChoice[] | undefined,
): Map<number, string> | null {
  if (turnChoices == null) return null;
  const map = new Map<number, string>();
  for (const tc of turnChoices) {
    const a = tc.chosen_action as string;
    if (a === 'vo_lesson') map.set(tc.week, 'vo');
    else if (a === 'da_lesson') map.set(tc.week, 'da');
    else if (a === 'vi_lesson') map.set(tc.week, 'vi');
  }
  return map;
}

export function estimateBaseStats(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
  turnChoices?: TurnChoice[],
): StatusValues {
  let vo = 0,
    da = 0,
    vi = 0;

  // レッスンのSPパーフェクト基礎値を加算
  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => a.week - b.week);

  const choiceByWeek = lessonStatByWeek(turnChoices);
  if (choiceByWeek != null) {
    // スケジュール方式: ユーザが各週に指定した属性をそのまま使う。配分回数ベースの
    // 「多い属性を高値週へ」近似だと DaDaDaDaVi 指定が ViDaDaDaDa 扱いになり、
    // パラボ土台が実際の踏み順と食い違う。
    for (const w of lessonWeeks) {
      const stat = choiceByWeek.get(w.week);
      if (stat == null) continue;
      const lesson = getLesson(w, stat);
      if (lesson != null) {
        vo += lesson.sp_bonus.vo;
        da += lesson.sp_bonus.da;
        vi += lesson.sp_bonus.vi;
      }
    }
  } else {
    // 自動ピックモード: 各属性のレッスン回数分、後ろの週(高い値)から割り当て (近似)
    const weekQueue = [...lessonWeeks].sort((a, b) => b.week - a.week);
    const sortedAllocation = Object.entries(lessonAllocation).sort(
      (a, b) => b[1] - a[1],
    );
    let queueIndex = 0;
    for (const [statKey, count] of sortedAllocation) {
      for (let i = 0; i < count && queueIndex < weekQueue.length; i++) {
        const w = weekQueue[queueIndex++];
        const lesson = getLesson(w, statKey);
        if (lesson != null) {
          vo += lesson.sp_bonus.vo;
          da += lesson.sp_bonus.da;
          vi += lesson.sp_bonus.vi;
        }
      }
    }
  }

  // 授業の基礎値（メイン属性に全額配分と仮定）
  for (const week of plan.schedule) {
    if (week.classes.length > 0) {
      // 最大値の授業を加算
      const bestClass = [...week.classes].sort(
        (a, b) =>
          b.sp_bonus.vo +
          b.sp_bonus.da +
          b.sp_bonus.vi -
          (a.sp_bonus.vo + a.sp_bonus.da + a.sp_bonus.vi),
      )[0];
      vo += bestClass.sp_bonus.vo;
      da += bestClass.sp_bonus.da;
      vi += bestClass.sp_bonus.vi;
    }

    // 固定イベント
    if (isFixedEvent(week) && week.status_gain != null) {
      vo += week.status_gain.vo;
      da += week.status_gain.da;
      vi += week.status_gain.vi;
    }
  }

  return sv(vo, da, vi);
}

// --- Calculate lesson stat totals ---

export function calculateLessonStatTotals(
  plan: TrainingPlan,
  lessonAllocation: Record<string, number>,
  turnChoices?: TurnChoice[],
): StatusValues {
  let vo = 0,
    da = 0,
    vi = 0;

  const lessonWeeks = plan.schedule
    .filter((w) => w.lessons.length > 0)
    .sort((a, b) => b.week - a.week);

  const choiceByWeek = lessonStatByWeek(turnChoices);
  if (choiceByWeek != null) {
    // スケジュール方式: ユーザが各週に指定した属性をそのまま使う (踏み順を保持)。
    for (const w of lessonWeeks) {
      const stat = choiceByWeek.get(w.week);
      if (stat == null) continue;
      const lesson = getLesson(w, stat);
      if (lesson != null) {
        vo += lesson.sp_bonus.vo;
        da += lesson.sp_bonus.da;
        vi += lesson.sp_bonus.vi;
      }
    }
  } else {
    // 自動ピックモード: 配分回数ベースの近似 (多い属性を高値週へ)
    const weekQueue = [...lessonWeeks];
    const sortedAllocation = Object.entries(lessonAllocation).sort(
      (a, b) => b[1] - a[1],
    );
    let queueIndex = 0;
    for (const [statKey, count] of sortedAllocation) {
      for (let i = 0; i < count && queueIndex < weekQueue.length; i++) {
        const w = weekQueue[queueIndex++];
        const lesson = getLesson(w, statKey);
        if (lesson != null) {
          vo += lesson.sp_bonus.vo;
          da += lesson.sp_bonus.da;
          vi += lesson.sp_bonus.vi;
        }
      }
    }
  }

  // 試験/オーディション (基礎値+配分値 / 種別理論値) もパラボ対象になるので加算する。
  // HIF選抜試験は base+alloc、NIAオーディションは種別理論値が status_gain に反映済み
  // (buildNiaAuditionPlan)。実 calculate 側 (statusCalculation) はどちらにもパラボを適用するため、
  // スコアリング/内訳のパラボ土台でも両方を含める。
  for (const w of plan.schedule) {
    if (
      w.type === 'audition' &&
      (w.hif_exam_base != null ||
        w.hif_exam_distributed != null ||
        (w.nia_audition_tiers?.length ?? 0) > 0) &&
      w.status_gain != null
    ) {
      vo += w.status_gain.vo;
      da += w.status_gain.da;
      vi += w.status_gain.vi;
    }
  }

  return sv(vo, da, vi);
}

// --- Trigger count bonus consumer pool aggregation ---

/** trigger_count_bonus の単体スコアリング用、消費側カード1枚分の per-fire 寄与情報 */
export interface TriggerBonusContributor {
  cardId: string;
  cardName: string;
  perFire: StatusValues;
}

/** trigger_count_bonus の単体スコアリング用、対象トリガーごとの集計情報 */
export interface TriggerBonusEntry {
  /** 全消費側カードの per-fire ステータス合計 (推定スコア計算に使う) */
  total: StatusValues;
  /** 消費側カード一覧 (breakdown 表示用、寄与額の降順) */
  contributors: TriggerBonusContributor[];
}

/**
 * trigger_count_bonus 効果の単体スコアリングのため、対象トリガーごとに
 * プール内全ての消費側カードの per-fire ステータスを事前計算する。
 */
export function computeTriggerBonusInfo(
  pool: SupportCard[],
  uncapLevels: Record<string, number> | undefined,
): Record<string, TriggerBonusEntry> {
  // どの trigger_target を集計すべきか抽出
  const targets = new Set<string>();
  for (const card of pool) {
    for (const effect of card.effects) {
      if (effect.value_type === 'trigger_count_bonus' && effect.trigger_target) {
        targets.add(effect.trigger_target);
      }
    }
  }

  const result: Record<string, TriggerBonusEntry> = {};
  for (const target of targets) {
    const candidates: Array<TriggerBonusContributor & { total: number }> = [];
    for (const card of pool) {
      const uncap = getUncapLevel(card, uncapLevels);
      let cVo = 0, cDa = 0, cVi = 0;
      for (const effect of card.effects) {
        if (effect.trigger !== target || effect.value_type !== 'flat') continue;
        const v = Math.floor(getEffectValue(effect, uncap));
        switch (effect.stat) {
          case 'vo': cVo += v; break;
          case 'da': cDa += v; break;
          case 'vi': cVi += v; break;
          case 'all':
            cVo += v; cDa += v; cVi += v;
            break;
        }
      }
      const total = cVo + cDa + cVi;
      if (total > 0) {
        candidates.push({
          cardId: card.id,
          cardName: card.name,
          perFire: { vo: cVo, da: cDa, vi: cVi },
          total,
        });
      }
    }
    candidates.sort((a, b) => b.total - a.total);
    result[target] = {
      total: {
        vo: candidates.reduce((s, c) => s + c.perFire.vo, 0),
        da: candidates.reduce((s, c) => s + c.perFire.da, 0),
        vi: candidates.reduce((s, c) => s + c.perFire.vi, 0),
      },
      contributors: candidates.map(({ cardId, cardName, perFire }) => ({ cardId, cardName, perFire })),
    };
  }
  return result;
}

// --- Calculate card contribution ---

export function calculateCardContribution(
  card: SupportCard,
  triggerCounts: Record<string, number>,
  _lessonAllocation: Record<string, number>,
  lessonStatTotals: StatusValues,
  uncapLevels?: Record<string, number>,
  triggerBonusInfo?: Record<string, TriggerBonusEntry>,
  /**
   * デッキ確定後の再計算で、producer 側の trigger_count_bonus を raw_* に加算しないためのフラグ。
   * デッキ確定後は consumer 側の flat 効果が adjustedCounts 経由で実発火回数分加算されるため、
   * producer 側でも加算すると二重カウントになる。
   */
  skipTriggerBonusSelfContribution: boolean = false,
): CardScore {
  const uncap = getUncapLevel(card, uncapLevels);
  let vo = 0,
    da = 0,
    vi = 0;
  let teamBonusTotal = 0;
  const teamBonusContributors: TeamBonusContributor[] = [];
  const breakdowns: EffectBreakdown[] = [];

  for (const effect of card.effects) {
    // SP率は突破確率であり理論値計算では不要（全SPクリア前提）
    if (effect.value_type === 'sp_rate') continue;

    // trigger_count_bonus: 自カードは追加でステータスを得ないが、他カードのトリガー発火回数を増やす
    if (effect.value_type === 'trigger_count_bonus') {
      const target = effect.trigger_target;
      if (!target) continue;
      const perScale = getEffectValue(effect, uncap);
      const scaleCount = effect.scales_with
        ? (triggerCounts[effect.scales_with] ?? 0)
        : 1;
      let bonusFires = perScale * scaleCount;
      if (effect.max_count != null) bonusFires = Math.min(bonusFires, effect.max_count);
      bonusFires = Math.floor(bonusFires);
      if (bonusFires <= 0) continue;

      const entry = triggerBonusInfo?.[target];
      if (entry == null) continue;

      // 自カードを除外して消費側カードを集計 (producer 自身が consumer 効果を持つ場合の二重カウント防止)
      let synergyVoSum = 0, synergyDaSum = 0, synergyViSum = 0;
      const contribRows: EffectBreakdown[] = [];
      for (const c of entry.contributors) {
        if (c.cardId === card.id) continue;
        const cVo = c.perFire.vo * bonusFires;
        const cDa = c.perFire.da * bonusFires;
        const cVi = c.perFire.vi * bonusFires;
        const cTotal = cVo + cDa + cVi;
        if (cTotal <= 0) continue;
        synergyVoSum += cVo;
        synergyDaSum += cDa;
        synergyViSum += cVi;
        const perFireDesc = [
          c.perFire.vo > 0 ? `Vo+${c.perFire.vo}` : '',
          c.perFire.da > 0 ? `Da+${c.perFire.da}` : '',
          c.perFire.vi > 0 ? `Vi+${c.perFire.vi}` : '',
        ].filter(Boolean).join('/');
        const mainStat: 'vo' | 'da' | 'vi' =
          cVo >= cDa && cVo >= cVi ? 'vo' : cDa >= cVi ? 'da' : 'vi';
        contribRows.push({
          reason: `  ↳ ${c.cardName} (${perFireDesc}/回)`,
          stat: mainStat,
          value: Math.round(cTotal * 10) / 10,
        });
        teamBonusContributors.push({
          card_name: c.cardName,
          value: Math.floor(cTotal),
        });
      }
      if (contribRows.length === 0) continue;

      teamBonusTotal += synergyVoSum + synergyDaSum + synergyViSum;
      if (!skipTriggerBonusSelfContribution) {
        // 単体スコアリング時: 「想定される消費側カードへの寄与」を自スコアに加算 (ヒューリスティック)
        vo += synergyVoSum;
        da += synergyDaSum;
        vi += synergyViSum;
      }

      // ヘッダ行 (式のみ、値カラムは空表示)
      const targetName = triggerDisplayName(target);
      const scalesWith = effect.scales_with;
      const formula = scalesWith
        ? `${triggerDisplayName(scalesWith)}×${scaleCount} × ${perScale}`
        : `×${perScale}`;
      const headerSuffix = skipTriggerBonusSelfContribution ? ' → 他カードへ寄与' : '';
      breakdowns.push({
        reason: `[アイテム] ${targetName}+${bonusFires}回 (${formula})${headerSuffix}`,
        stat: 'all',
        value: 0,
      });
      breakdowns.push(...contribRows);
      continue;
    }

    if (effect.value_type === 'para_bonus') {
      // パラボは該当属性のレッスン上昇値にのみ適用
      const pct = getEffectValue(effect, uncap) / 100.0;
      let bonus = 0;
      switch (effect.stat) {
        case 'vo':
          bonus = lessonStatTotals.vo * pct;
          vo += bonus;
          break;
        case 'da':
          bonus = lessonStatTotals.da * pct;
          da += bonus;
          break;
        case 'vi':
          bonus = lessonStatTotals.vi * pct;
          vi += bonus;
          break;
        case 'all': {
          const bVo = lessonStatTotals.vo * pct;
          const bDa = lessonStatTotals.da * pct;
          const bVi = lessonStatTotals.vi * pct;
          vo += bVo;
          da += bDa;
          vi += bVi;
          bonus = bVo + bDa + bVi;
          break;
        }
      }

      if (Math.abs(bonus) < 0.01) continue;

      const reason = `パラボ(${effect.stat.toUpperCase()})+${getEffectValue(effect, uncap)}%`;
      breakdowns.push({
        reason,
        stat: effect.stat,
        value: Math.round(bonus * 10) / 10,
      });
      continue;
    }

    const value =
      effect.value_type === 'flat'
        ? calculateFlatValue(effect, triggerCounts, uncap, card)
        : 0;

    if (Math.abs(value) < 0.01) continue;

    // 内訳の理由テキスト生成
    const reason2 = buildReasonText(effect, triggerCounts, uncap, card);

    switch (effect.stat) {
      case 'vo':
        vo += value;
        break;
      case 'da':
        da += value;
        break;
      case 'vi':
        vi += value;
        break;
      case 'all':
        vo += value / 3.0;
        da += value / 3.0;
        vi += value / 3.0;
        break;
      default:
        vo += value / 3.0;
        da += value / 3.0;
        vi += value / 3.0;
        break;
    }

    breakdowns.push({
      reason: reason2,
      stat: effect.stat,
      value: Math.round(value * 10) / 10,
    });
  }

  const iVo = Math.floor(vo);
  const iDa = Math.floor(da);
  const iVi = Math.floor(vi);

  return {
    card,
    raw_vo: iVo,
    raw_da: iDa,
    raw_vi: iVi,
    team_bonus_total: Math.floor(teamBonusTotal),
    team_bonus_contributors: teamBonusContributors,
    total_value: iVo + iDa + iVi,
    breakdowns,
    is_rental: false,
    is_required: false,
    uncap_level: uncap,
  };
}

