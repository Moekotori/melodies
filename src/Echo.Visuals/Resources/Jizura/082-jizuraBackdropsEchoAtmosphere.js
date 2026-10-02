/* First-party ambient scenes: satin ribbons, folded paper and a curtain of stardust.
   All colour follows the active scheme. Fixed geometry and finite particles; no asset or canvas caches. */
(() => {
  'use strict';
  const K = J.echo, { mix, rgba, TAU } = K;
  const scenes = ['echoSatinFlow', 'echoPaperBloom', 'echoStardustVeil'];
  K.commonBackdrops = Object.freeze([...(K.commonBackdrops || []), ...scenes]);
  const ground = (env, ctx) => {
    const { W, H, sc } = env, U = K.U(env), dark = K.isDark(sc.bg);
    const T = { U, dark, bg: mix(sc.bg, dark ? '#000000' : '#FFFFFF', 0.16), fg: sc.fg, a: sc.accent, b: sc.accent2 };
    ctx.globalAlpha = K.fade(env, 0.7);
    const g = ctx.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, mix(T.bg, T.a, 0.045)); g.addColorStop(0.5, T.bg); g.addColorStop(1, mix(T.bg, T.b, 0.065));
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
    return T;
  };
  const glow = (ctx, x, y, radius, colour, alpha) => K.glowDisc(ctx, x, y, radius, colour, alpha);

  K.registerBg('echoSatinFlow', '丝绸光带', ['emotional', 'calm', 'editorial'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    glow(ctx, W * 0.76, H * 0.8, U * 0.7, T.a, T.dark ? 0.12 : 0.07);
    const N = 64, points = new Float32Array((N + 1) * 6);
    const ribbon = (upper) => {
      const phase = t * 0.15 + (upper ? 1.6 : 0);
      // Store each tangent once, then reuse it for all satin shading strips.
      for (let i = 0; i <= N; i++) {
        const u = i / N, k = u * 5.8 + phase;
        const x = W * (-0.13 + u * 1.28), y = H * ((upper ? 0.04 : 0.88) + Math.sin(k) * (upper ? 0.105 : 0.13));
        const slope = H * Math.cos(k) * (upper ? 0.105 : 0.13) * 5.8 / (W * 1.28), norm = Math.sqrt(1 + slope * slope);
        const width = U * (upper ? 0.075 : 0.14) * (0.55 + 0.45 * Math.pow(Math.sin(u * Math.PI), 2));
        const off = i * 6; points[off] = x; points[off + 1] = y; points[off + 2] = -slope / norm;
        points[off + 3] = 1 / norm; points[off + 4] = width;
        points[off + 5] = Math.sin(k * 1.1 + phase) * 0.17;
      }
      const follow = (i, side, move = false) => {
        const off = i * 6, d = (side + points[off + 5] * (1 - side * side)) * points[off + 4];
        const x = points[off] + points[off + 2] * d, y = points[off + 1] + points[off + 3] * d;
        if (move) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      };
      for (let strip = 0; strip < 36; strip++) {
        const lo = -1 + strip / 18, hi = lo + 1 / 18 + 0.008, v = (lo + hi) * 0.5;
        const shine = Math.exp(-Math.pow((v - 0.13 - Math.sin(t * 0.12) * 0.06) / 0.19, 2));
        const base = mix(T.bg, upper ? T.b : T.a, T.dark ? 0.38 : 0.2);
        const colour = mix(base, T.fg, (T.dark ? 0.07 : 0.035) + shine * (T.dark ? 0.54 : 0.24));
        ctx.fillStyle = colour; ctx.beginPath(); follow(0, lo, true);
        for (let i = 1; i <= N; i++) follow(i, lo);
        for (let i = N; i >= 0; i--) follow(i, hi);
        ctx.closePath(); ctx.fill();
      }
      // Fine longitudinal fibres catch light without adding a noisy texture layer.
      for (let strand = 0; strand < 8; strand++) {
        const v = -0.84 + strand * 0.23;
        ctx.strokeStyle = rgba(T.fg, strand === 4 ? 0.12 : 0.012); ctx.lineWidth = U * 0.0007;
        ctx.beginPath(); follow(0, v, true); for (let i = 1; i <= N; i++) follow(i, v); ctx.stroke();
      }
    };
    ribbon(true); ribbon(false);
  }, false);

  K.registerBg('echoPaperBloom', '悬浮纸雕', ['calm', 'editorial', 'emotional'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env);
    const bloom = (cx, cy, radius, petals, turn) => {
      glow(ctx, cx + radius * 0.05, cy + radius * 0.13, radius * 1.3, T.fg, T.dark ? 0.06 : 0.045);
      ctx.save(); ctx.translate(cx, cy); ctx.rotate(turn);
      for (let i = 0; i < petals; i++) {
        ctx.save(); ctx.rotate(i / petals * TAU);
        const length = radius * (0.93 + 0.07 * Math.sin(i * 1.7)), width = radius * 0.3, root = radius * 0.09;
        // A curved paper petal, with two differently lit planes meeting at its fold.
        const petal = (dx, dy) => {
          ctx.beginPath(); ctx.moveTo(root + dx, dy);
          ctx.bezierCurveTo(length * 0.32 + dx, -width * 0.82 + dy, length * 0.85 + dx, -width * 0.72 + dy, length + dx, -width * 0.18 + dy);
          ctx.bezierCurveTo(length * 0.96 + dx, width * 0.35 + dy, length * 0.48 + dx, width * 0.64 + dy, root + dx, dy); ctx.closePath();
        };
        const shadow = ctx.createLinearGradient(0, -width, 0, width);
        shadow.addColorStop(0, rgba(T.fg, 0)); shadow.addColorStop(0.65, rgba(T.fg, T.dark ? 0.045 : 0.1)); shadow.addColorStop(1, rgba(T.fg, 0));
        petal(radius * 0.018, radius * 0.035); ctx.fillStyle = shadow; ctx.fill();
        petal(0, 0);
        const paper = T.dark ? mix(T.bg, T.fg, 0.7) : mix(T.bg, '#FFFFFF', 0.76);
        const body = ctx.createLinearGradient(0, -width, 0, width);
        body.addColorStop(0, paper);
        body.addColorStop(0.47, mix(paper, T.dark ? T.bg : T.fg, 0.22));
        body.addColorStop(0.49, mix(paper, T.dark ? T.fg : '#FFFFFF', 0.3));
        body.addColorStop(1, mix(paper, T.dark ? T.bg : T.fg, 0.3));
        ctx.fillStyle = body; ctx.fill();
        ctx.strokeStyle = rgba(T.fg, T.dark ? 0.08 : 0.1); ctx.lineWidth = U * 0.001; ctx.stroke();
        ctx.beginPath(); ctx.moveTo(root, 0); ctx.bezierCurveTo(length * 0.4, -width * 0.1, length * 0.82, -width * 0.06, length, -width * 0.18);
        ctx.strokeStyle = rgba(T.fg, T.dark ? 0.25 : 0.09); ctx.stroke();
        ctx.restore();
      }
      const center = ctx.createRadialGradient(-radius * 0.025, -radius * 0.035, 0, 0, 0, radius * 0.11);
      center.addColorStop(0, mix(T.bg, T.b, 0.2)); center.addColorStop(1, T.bg);
      ctx.fillStyle = center; ctx.beginPath(); ctx.arc(0, 0, radius * 0.095, 0, TAU); ctx.fill();
      ctx.restore();
    };
    bloom(W * 0.85, H * 0.69 + Math.sin(t * 0.18) * U * 0.012, U * 0.39, 11, -0.5 + t * 0.025);
    bloom(W * 0.065, H * 0.14, U * 0.23, 9, 0.2 - t * 0.018);
    // Three drifting paper slivers establish depth, while leaving the lyric band clear.
    for (let i = 0; i < 3; i++) {
      const x = W * (0.12 + i * 0.29), y = H * (0.88 - i * 0.38) + Math.sin(t * 0.22 + i) * U * 0.014;
      ctx.save(); ctx.translate(x, y); ctx.rotate(i * 0.7 + t * 0.035);
      ctx.fillStyle = mix(T.bg, T.fg, T.dark ? 0.24 : 0.07); ctx.beginPath();
      ctx.moveTo(-U * 0.036, 0); ctx.quadraticCurveTo(0, -U * 0.021, U * 0.036, 0); ctx.quadraticCurveTo(0, U * 0.003, -U * 0.036, 0); ctx.fill();
      ctx.restore();
    }
  }, false);

  K.registerBg('echoStardustVeil', '星尘帷幕', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H } = env, T = ground(env, ctx), U = T.U, t = K.sinceStart(env), seed = P.seed || 42;
    glow(ctx, W * 0.82, H * 0.38, U * 0.8, T.a, T.dark ? 0.14 : 0.08);
    glow(ctx, W * 0.1, H * 0.84, U * 0.55, T.b, T.dark ? 0.09 : 0.045);
    const position = (filament, u, out) => {
      const anchor = filament === 0 ? 0.035 : 0.63 + filament * 0.053;
      out[0] = W * (anchor + Math.sin(u * 4.5 + filament * 0.28 + t * 0.11) * 0.055);
      out[1] = H * u;
    };
    const pos = [0, 0];
    for (let f = 0; f < 6; f++) {
      const g = ctx.createLinearGradient(0, 0, 0, H);
      g.addColorStop(0, rgba(T.a, 0)); g.addColorStop(0.38, rgba(T.a, T.dark ? 0.2 : 0.1));
      g.addColorStop(0.7, rgba(T.b, T.dark ? 0.18 : 0.08)); g.addColorStop(1, rgba(T.b, 0));
      ctx.strokeStyle = g; ctx.lineWidth = U * 0.0008; ctx.beginPath();
      for (let i = 0; i <= 48; i++) { position(f, i / 48, pos); if (i) ctx.lineTo(pos[0], pos[1]); else ctx.moveTo(pos[0], pos[1]); } ctx.stroke();
      for (let bead = 0; bead < 12; bead++) {
        const u = K.wrap((bead + J.r(seed, f * 20 + bead, 1) * 0.5) / 12 + t * 0.006, 1);
        position(f, u, pos);
        const edge = Math.sin(u * Math.PI), bright = 0.3 + 0.7 * J.r(seed, f * 20 + bead, 2), rad = U * (0.0012 + bright * 0.0022);
        const colour = mix(f % 2 ? T.a : T.b, T.fg, 0.6);
        if (bead % 4 === 0) glow(ctx, pos[0], pos[1], rad * 10, colour, edge * (T.dark ? 0.18 : 0.06));
        ctx.fillStyle = rgba(colour, edge * bright * (T.dark ? 0.9 : 0.5)); ctx.beginPath(); ctx.arc(pos[0], pos[1], rad, 0, TAU); ctx.fill();
        if (bead % 5 === 0) {
          ctx.strokeStyle = rgba(colour, edge * 0.3); ctx.lineWidth = U * 0.0007; ctx.beginPath();
          ctx.moveTo(pos[0] - rad * 3, pos[1]); ctx.lineTo(pos[0] + rad * 3, pos[1]);
          ctx.moveTo(pos[0], pos[1] - rad * 3); ctx.lineTo(pos[0], pos[1] + rad * 3); ctx.stroke();
        }
      }
    }
    // Out-of-focus dust is finite and seeded, never accumulated in a simulation.
    for (let i = 0; i < 16; i++) {
      const x = W * J.r(seed, i, 15), y = H * K.wrap(J.r(seed, i, 16) + t * 0.004, 1);
      if (x > W * 0.18 && x < W * 0.65 && y > H * 0.3 && y < H * 0.7) continue;
      const rad = U * (0.01 + J.r(seed, i, 17) * 0.025);
      glow(ctx, x, y, rad, i % 2 ? T.a : T.b, T.dark ? 0.09 : 0.04);
    }
  }, false);
})();
