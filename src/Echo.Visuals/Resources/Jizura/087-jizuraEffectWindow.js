/* Keep the renderer's effect semantics while avoiding a full-song scan per frame. */
const createJizuraEffectWindow = plan => {
  const events = plan.events;
  const span = events.reduce((max, event) => Math.max(max, Number.isFinite(event.dur) ? event.dur : 0), 14 / 24) + 0.1;
  const view = { ...plan, events: [] };
  let previousStart = -1, previousEnd = -1;
  const upperBound = time => {
    let lo = 0, hi = events.length;
    while (lo < hi) { const mid = (lo + hi) >>> 1; if (events[mid].t <= time) lo = mid + 1; else hi = mid; }
    return lo;
  };
  return time => {
    const start = upperBound(time - Math.max(span, 1 / plan.fps + 0.1));
    const end = upperBound(time);
    if (start !== previousStart || end !== previousEnd) {
      view.events = events.slice(start, end); previousStart = start; previousEnd = end;
    }
    view.fps = plan.fps;
    return view;
  };
};
