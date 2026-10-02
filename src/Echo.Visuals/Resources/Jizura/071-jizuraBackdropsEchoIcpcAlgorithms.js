/* Original ICPC algorithm artwork. Small fixed traces live only in the disposable frame.
   No image assets, timers, extra canvas layers or growing caches. */
(() => {
  'use strict';
  const K = J.echo, { rgba, mix, clamp, TAU } = K;
  const MONO = 'ui-monospace,"SF Mono",Menlo,Consolas,monospace';
  const text = (ctx, value, x, y, size, colour, align = 'left', weight = 400) => {
    ctx.font = `${weight} ${size}px ${MONO}`; ctx.textAlign = align; ctx.fillStyle = colour; ctx.fillText(value, x, y);
  };
  const stroke = (ctx, x, y, xx, yy) => { ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(xx, yy); ctx.stroke(); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  const smooth = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const base = (env, ctx, title, subtitle, status) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, a: sc.accent, b: sc.accent2, fg: sc.fg, dim: rgba(sc.fg, dark ? 0.43 : 0.55),
      bg: mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.16), faint: rgba(sc.fg, 0.1) };
    ctx.globalAlpha = K.fade(env, 0.5); ctx.fillStyle = T.bg; ctx.fillRect(0, 0, W, H);
    text(ctx, title, W * 0.055, H * 0.09, U * 0.018, T.a, 'left', 700);
    text(ctx, subtitle, W * 0.055, H * 0.13, U * 0.014, T.dim);
    text(ctx, status, W * 0.945, H * 0.09, U * 0.016, T.b, 'right', 700);
    ctx.strokeStyle = T.faint; ctx.lineWidth = 1;
    stroke(ctx, W * 0.055, H * 0.17, W * 0.945, H * 0.17);
    return T;
  };

  const SEARCH = [], TARGET = 37;
  let lo = 0, hi = 64;
  while (lo < hi) {
    const mid = Math.floor((lo + hi) / 2);
    SEARCH.push({ lo, hi, mid, pass: mid >= TARGET });
    if (mid >= TARGET) hi = mid; else lo = mid + 1;
  }
  SEARCH.push({ lo, hi, mid: lo, pass: true });
  K.registerBg('echoBinarySearch', '二分探索', ['calm', 'graphic', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 7), step = Math.min(SEARCH.length - 1, Math.floor(t / 0.48));
    const s = SEARCH[step], done = step === SEARCH.length - 1;
    const T = base(env, ctx, 'B / BINARY SEARCH', 'false → true  /  find the first feasible answer', done ? 'ANSWER FOUND' : 'HALVE THE INTERVAL'), U = T.U;
    const portrait = H > W, cx = W * (portrait ? 0.5 : 0.18), cy = H * (portrait ? 0.79 : 0.77), rad = U * (portrait ? 0.13 : 0.15);
    // Each retained interval is an arc on the same answer dial.
    ctx.strokeStyle = T.faint; ctx.lineWidth = U * 0.003; ctx.beginPath(); ctx.arc(cx, cy, rad, 0, TAU); ctx.stroke();
    for (let i = 0; i <= 64; i++) {
      const a = i / 65 * TAU - Math.PI / 2, major = i % 8 === 0;
      ctx.strokeStyle = i >= s.lo && i <= s.hi ? rgba(T.a, 0.7) : T.faint;
      stroke(ctx, cx + Math.cos(a) * rad, cy + Math.sin(a) * rad,
        cx + Math.cos(a) * rad * (major ? 1.1 : 1.04), cy + Math.sin(a) * rad * (major ? 1.1 : 1.04));
    }
    for (let i = 0; i <= step; i++) {
      const q = SEARCH[i], rr = rad * (0.94 - i * 0.065);
      ctx.strokeStyle = rgba(i === step ? T.b : T.a, i === step ? 0.85 : 0.22); ctx.lineWidth = U * (i === step ? 0.006 : 0.002);
      ctx.beginPath(); ctx.arc(cx, cy, rr, q.lo / 65 * TAU - Math.PI / 2, (q.hi + 1) / 65 * TAU - Math.PI / 2); ctx.stroke();
    }
    text(ctx, String(s.mid).padStart(2, '0'), cx, cy + U * 0.022, U * 0.08, T.fg, 'center', 700);
    text(ctx, done ? 'lower_bound' : 'mid', cx, cy + U * 0.055, U * 0.014, T.dim, 'center');
    // History collapses toward the same absolute coordinate; no fake random values.
    const x0 = W * (portrait ? 0.08 : 0.42), x1 = W * 0.92, y0 = H * (portrait ? 0.94 : 0.66);
    if (!portrait) for (let i = 0; i <= step; i++) {
      const q = SEARCH[i], y = y0 + i * H * 0.038, a = x0 + q.lo / 64 * (x1 - x0), b = x0 + q.hi / 64 * (x1 - x0);
      ctx.strokeStyle = T.faint; ctx.lineWidth = 1; stroke(ctx, x0, y, x1, y);
      ctx.strokeStyle = rgba(i === step ? T.b : T.a, i === step ? 0.9 : 0.3); ctx.lineWidth = U * 0.004;
      stroke(ctx, a, y, Math.max(a + 2, b), y); ctx.fillStyle = i === step ? T.b : T.a;
      dot(ctx, x0 + q.mid / 64 * (x1 - x0), y, U * 0.004);
    }
    text(ctx, `[${s.lo}, ${s.hi}]`, W * 0.945, H * 0.27, U * 0.054, rgba(T.a, 0.6), 'right', 700);
    text(ctx, `check(${s.mid}) = ${s.pass ? 'true' : 'false'}`, W * 0.945, H * 0.32, U * 0.017, T.dim, 'right');
    text(ctx, `${step + 1} / ${SEARCH.length} STEPS`, x1, H * 0.955, U * 0.013, T.dim, 'right');
  });

  const SOURCE = 'ABABACABABACABA', PATTERN = 'ABABACA', PI = new Array(PATTERN.length).fill(0);
  for (let i = 1, j = 0; i < PATTERN.length; i++) {
    while (j && PATTERN[i] !== PATTERN[j]) j = PI[j - 1];
    if (PATTERN[i] === PATTERN[j]) j++;
    PI[i] = j;
  }
  const MATCH = [];
  for (let i = 0, j = 0; i < SOURCE.length; i++) {
    while (j && SOURCE[i] !== PATTERN[j]) {
      MATCH.push({ i, j, offset: i - j, kind: 'fallback' }); j = PI[j - 1];
    }
    MATCH.push({ i, j, offset: i - j, kind: SOURCE[i] === PATTERN[j] ? 'compare' : 'skip' });
    if (SOURCE[i] === PATTERN[j]) j++;
    if (j === PATTERN.length) { MATCH.push({ i, j, offset: i - j + 1, kind: 'found' }); j = PI[j - 1]; }
  }
  K.registerBg('echoPrefixMatch', '文字列マッチ', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 7), idx = Math.min(MATCH.length - 1, Math.floor(t / 0.19));
    const found = MATCH.slice(0, idx + 1).filter(e => e.kind === 'found');
    const s = idx === MATCH.length - 1 && found.length ? found[found.length - 1] : MATCH[idx];
    const T = base(env, ctx, 'S / PREFIX MATCH', 'KMP  /  keep the prefix, skip the repetition', s.kind === 'fallback' ? 'FOLLOW FAILURE LINK' : found.length ? 'MATCH FOUND' : 'COMPARE CHARACTERS'), U = T.U;
    const cell = W * 0.058, x0 = W * 0.065, y0 = H * 0.72, y1 = H * 0.83;
    // Two open type rails. The pattern slides by whole characters as links are followed.
    ctx.strokeStyle = T.faint; ctx.lineWidth = 1;
    stroke(ctx, x0, y0 + U * 0.035, W * 0.935, y0 + U * 0.035);
    stroke(ctx, x0, y1 + U * 0.035, W * 0.935, y1 + U * 0.035);
    const shift = s.offset;
    for (let i = 0; i < SOURCE.length; i++) {
      const x = x0 + (i + 0.5) * cell, hit = found.some(e => i >= e.offset && i < e.offset + PATTERN.length);
      text(ctx, SOURCE[i], x, y0, U * 0.032, hit ? T.a : i === s.i ? T.b : T.dim, 'center', 700);
      text(ctx, String(i), x, y0 + U * 0.022, U * 0.011, T.dim, 'center');
    }
    for (let j = 0; j < PATTERN.length && shift + j < SOURCE.length; j++) {
      const x = x0 + (shift + j + 0.5) * cell;
      text(ctx, PATTERN[j], x, y1, U * 0.032, j < s.j ? T.a : j === s.j ? T.b : T.dim, 'center', 700);
      if (j < s.j) { ctx.strokeStyle = rgba(T.a, 0.2); stroke(ctx, x, y0 + U * 0.05, x, y1 - U * 0.045); }
    }
    const px = x0 + (shift + Math.min(s.j, PATTERN.length - 1, SOURCE.length - shift - 1) + 0.5) * cell;
    ctx.fillStyle = T.b; ctx.beginPath(); ctx.moveTo(px, y1 - U * 0.058); ctx.lineTo(px - U * 0.007, y1 - U * 0.071); ctx.lineTo(px + U * 0.007, y1 - U * 0.071); ctx.fill();
    if (s.kind === 'fallback') {
      const to = x0 + (shift + PI[s.j - 1] + 0.5) * cell;
      ctx.strokeStyle = rgba(T.b, 0.7); ctx.lineWidth = U * 0.002; ctx.beginPath(); ctx.moveTo(px, y1 + U * 0.05);
      ctx.bezierCurveTo(px, y1 + U * 0.12, to, y1 + U * 0.12, to, y1 + U * 0.05); ctx.stroke();
    }
    text(ctx, 'π', W * 0.055, H * 0.255, U * 0.04, T.b);
    for (let j = 0; j < PI.length; j++) {
      const x = W * 0.12 + j * U * 0.045;
      text(ctx, PATTERN[j], x, H * 0.23, U * 0.018, T.dim, 'center');
      text(ctx, String(PI[j]), x, H * 0.27, U * 0.022, T.a, 'center', 700);
    }
    text(ctx, `${found.length} MATCH${found.length === 1 ? '' : 'ES'}`, W * 0.945, H * 0.95, U * 0.018, T.a, 'right', 700);
  });

  // Eight-point radix-2 DIT FFT. The first column is bit-reversed input.
  const FFT = [Array.from({ length: 8 }, (_, i) => { const k = ((i & 1) << 2) | (i & 2) | ((i & 4) >> 2); return [Math.sin(TAU * k / 8) + 0.5 * Math.cos(TAU * k / 4), 0]; })];
  for (let size = 2; size <= 8; size *= 2) {
    const prev = FFT[FFT.length - 1], next = prev.map(p => p.slice()), half = size / 2;
    for (let start = 0; start < 8; start += size) for (let j = 0; j < half; j++) {
      const a = prev[start + j], b = prev[start + j + half], angle = -TAU * j / size;
      const re = b[0] * Math.cos(angle) - b[1] * Math.sin(angle), im = b[0] * Math.sin(angle) + b[1] * Math.cos(angle);
      next[start + j] = [a[0] + re, a[1] + im]; next[start + j + half] = [a[0] - re, a[1] - im];
    }
    FFT.push(next);
  }
  K.registerBg('echoFftButterfly', 'FFTバタフライ', ['graphic', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 8), stage = Math.min(3, Math.floor(t / 0.85)), phase = smooth(t / 0.85 - stage);
    const T = base(env, ctx, 'F / BUTTERFLY NETWORK', '8-point FFT  /  time domain → frequency domain', stage === 3 ? 'SPECTRUM RESOLVED' : `RADIX 2 / STAGE ${stage + 1}`), U = T.U;
    const x0 = W * 0.11, dx = W * 0.25, y0 = H * 0.66, dy = H * 0.036;
    for (let col = 0; col < 3; col++) {
      const half = 1 << col, size = half * 2;
      for (let i = 0; i < 8; i++) for (const cross of [false, true]) {
        const j = cross ? i ^ half : i, ax = x0 + col * dx, bx = ax + dx, ay = y0 + i * dy, by = y0 + j * dy;
        const colour = cross ? T.b : T.a, active = col === stage;
        ctx.strokeStyle = rgba(colour, col < stage ? 0.5 : 0.12); ctx.lineWidth = U * 0.0015;
        stroke(ctx, ax, ay, bx, by);
        if (active) { ctx.strokeStyle = rgba(colour, 0.8); ctx.lineWidth = U * 0.0025; stroke(ctx, ax, ay, ax + dx * phase, ay + (by - ay) * phase); }
      }
      text(ctx, `m = ${size}`, x0 + (col + 0.5) * dx, H * 0.975, U * 0.014, T.dim, 'center');
    }
    for (let col = 0; col < 4; col++) for (let i = 0; i < 8; i++) {
      const x = x0 + col * dx, y = y0 + i * dy, ready = col <= stage;
      ctx.fillStyle = T.bg; dot(ctx, x, y, U * 0.01); ctx.fillStyle = ready ? T.a : T.dim; dot(ctx, x, y, U * 0.004);
      if (col === 0) text(ctx, `x${((i & 1) << 2) | (i & 2) | ((i & 4) >> 2)}`, x - U * 0.025, y + U * 0.004, U * 0.012, T.dim, 'right');
      if (col === 3) {
        const value = FFT[3][i], magnitude = Math.hypot(value[0], value[1]);
        ctx.fillStyle = rgba(T.b, ready ? 0.8 : 0.12); ctx.fillRect(x + W * 0.02, y - U * 0.004, W * 0.04 * magnitude / 4, U * 0.008);
      }
    }
    const cx = W * 0.12, cy = H * 0.28, radius = U * 0.073;
    ctx.strokeStyle = rgba(T.a, 0.25); ctx.lineWidth = 1; ctx.beginPath(); ctx.arc(cx, cy, radius, 0, TAU); ctx.stroke();
    for (let i = 0; i < 8; i++) {
      const angle = -TAU * i / 8, x = cx + Math.cos(angle) * radius, y = cy + Math.sin(angle) * radius;
      ctx.fillStyle = i === Math.floor(t * 2) % 8 ? T.b : T.a; dot(ctx, x, y, U * 0.0035);
    }
    text(ctx, 'ω', cx, cy + U * 0.012, U * 0.04, T.dim, 'center');
    text(ctx, 'e⁻²πⁱᵏ/ⁿ', W * 0.945, H * 0.29, U * 0.04, rgba(T.b, 0.5), 'right');
    text(ctx, 'O(n log n)', W * 0.945, H * 0.335, U * 0.016, T.dim, 'right');
  });
})();
