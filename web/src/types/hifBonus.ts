/**
 * HIFモード固有のボーナス (パネル方式の永続強化)。
 * 各パネルのレベルをユーザが設定でき、デフォルトはMAX。
 * 効果値はパネル定義表 (基本情報/ボーナス一覧) に準拠。
 */

export interface HifBonusLevels {
  /** ボーカル上昇 (Lv1-5): flat +20×Lv, paraBonus +2%×Lv */
  voUpLevel: number;
  /** ダンス上昇 (Lv1-5) */
  daUpLevel: number;
  /** ビジュアル上昇 (Lv1-5) */
  viUpLevel: number;
  /** SPレッスン発生率増加 (Lv1-5): +1%×Lv (計算非関与) */
  spRateLevel: number;
  /** 試験前体力回復% (Lv1-6) (計算非関与) */
  hpRecoveryLevel: number;
  /** 【本戦】パラメータ上限増加 (Lv1-6): +50/+80/+110/+140/+170/+200 */
  finalStatLimitLevel: number;
  /** 【選抜試験】初期Pポイント (Lv1-6) (計算非関与) */
  preExamPpLevel: number;
  /** 【本戦】初期Pポイント (Lv1-6) (計算非関与) */
  finalPpLevel: number;
  /** 相談スキルカード割引 (Lv1-6) (計算非関与) */
  consultationDiscountLevel: number;
}

/** 各パネルの最大Lv */
export const HIF_BONUS_MAX_LEVELS = {
  voUpLevel: 5, daUpLevel: 5, viUpLevel: 5,
  spRateLevel: 5,
  hpRecoveryLevel: 6,
  finalStatLimitLevel: 6,
  preExamPpLevel: 6,
  finalPpLevel: 6,
  consultationDiscountLevel: 6,
} as const;

/** 全パネル最大Lvのデフォルト設定 */
export function defaultHifBonusLevels(): HifBonusLevels {
  return {
    voUpLevel: 5, daUpLevel: 5, viUpLevel: 5,
    spRateLevel: 5,
    hpRecoveryLevel: 6,
    finalStatLimitLevel: 6,
    preExamPpLevel: 6,
    finalPpLevel: 6,
    consultationDiscountLevel: 6,
  };
}

// --- 効果テーブル (index = level, level 0 は効果なし) ---

/** Vo/Da/Vi 上昇 flat 値 */
export const HIF_STAT_UP_FLAT = [0, 20, 40, 60, 80, 100];
/** Vo/Da/Vi 上昇 paraBonus % */
export const HIF_STAT_UP_PARA = [0, 2, 4, 6, 8, 10];
/** 【本戦】パラメータ上限増加 */
export const HIF_FINAL_CAP_BONUS = [0, 50, 80, 110, 140, 170, 200];
/** SPレッスン発生率 % */
export const HIF_SP_RATE_INCREASE = [0, 1, 2, 3, 4, 5];
/** 試験前体力回復 % */
export const HIF_HP_RECOVERY = [0, 5, 7, 9, 11, 13, 15];
/** 初期Pポイント */
export const HIF_PP_INCREASE = [0, 50, 80, 110, 140, 170, 200];
/** 相談スキルカード割引 % */
export const HIF_CONSULTATION_DISCOUNT = [0, 5, 10, 15, 20, 25, 30];

/** 指定したレベルでの Vo flat ボーナス値を取得 */
export function getVoFlatBonus(lv: number): number {
  return HIF_STAT_UP_FLAT[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getDaFlatBonus(lv: number): number {
  return HIF_STAT_UP_FLAT[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getViFlatBonus(lv: number): number {
  return HIF_STAT_UP_FLAT[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getVoParaBonus(lv: number): number {
  return HIF_STAT_UP_PARA[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getDaParaBonus(lv: number): number {
  return HIF_STAT_UP_PARA[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getViParaBonus(lv: number): number {
  return HIF_STAT_UP_PARA[Math.max(0, Math.min(lv, 5))] ?? 0;
}
export function getFinalCapBonus(lv: number): number {
  return HIF_FINAL_CAP_BONUS[Math.max(0, Math.min(lv, 6))] ?? 0;
}
