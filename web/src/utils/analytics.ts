declare global {
  interface Window {
    gtag?: (...args: unknown[]) => void;
  }
}

// ─── 基本イベント送信 ───

export function trackEvent(eventName: string, params?: Record<string, unknown>): void {
  window.gtag?.('event', eventName, params);
}

// ─── ユーザープロパティ ───

export function setUserProperties(properties: Record<string, unknown>): void {
  window.gtag?.('set', 'user_properties', properties);
}

// ─── 計測タイマー ───

const timers = new Map<string, number>();

export function startTimer(name: string): void {
  timers.set(name, performance.now());
}

export function endTimer(name: string): number | null {
  const start = timers.get(name);
  if (start == null) return null;
  timers.delete(name);
  return Math.round(performance.now() - start);
}

// ─── セッション内カウンター ───

const counters = new Map<string, number>();

export function incrementCounter(name: string): number {
  const current = counters.get(name) ?? 0;
  const next = current + 1;
  counters.set(name, next);
  return next;
}

export function getCounter(name: string): number {
  return counters.get(name) ?? 0;
}

// ─── ファネルステップ ───

const completedSteps = new Set<string>();

export function trackFunnelStep(
  funnelName: string,
  stepNumber: number,
  stepName: string,
): void {
  const key = `${funnelName}_${stepNumber}`;
  const isFirstTime = !completedSteps.has(key);
  completedSteps.add(key);

  trackEvent('funnel_step', {
    funnel_name: funnelName,
    step_number: stepNumber,
    step_name: stepName,
    is_first_time: isFirstTime,
  });
}
