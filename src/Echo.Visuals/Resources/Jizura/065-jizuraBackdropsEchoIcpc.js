/* First-party ICPC scenes: a solved flow network, a convex-hull sweep and a scoreboard reveal.
   All artwork uses the look's palette. Fixed-size data; no extra canvases, assets or caches. */
(() => {
  'use strict';
  const K = J.echo, { mix, rgba, clamp, TAU } = K;
  const MONO = 'ui-monospace,"SF Mono",Menlo,Consolas,monospace';
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const circle = (ctx, x, y, radius) => { ctx.beginPath(); ctx.arc(x, y, radius, 0, TAU); };
  const line = (ctx, ax, ay, bx, by) => { ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); };
  const text = (ctx, value, x, y, size, colour, weight = 400, align = 'left') => {
    ctx.font = `${weight} ${size}px ${MONO}`; ctx.fillStyle = colour; ctx.textAlign = align; ctx.fillText(value, x, y);
  };
  const ground = (env, ctx, title, subtitle) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, fg: sc.fg, a: sc.accent, b: sc.accent2, dim: rgba(sc.fg, dark ? 0.4 : 0.5),
      base: mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.18), quiet: rgba(sc.fg, dark ? 0.09 : 0.12) };
    ctx.globalAlpha = K.fade(env, 0.65);
    ctx.fillStyle = T.base; ctx.fillRect(0, 0, W, H);
    // A drafting grid, kept faint so arbitrary lyric layouts remain legible.
    ctx.strokeStyle = rgba(sc.fg, 0.035); ctx.lineWidth = 1;
    ctx.beginPath();
    const step = U * 0.075;
    for (let x = 0; x < W; x += step) { ctx.moveTo(x, 0); ctx.lineTo(x, H); }
    for (let y = 0; y < H; y += step) { ctx.moveTo(0, y); ctx.lineTo(W, y); }
    ctx.stroke();
    text(ctx, title, W * 0.055, H * 0.092, U * 0.018, T.a, 700);
    text(ctx, subtitle, W * 0.055, H * 0.13, U * 0.014, T.dim);
    ctx.strokeStyle = T.quiet; line(ctx, W * 0.055, H * 0.17, W * 0.945, H * 0.17);
    return T;
  };

  // A feasible maximum flow of 12. The highlighted cut has capacity 4 + 3 + 5.
  // Paths augment by 4, 3, 2, 3; edge labels replay these same facts.
  const NODES = [[0, 0.5], [0.24, 0.06], [0.24, 0.94], [0.7, 0.06], [0.7, 0.94], [1, 0.5]];
  const EDGES = [[0, 1, 7], [0, 2, 5], [1, 3, 4], [1, 4, 3], [2, 4, 5], [3, 5, 4], [4, 5, 8]];
  const PATHS = [[0, 2, 5], [0, 3, 6], [1, 4, 6], [1, 4, 6]], DELTA = [4, 3, 2, 3];
  K.registerBg('echoMinCut', '最小カット', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx, 'G / FLOW NETWORK', 'augmenting paths  /  residual capacity'), U = T.U;
    const t = K.wrap(K.sinceStart(env), 7), stage = Math.min(4, Math.floor(t / 0.85)), phase = t / 0.85 - stage;
    const complete = stage === 4, flow = [0, 0, 0, 0, 0, 0, 0];
    let value = 0;
    for (let i = 0; i < stage; i++) { value += DELTA[i]; for (const e of PATHS[i]) flow[e] += DELTA[i]; }
    const ox = W * 0.1, oy = H * 0.66, gw = W * 0.8, gh = H * 0.22, nr = U * 0.024;
    const points = NODES.map(([x, y]) => [ox + x * gw, oy + y * gh]);
    // The cut is a slanted seam, with transverse ticks rather than a selection bar.
    if (complete) {
      ctx.fillStyle = rgba(T.a, 0.045); ctx.beginPath(); ctx.moveTo(W * 0.05, H * 0.6);
      ctx.lineTo(W * 0.52, H * 0.6); ctx.lineTo(W * 0.44, H * 0.93); ctx.lineTo(W * 0.05, H * 0.93); ctx.closePath(); ctx.fill();
      ctx.strokeStyle = rgba(T.b, 0.65); ctx.lineWidth = U * 0.002; ctx.setLineDash([U * 0.006, U * 0.009]);
      line(ctx, W * 0.52, H * 0.61, W * 0.44, H * 0.91); ctx.setLineDash([]);
      text(ctx, 'S', W * 0.38, H * 0.92, U * 0.016, T.a);
      text(ctx, 'T', W * 0.57, H * 0.92, U * 0.016, T.b);
    }
    EDGES.forEach(([a, b, capacity], i) => {
      const [ax, ay] = points[a], [bx, by] = points[b], dx = bx - ax, dy = by - ay, len = Math.hypot(dx, dy);
      const active = !complete && PATHS[stage].includes(i), cut = complete && [2, 3, 4].includes(i);
      ctx.strokeStyle = cut ? rgba(T.b, 0.8) : rgba(T.a, flow[i] ? 0.55 : 0.16);
      ctx.lineWidth = U * (flow[i] ? 0.004 : 0.002); line(ctx, ax, ay, bx, by);
      const hx = bx - dx / len * nr * 1.65, hy = by - dy / len * nr * 1.65;
      const angle = Math.atan2(dy, dx), arrow = U * 0.009;
      ctx.beginPath(); ctx.moveTo(hx, hy); ctx.lineTo(hx - Math.cos(angle - 0.5) * arrow, hy - Math.sin(angle - 0.5) * arrow);
      ctx.moveTo(hx, hy); ctx.lineTo(hx - Math.cos(angle + 0.5) * arrow, hy - Math.sin(angle + 0.5) * arrow); ctx.stroke();
      if (active) {
        const p = ease(clamp(phase * 1.5));
        ctx.strokeStyle = T.a; ctx.lineWidth = U * 0.005; line(ctx, ax, ay, ax + dx * p, ay + dy * p);
        ctx.fillStyle = T.fg; circle(ctx, ax + dx * p, ay + dy * p, U * 0.005); ctx.fill();
      }
      const mx = (ax + bx) / 2, my = (ay + by) / 2;
      ctx.fillStyle = T.base; ctx.fillRect(mx - U * 0.026, my - U * 0.016, U * 0.052, U * 0.032);
      text(ctx, `${flow[i]}/${capacity}`, mx, my + U * 0.005, U * 0.017, cut ? T.b : T.dim, 400, 'center');
    });
    points.forEach(([x, y], i) => {
      ctx.fillStyle = T.base; circle(ctx, x, y, nr); ctx.fill(); ctx.strokeStyle = rgba(T.a, 0.7); ctx.lineWidth = U * 0.002; ctx.stroke();
      text(ctx, ['s', 'a', 'b', 'c', 'd', 't'][i], x, y + U * 0.008, U * 0.024, T.fg, 700, 'center');
    });
    text(ctx, complete ? 'MAX FLOW = MIN CUT' : `AUGMENT ${stage + 1} / 4`, W * 0.945, H * 0.092, U * 0.018, complete ? T.b : T.dim, 700, 'right');
    text(ctx, String(value).padStart(2, '0'), W * 0.945, H * 0.29, U * 0.11, rgba(T.a, 0.55), 700, 'right');
    text(ctx, complete ? '4 + 3 + 5 = 12' : `flow + ${DELTA[stage]}`, W * 0.945, H * 0.33, U * 0.015, T.dim, 400, 'right');
  });

  // A monotone-chain convex hull, with a bounded event list including popped turns.
  const HULL_POINTS = [[0.04, 0.56], [0.13, 0.2], [0.2, 0.76], [0.28, 0.39], [0.34, 0.06], [0.42, 0.69],
    [0.49, 0.95], [0.57, 0.28], [0.65, 0.58], [0.72, 0.12], [0.78, 0.84], [0.87, 0.36], [0.96, 0.64]];
  const cross = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
  const hullEvents = [], lower = [], upper = [];
  for (const [chain, order] of [[lower, HULL_POINTS], [upper, [...HULL_POINTS].reverse()]]) {
    for (const p of order) {
      while (chain.length > 1 && cross(chain[chain.length - 2], chain[chain.length - 1], p) <= 0) {
        const rejected = chain.pop(); hullEvents.push({ lower: lower.slice(), upper: upper.slice(), p, rejected });
      }
      chain.push(p); hullEvents.push({ lower: lower.slice(), upper: upper.slice(), p });
    }
  }
  const HULL = [...lower.slice(0, -1), ...upper.slice(0, -1)];
  K.registerBg('echoConvexHull', '凸包', ['graphic', 'editorial', 'calm'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx, 'H / CONVEX HULL', 'monotone chain  /  O(n log n)'), U = T.U;
    const t = K.wrap(K.sinceStart(env), 8), step = Math.floor(t / 0.085), complete = step >= hullEvents.length;
    const event = hullEvents[Math.min(step, hullEvents.length - 1)];
    const ox = W * 0.12, oy = H * 0.65, gw = W * 0.76, gh = H * 0.23;
    const pos = p => [ox + p[0] * gw, oy + p[1] * gh];
    const path = points => { ctx.beginPath(); points.forEach((p, i) => { const [x, y] = pos(p); if (i) ctx.lineTo(x, y); else ctx.moveTo(x, y); }); };
    // Finish with a translucent faceted shape, leaving the lyric's middle band open.
    if (complete) {
      path(HULL); ctx.closePath(); ctx.fillStyle = rgba(T.a, 0.075); ctx.fill();
      ctx.strokeStyle = rgba(T.a, 0.8); ctx.lineWidth = U * 0.003; ctx.stroke();
      const [cx, cy] = pos([0.5, 0.52]);
      ctx.strokeStyle = rgba(T.a, 0.1); ctx.lineWidth = 1;
      for (const p of HULL) { const [x, y] = pos(p); line(ctx, cx, cy, x, y); }
    } else {
      for (const [chain, colour] of [[event.lower, T.a], [event.upper, T.b]]) {
        path(chain); ctx.strokeStyle = rgba(colour, 0.8); ctx.lineWidth = U * 0.003; ctx.stroke();
      }
      const [sx] = pos(event.p);
      ctx.strokeStyle = rgba(T.b, 0.3); ctx.lineWidth = 1; ctx.setLineDash([3, 6]); line(ctx, sx, H * 0.62, sx, H * 0.93); ctx.setLineDash([]);
      if (event.rejected) {
        const [x, y] = pos(event.rejected); ctx.strokeStyle = T.b; const d = U * 0.008;
        line(ctx, x - d, y - d, x + d, y + d); line(ctx, x + d, y - d, x - d, y + d);
      }
    }
    HULL_POINTS.forEach((p, i) => {
      const [x, y] = pos(p), onHull = complete && HULL.includes(p), current = !complete && p === event.p;
      if (onHull || current) { circle(ctx, x, y, U * 0.014); ctx.strokeStyle = rgba(T.a, 0.22); ctx.lineWidth = 1; ctx.stroke(); }
      circle(ctx, x, y, U * (onHull || current ? 0.005 : 0.003)); ctx.fillStyle = onHull || current ? T.a : T.dim; ctx.fill();
      text(ctx, `p${i}`, x, y - U * 0.02, U * 0.012, T.dim, 400, 'center');
    });
    text(ctx, complete ? 'BOUNDARY FOUND' : event.rejected ? 'POP / NON-LEFT TURN' : 'PUSH / LEFT TURN', W * 0.945, H * 0.092, U * 0.018, T.b, 700, 'right');
    text(ctx, 'cross(a,b,c)', W * 0.055, H * 0.25, U * 0.026, rgba(T.fg, 0.4));
    text(ctx, '= (b-a) × (c-a)', W * 0.055, H * 0.295, U * 0.018, T.dim);
    text(ctx, `${HULL_POINTS.length} POINTS / ${complete ? HULL.length : '—'} VERTICES`, W * 0.945, H * 0.95, U * 0.014, T.dim, 400, 'right');
  });

  const REVEAL_TEAMS = ['NORTH / ALPHA', 'HARBOR / HELIX', 'SUMMIT / COBALT'];
  const SOLVED = [9, 8, 8], PENALTY = [1042, 1186, 1231];
  K.registerBg('echoBoardReveal', 'リゾルバ', ['graphic', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx, 'FINAL / THE RESOLVER', 'five hours  /  three people  /  one machine'), U = T.U;
    const t = K.wrap(K.sinceStart(env), 10), reveal = ease((t - 1.4) / 1.2), final = t >= 2.6, portrait = H > W;
    text(ctx, final ? 'STANDINGS REVEALED' : 'SCOREBOARD FROZEN', W * 0.945, H * 0.092, U * 0.018, final ? T.a : T.b, 700, 'right');
    // An outlined rank numeral is the signature, quieter than a filled hero behind lyrics.
    ctx.font = `900 ${U * (portrait ? 0.32 : 0.42)}px ${MONO}`; ctx.textAlign = 'right';
    ctx.strokeStyle = rgba(T.a, 0.14 + reveal * 0.13); ctx.lineWidth = U * 0.0015;
    ctx.strokeText(final ? '01' : '??', W * 0.94, H * (portrait ? 0.36 : 0.54));
    const y0 = H * 0.68, rh = H * 0.087, x0 = W * 0.055, end = W * 0.945;
    for (let i = 0; i < 3; i++) {
      const y = y0 + i * rh, winner = i === 0;
      ctx.strokeStyle = T.quiet; ctx.lineWidth = 1; line(ctx, x0, y + rh * 0.78, end, y + rh * 0.78);
      text(ctx, String(i + 1).padStart(2, '0'), x0, y + rh * 0.3, U * 0.026, winner ? T.a : T.dim, 700);
      text(ctx, REVEAL_TEAMS[i], W * 0.14, y + rh * 0.3, U * (portrait ? 0.018 : 0.022), T.fg, 700);
      const cellsX = W * (portrait ? 0.14 : 0.52), cellsY = y + rh * (portrait ? 0.58 : 0.2), cell = W * (portrait ? 0.026 : 0.022);
      for (let j = 0; j < 9; j++) {
        const pending = j === SOLVED[i] - 1, solved = j < SOLVED[i], x = cellsX + j * cell * 1.25;
        const a = pending ? reveal : 1;
        ctx.fillStyle = solved ? rgba(T.a, 0.14 + 0.64 * a) : T.quiet;
        ctx.fillRect(x, cellsY - cell * 0.4, cell, cell * 0.7);
        if (pending && !final) text(ctx, '?', x + cell * 0.5, cellsY + cell * 0.13, cell * 0.6, T.b, 700, 'center');
      }
      text(ctx, `${final ? SOLVED[i] : SOLVED[i] - 1}`, W * 0.845, y + rh * 0.3, U * 0.03, winner ? T.a : T.fg, 700, 'right');
      text(ctx, final ? String(PENALTY[i]) : '—', end, y + rh * 0.3, U * 0.018, T.dim, 400, 'right');
    }
    text(ctx, 'SOLVED', W * 0.845, H * 0.645, U * 0.012, T.dim, 400, 'right');
    text(ctx, 'PENALTY', end, H * 0.645, U * 0.012, T.dim, 400, 'right');
    // Nine rising balloons, anchored to the winner's last accepted problem.
    for (let i = 0; i < 9; i++) {
      const rise = clamp((t - 2.4 - i * 0.1) / 5), x = W * (0.6 + i * 0.035) + Math.sin(t * 0.8 + i) * U * 0.008;
      const y = H * (0.59 - rise * 0.38), rad = U * 0.011;
      if (!rise) continue;
      ctx.globalAlpha = K.fade(env, 0.65) * (1 - ease((rise - 0.8) * 5)) * 0.65;
      ctx.strokeStyle = rgba(T.fg, 0.2); ctx.lineWidth = 1;
      ctx.beginPath(); ctx.moveTo(x, y + rad); ctx.bezierCurveTo(x - rad, y + rad * 2, x + rad, y + rad * 3, x, y + rad * 4); ctx.stroke();
      ctx.beginPath(); ctx.ellipse(x, y, rad * 0.8, rad, 0, 0, TAU); ctx.fillStyle = i % 3 ? T.a : T.b; ctx.fill();
    }
  });
})();
