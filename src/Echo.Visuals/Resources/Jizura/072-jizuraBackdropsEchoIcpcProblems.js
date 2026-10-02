/* ECHO-authored contest statements and illustrations of three classic linear algorithms.
   Only tiny fixed sample traces are retained in the disposable iframe. No assets or caches. */
(() => {
  'use strict';
  const K = J.echo, { rgba, mix, clamp, TAU } = K;
  const MONO = 'ui-monospace,"SF Mono",Menlo,Consolas,"Microsoft YaHei UI",monospace';
  const text = (ctx, value, x, y, size, colour, align = 'left', weight = 400) => {
    ctx.font = `${weight} ${size}px ${MONO}`; ctx.textAlign = align; ctx.fillStyle = colour; ctx.fillText(value, x, y);
  };
  const line = (ctx, ax, ay, bx, by) => { ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  const wrap = (ctx, value, x, y, width, lh) => {
    let row = '';
    for (const word of value.split(/\s+/)) {
      const next = row ? `${row} ${word}` : word;
      if (row && ctx.measureText(next).width > width) { ctx.fillText(row, x, y); row = word; y += lh; }
      else row = next;
    }
    if (row) ctx.fillText(row, x, y);
    return y + lh;
  };
  const sheet = (env, ctx, problem, status) => {
    const { W, H, sc } = env, U = K.U(env), portrait = H > W, dark = K.isDark(sc.bg);
    const T = { U, a: sc.accent, b: sc.accent2, fg: sc.fg, dim: rgba(sc.fg, dark ? 0.58 : 0.65),
      faint: rgba(sc.fg, 0.12), bg: mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.18) };
    ctx.globalAlpha = K.fade(env, 0.45); ctx.fillStyle = T.bg; ctx.fillRect(0, 0, W, H);
    text(ctx, problem.title, W * 0.055, H * 0.08, U * 0.028, T.fg, 'left', 700);
    text(ctx, status, W * 0.945, H * 0.08, U * 0.015, T.a, 'right', 700);
    text(ctx, 'ECHO CONTEST / 1 s / 64 MiB', W * 0.055, H * 0.115, U * 0.012, T.dim);
    ctx.font = `${U * 0.019}px ${MONO}`; ctx.fillStyle = T.dim; ctx.textAlign = 'left';
    let y = wrap(ctx, problem.statement, W * 0.055, H * 0.17, W * (portrait ? 0.89 : 0.56), U * 0.029);
    y = wrap(ctx, problem.format, W * 0.055, y, W * (portrait ? 0.89 : 0.56), U * 0.029);
    text(ctx, problem.bounds, W * 0.055, y, U * 0.014, T.dim);
    const sx = W * (portrait ? 0.055 : 0.69), sy = portrait ? Math.max(H * 0.275, y + U * 0.035) : H * 0.165;
    text(ctx, 'SAMPLE INPUT', sx, sy, U * 0.012, T.b, 'left', 700);
    for (let i = 0; i < problem.input.length; i++) text(ctx, problem.input[i], sx, sy + U * (0.026 + i * 0.025), U * 0.017, T.fg);
    text(ctx, `OUTPUT  ${problem.answer}`, portrait ? W * 0.945 : sx, sy + U * 0.084, U * 0.022, T.a, portrait ? 'right' : 'left', 700);
    return T;
  };

  const LAMPS = [2, 7, 1, 8, 2, 8, 1, 8], WIDTH = 3, RING_TRACE = [];
  let ringSum = LAMPS.slice(0, WIDTH).reduce((a, b) => a + b, 0), ringBest = -1, ringStart = 0;
  for (let start = 0; start < LAMPS.length; start++) {
    if (start) ringSum += LAMPS[(start + WIDTH - 1) % LAMPS.length] - LAMPS[start - 1];
    if (ringSum > ringBest) { ringBest = ringSum; ringStart = start; }
    RING_TRACE.push({ start, sum: ringSum, best: ringBest, bestStart: ringStart });
  }
  const RING = { title: 'A. Ring of Lights', statement: 'There are n lights on a ring, with brightness a[i]. Choose exactly k consecutive lights with the largest total brightness. Wrapping is allowed.',
    format: 'Input: n, k and array a. Output: the maximum sum.', bounds: '1 ≤ k ≤ n ≤ 200000; 0 ≤ a[i] ≤ 10⁹', input: ['8 3', '2 7 1 8 2 8 1 8'], answer: ringBest };
  K.registerBg('echoRingLamps', 'Ring of Lights', ['calm', 'graphic', 'emotional'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 8), step = Math.min(7, Math.floor(t / 0.36)), done = t >= 2.88;
    const s = RING_TRACE[step], active = done ? s.bestStart : s.start;
    const T = sheet(env, ctx, RING, done ? 'AC / O(n)' : 'SLIDING WINDOW'), U = T.U, portrait = H > W;
    const cx = W * (portrait ? 0.27 : 0.22), cy = H * 0.78, radius = U * (portrait ? 0.115 : 0.135);
    ctx.strokeStyle = T.faint; ctx.lineWidth = U * 0.004; ctx.beginPath(); ctx.arc(cx, cy, radius, 0, TAU); ctx.stroke();
    ctx.strokeStyle = rgba(T.a, 0.65); ctx.lineWidth = U * 0.014; ctx.beginPath();
    ctx.arc(cx, cy, radius, active / 8 * TAU - Math.PI / 2, (active + WIDTH - 1) / 8 * TAU - Math.PI / 2); ctx.stroke();
    for (let i = 0; i < LAMPS.length; i++) {
      const angle = i / 8 * TAU - Math.PI / 2, x = cx + Math.cos(angle) * radius, y = cy + Math.sin(angle) * radius;
      const on = (i - active + 8) % 8 < WIDTH;
      ctx.fillStyle = T.bg; dot(ctx, x, y, U * 0.018); ctx.fillStyle = on ? T.b : T.dim; dot(ctx, x, y, U * 0.008);
      text(ctx, String(LAMPS[i]), cx + Math.cos(angle) * radius * 1.3, cy + Math.sin(angle) * radius * 1.3 + U * 0.007, U * 0.022, on ? T.fg : T.dim, 'center', 700);
    }
    text(ctx, String(done ? s.best : s.sum), cx, cy + U * 0.02, U * 0.067, T.fg, 'center', 700);
    text(ctx, done ? 'maximum' : 'window sum', cx, cy + U * 0.05, U * 0.014, T.dim, 'center');
    const x0 = W * (portrait ? 0.53 : 0.49), bw = W * (portrait ? 0.045 : 0.045), base = H * 0.9;
    for (let i = 0; i <= step; i++) {
      const trace = RING_TRACE[i], height = H * 0.22 * trace.sum / ringBest;
      ctx.fillStyle = rgba(i === s.bestStart ? T.b : T.a, i === s.bestStart ? 0.65 : 0.18);
      ctx.fillRect(x0 + i * bw, base - height, bw * 0.68, height);
      text(ctx, String(i + 1), x0 + (i + 0.34) * bw, base + U * 0.024, U * 0.012, T.dim, 'center');
    }
    text(ctx, 'WINDOW SUM BY START INDEX', x0, H * 0.955, U * 0.014, T.dim);
  });

  const PARCELS = [3, 1, 2, 6, 1, 2, 1, 4], CAPACITY = 10, PARCEL_TRACE = [];
  let left = 0, sum = 0, best = 0, bestLeft = 0, bestRight = 0;
  for (let right = 0; right < PARCELS.length; right++) {
    sum += PARCELS[right];
    while (sum > CAPACITY) sum -= PARCELS[left++];
    if (right - left + 1 > best) { best = right - left + 1; bestLeft = left; bestRight = right; }
    PARCEL_TRACE.push({ left, right, sum, best, bestLeft, bestRight });
  }
  const PARCEL = { title: 'B. Parcel Conveyor', statement: 'There are n parcels of weight a[i], in a fixed order. Choose a contiguous block weighing at most C. What is the maximum number of parcels?',
    format: 'Input: n, C and array a. Output: the maximum count.', bounds: '1 ≤ n ≤ 200000; 1 ≤ a[i] ≤ 10⁹; 1 ≤ C ≤ 10¹⁴', input: ['8 10', '3 1 2 6 1 2 1 4'], answer: best };
  K.registerBg('echoParcelWindow', 'Parcel Conveyor', ['graphic', 'pop', 'calm'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 8), step = Math.min(7, Math.floor(t / 0.36)), done = t >= 2.88;
    const s = PARCEL_TRACE[step], L = done ? s.bestLeft : s.left, R = done ? s.bestRight : s.right;
    const T = sheet(env, ctx, PARCEL, done ? 'AC / O(n)' : 'TWO POINTERS'), U = T.U;
    const x0 = W * 0.07, cw = W * 0.108, base = H * 0.855, boxH = H * 0.105;
    ctx.strokeStyle = T.faint; ctx.lineWidth = U * 0.003; line(ctx, W * 0.055, base + U * 0.008, W * 0.945, base + U * 0.008);
    for (let i = 0; i < 8; i++) {
      const x = x0 + i * cw, on = i >= L && i <= R;
      ctx.fillStyle = rgba(on ? T.a : T.fg, on ? 0.16 : 0.04); ctx.fillRect(x, base - boxH, cw * 0.78, boxH);
      ctx.strokeStyle = rgba(on ? T.a : T.fg, on ? 0.65 : 0.2); ctx.lineWidth = U * 0.002; ctx.strokeRect(x, base - boxH, cw * 0.78, boxH);
      // A packing seam and small shipping label make the array an actual conveyor.
      ctx.strokeStyle = rgba(T.fg, 0.13); line(ctx, x + cw * 0.39, base - boxH, x + cw * 0.39, base - boxH * 0.75);
      text(ctx, String(PARCELS[i]), x + cw * 0.39, base - boxH * 0.38, U * 0.034, on ? T.fg : T.dim, 'center', 700);
      text(ctx, `${i + 1}`, x + cw * 0.39, base + U * 0.037, U * 0.013, T.dim, 'center');
    }
    const ax = x0 + L * cw, bx = x0 + (R + 0.78) * cw, by = base - boxH - U * 0.025;
    ctx.strokeStyle = T.b; ctx.lineWidth = U * 0.002; line(ctx, ax, by, bx, by);
    text(ctx, 'L', ax, by - U * 0.012, U * 0.019, T.b, 'center', 700);
    text(ctx, 'R', bx, by - U * 0.012, U * 0.019, T.b, 'center', 700);
    const weight = done ? PARCELS.slice(L, R + 1).reduce((a, b) => a + b, 0) : s.sum;
    text(ctx, `${weight} / ${CAPACITY} kg`, W * 0.945, H * 0.955, U * 0.023, T.b, 'right', 700);
    text(ctx, `CURRENT ${R - L + 1} / BEST ${s.best} PARCELS`, W * 0.055, H * 0.955, U * 0.019, T.dim);
  });

  const HEIGHTS = [3, 0, 2, 0, 4, 1, 0, 3, 1, 2], BEFORE = [], AFTER = [], WATER = [];
  for (let i = 0, max = 0; i < HEIGHTS.length; i++) BEFORE[i] = max = Math.max(max, HEIGHTS[i]);
  for (let i = HEIGHTS.length - 1, max = 0; i >= 0; i--) AFTER[i] = max = Math.max(max, HEIGHTS[i]);
  for (let i = 0; i < HEIGHTS.length; i++) WATER[i] = Math.min(BEFORE[i], AFTER[i]) - HEIGHTS[i];
  const VOLUME = WATER.reduce((a, b) => a + b, 0);
  const RAIN = { title: 'C. Rain Reservoir', statement: 'There are n unit-width columns of height h[i]. Rain drains off both ends. Find the maximum volume of water trapped between the columns.',
    format: 'Input: n and array h. Output: the trapped volume.', bounds: '1 ≤ n ≤ 200000; 0 ≤ h[i] ≤ 10⁹; width = 1', input: ['10', '3 0 2 0 4 1 0 3 1 2'], answer: VOLUME };
  K.registerBg('echoRainReservoir', 'Rain Reservoir', ['emotional', 'graphic', 'calm'], (env, P, ctx) => {
    const { W, H } = env, t = K.wrap(K.sinceStart(env), 8), count = Math.min(10, Math.floor(t / 0.27)), done = count === 10;
    const T = sheet(env, ctx, RAIN, done ? 'AC / O(n)' : 'PREFIX / SUFFIX MAX'), U = T.U;
    const x0 = W * 0.085, cw = W * 0.083, floor = H * 0.9, unit = H * 0.046;
    let volume = 0;
    for (let i = 0; i < HEIGHTS.length; i++) {
      const x = x0 + i * cw, h = HEIGHTS[i] * unit, water = i < count ? WATER[i] : 0;
      volume += water;
      ctx.fillStyle = mix(T.bg, T.fg, 0.13); ctx.fillRect(x, floor - h, cw - 2, h);
      ctx.strokeStyle = rgba(T.fg, 0.3); ctx.lineWidth = 1; line(ctx, x, floor - h, x + cw - 2, floor - h);
      if (water) {
        const level = floor - h - water * unit;
        ctx.fillStyle = rgba(T.a, 0.24); ctx.fillRect(x, level, cw - 2, water * unit);
        ctx.strokeStyle = rgba(T.b, 0.75); ctx.lineWidth = U * 0.002; ctx.beginPath();
        for (let k = 0; k <= 8; k++) { const xx = x + (cw - 2) * k / 8, yy = level + Math.sin(k * 0.7 + t * 2 + i) * U * 0.002; if (k) ctx.lineTo(xx, yy); else ctx.moveTo(xx, yy); } ctx.stroke();
        text(ctx, `+${water}`, x + cw * 0.5, level + water * unit * 0.55 + U * 0.005, U * 0.02, T.a, 'center', 700);
      }
      text(ctx, String(HEIGHTS[i]), x + cw * 0.5, floor + U * 0.025, U * 0.015, T.dim, 'center');
    }
    // A small finite rain field, wholly below the central lyric band.
    ctx.strokeStyle = rgba(T.fg, 0.15); ctx.lineWidth = 1;
    for (let i = 0; i < 14; i++) {
      const x = W * (0.08 + J.r(P.seed || 42, i, 9) * 0.84), y = H * (0.64 + K.wrap(t * 0.18 + i * 0.17, 0.16));
      line(ctx, x, y, x - U * 0.004, y + U * 0.014);
    }
    text(ctx, `V = ${volume}`, W * 0.945, H * 0.975, U * 0.023, T.b, 'right', 700);
    text(ctx, 'water[i] = min(leftMax, rightMax) - h[i]', W * 0.055, H * 0.975, U * 0.015, T.dim);
  });
})();
