/* Background-only rendering for lyric gaps and the outro. Never extends lyric cuts.
   Only references to the current plan's background cuts, released with it; no image cache. */
const createJizuraBackdropContinuity = plan => {
  const cuts = plan.cuts.filter(cut => cut.bg && cut.bg !== 'none' && J.BG[cut.bg]);
  const cutAt = time => {
    if (!cuts.length) return null;
    let lo = 0, hi = cuts.length;
    while (lo < hi) { const mid = (lo + hi) >>> 1; if (cuts[mid].start <= time) lo = mid + 1; else hi = mid; }
    return cuts[Math.max(0, lo - 1)];
  };
  const draw = (renderer, ctx, time, options) => {
    const step = J.stepDur(plan.fx, plan.fps), quantized = Math.floor(time / step + 1e-6) * step;
    if (J.cutAt(plan, quantized)) return false;
    const cut = cutAt(quantized), entry = cut && J.BG[cut.bg];
    if (!entry || typeof entry.draw !== 'function') return false;
    const sc = plan.style.schemes[cut.scheme % plan.style.schemes.length] || plan.style.schemes[0];
    const t = options.motion === false ? Math.max(cut.start + 1, cut.end) : Math.max(quantized, cut.start + 1);
    const env = renderer.makeEnv(ctx, plan, cut, sc, {
      pass: 'main', t, lt: t - cut.start, ltb: t - cut.start,
      step: Math.floor(t * 24), scale: options.scale, allowFilter: renderer.filterOK && !options.fast,
      energy: null, beat: null, bgOnly: true,
    });
    env.pIn = 1; env.pOut = 0;
    ctx.save();
    try {
      ctx.setTransform(options.scale, 0, 0, options.scale, 0, 0);
      ctx.globalAlpha = 1; ctx.globalCompositeOperation = 'source-over'; ctx.filter = 'none';
      ctx.fillStyle = sc.bg; ctx.fillRect(0, 0, plan.W, plan.H);
      entry.draw(env, cut.bgP || {});
      // Only first-party front scenes: lyric-bound vendor decorations need a text bbox.
      for (const item of cut.decor || []) {
        const front = J.DECOR[item.id];
        if (front?.pack === 'echo' && front.layer === 'front' && typeof front.draw === 'function') front.draw(env, null, item);
      }
      return true;
    } catch { return false; }
    finally { ctx.restore(); }
  };
  return { cutAt, draw };
};
