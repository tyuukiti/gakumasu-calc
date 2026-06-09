import type {
  SupportCard,
  CardEffect,
  TrainingPlan,
  WeekSchedule,
  StatusValues,
} from '../../src/types/models';

/**
 * 合成フィクスチャ生成ユーティリティ。
 * 総当たりオラクルで「真の最適」を計算できるよう、挙動が単純で決定的なカード/プランを作る。
 */

let _seq = 0;
/** テスト内で一意な連番 ID を返す (決定性のため Math.random は使わない)。 */
export function nextId(prefix = 'C'): string {
  _seq += 1;
  return `${prefix}${String(_seq).padStart(4, '0')}`;
}

/** _seq をリセット (テスト間の ID 安定化が必要な場合に使用)。 */
export function resetIds(): void {
  _seq = 0;
}

export interface MakeCardOptions {
  id?: string;
  name?: string;
  type?: string; // 'vo' | 'da' | 'vi' | 'all' | 'as'
  rarity?: string;
  plan?: string;
  tag?: string;
  /** equip/flat の装備ボーナス (属性別)。calculate の getInitialBonus が読む。 */
  equip?: Partial<StatusValues>;
  /** equip/para_bonus の % (属性別 or all)。レッスン獲得に乗る。 */
  paraBonus?: Partial<StatusValues> & { all?: number };
  /** equip/sp_rate を付ける属性 (SP率カード判定用)。例: ['vo'] / ['all']。 */
  sp?: string[];
  /** 任意の追加 effects をそのまま付与。 */
  effects?: CardEffect[];
}

function flatEffect(stat: string, value: number): CardEffect {
  return {
    trigger: 'equip',
    stat,
    values: [value, value, value, value, value],
    value_type: 'flat',
  };
}

function paraEffect(stat: string, value: number): CardEffect {
  return {
    trigger: 'equip',
    stat,
    values: [value, value, value, value, value],
    value_type: 'para_bonus',
  };
}

/** 単純なサポートカードを生成。デフォルトは効果なしの type='vo' カード。 */
export function makeCard(opts: MakeCardOptions = {}): SupportCard {
  const effects: CardEffect[] = [];
  if (opts.equip) {
    for (const stat of ['vo', 'da', 'vi'] as const) {
      const v = opts.equip[stat];
      if (v != null && v !== 0) effects.push(flatEffect(stat, v));
    }
  }
  if (opts.paraBonus) {
    if (opts.paraBonus.all != null && opts.paraBonus.all !== 0) {
      effects.push(paraEffect('all', opts.paraBonus.all));
    }
    for (const stat of ['vo', 'da', 'vi'] as const) {
      const v = opts.paraBonus[stat];
      if (v != null && v !== 0) effects.push(paraEffect(stat, v));
    }
  }
  if (opts.sp) {
    for (const stat of opts.sp) {
      effects.push({ trigger: 'equip', stat, values: [10, 10, 10, 10, 10], value_type: 'sp_rate' });
    }
  }
  if (opts.effects) effects.push(...opts.effects);

  return {
    id: opts.id ?? nextId(),
    name: opts.name ?? opts.id ?? 'card',
    rarity: opts.rarity ?? 'ssr',
    type: opts.type ?? 'vo',
    plan: opts.plan ?? '',
    tag: opts.tag,
    effects,
  };
}

export interface MakePlanOptions {
  id?: string;
  statusLimit?: number;
  baseStatus?: Partial<StatusValues>;
  /** レッスン週を各属性いくつ作るか。週ごとに sp_bonus をその属性に付与。 */
  lessonWeeks?: { vo?: number; da?: number; vi?: number };
  /** 各レッスン週の sp_bonus (属性別の基礎獲得)。 */
  lessonGain?: number;
  schedule?: WeekSchedule[];
}

function sv(p?: Partial<StatusValues>): StatusValues {
  return { vo: p?.vo ?? 0, da: p?.da ?? 0, vi: p?.vi ?? 0 };
}

/**
 * 単純な育成プランを生成。
 * デフォルトは「レッスンなし・上限高め」で、カードの equip/flat 合計がそのまま結果に出る。
 * lessonWeeks を指定すると各属性のレッスン週を生成 (para_bonus 検証用)。
 */
export function makePlan(opts: MakePlanOptions = {}): TrainingPlan {
  let schedule: WeekSchedule[];
  if (opts.schedule) {
    schedule = opts.schedule;
  } else {
    schedule = [];
    let week = 1;
    const gain = opts.lessonGain ?? 100;
    for (const stat of ['vo', 'da', 'vi'] as const) {
      const n = opts.lessonWeeks?.[stat] ?? 0;
      for (let i = 0; i < n; i++) {
        schedule.push({
          week,
          type: 'normal',
          available_actions: [`${stat}_lesson`, 'vo_lesson', 'da_lesson', 'vi_lesson'],
          lessons: [{ type: stat, sp_bonus: sv({ [stat]: gain }) }],
          classes: [],
        });
        week += 1;
      }
    }
  }

  return {
    id: opts.id ?? 'synthetic',
    name: 'Synthetic Plan',
    description: 'test fixture',
    total_weeks: schedule.length,
    status_limit: opts.statusLimit ?? 9999,
    base_status: sv(opts.baseStatus),
    schedule,
  };
}
