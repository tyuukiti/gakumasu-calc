import type { StatusValues } from '../types/models';

export function sv(vo: number, da: number, vi: number): StatusValues {
  return { vo, da, vi };
}

export const ZERO: StatusValues = { vo: 0, da: 0, vi: 0 };

export function svZero(): StatusValues {
  return { vo: 0, da: 0, vi: 0 };
}

export function svAdd(a: StatusValues, b: StatusValues): StatusValues {
  return { vo: a.vo + b.vo, da: a.da + b.da, vi: a.vi + b.vi };
}

export function svMultiplyAndFloor(s: StatusValues, factor: number): StatusValues {
  return {
    vo: Math.floor(s.vo * factor),
    da: Math.floor(s.da * factor),
    vi: Math.floor(s.vi * factor),
  };
}

export function svClone(s: StatusValues): StatusValues {
  return { vo: s.vo, da: s.da, vi: s.vi };
}

export function svTotal(s: StatusValues): number {
  return s.vo + s.da + s.vi;
}
