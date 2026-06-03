import type { Character } from '../types/models';

/**
 * キャラ補正トグル（3凸・STEP4）を適用した一時 Character を返す。元データは変更しない。
 * - 3凸: para_bonus は3凸ON時の最大値。OFFなら uncap3_bonus 分を減算。
 * - STEP4: ON（デフォルト）なら step4_bonus の base_status_bonus / para_bonus を上乗せ加算。
 *   3凸とは独立に作用する。
 */
export function applyCharacterToggles(
  character: Character | null | undefined,
  uncap3Enabled: boolean,
  step4Enabled: boolean,
): Character | null {
  if (character == null) return null;

  let base = character.base_status_bonus;
  let para = character.para_bonus;

  if (!uncap3Enabled && character.uncap3_bonus) {
    para = {
      vo: para.vo - character.uncap3_bonus.vo,
      da: para.da - character.uncap3_bonus.da,
      vi: para.vi - character.uncap3_bonus.vi,
    };
  }

  if (step4Enabled && character.step4_bonus) {
    base = {
      vo: base.vo + character.step4_bonus.base_status_bonus.vo,
      da: base.da + character.step4_bonus.base_status_bonus.da,
      vi: base.vi + character.step4_bonus.base_status_bonus.vi,
    };
    para = {
      vo: para.vo + character.step4_bonus.para_bonus.vo,
      da: para.da + character.step4_bonus.para_bonus.da,
      vi: para.vi + character.step4_bonus.para_bonus.vi,
    };
  }

  return { ...character, base_status_bonus: base, para_bonus: para };
}
