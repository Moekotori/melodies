/* ECHO first-party backdrops, "rock" tributes B: the looks 煉瓦 (a wall, light behind it) and Psychedelic (1960s light show, lasers, an eclipse).
   Original imagery only; every colour comes from the current scheme (dark = additive light, light = inked washes). */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, layer } = K;
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const tones = sc => {
    const dark = isDark(sc.bg);
    return dark
      ? { dark, bg0: mix(sc.bg, '#000000', 0.6), bg1: mix(sc.bg, '#000000', 0.3), c1: mix(sc.accent, '#FFFFFF', 0.12), c2: mix(sc.accent2, '#FFFFFF', 0.1), c3: mix(sc.ghostA || sc.accent, '#FFFFFF', 0.25), line: mix(sc.fg, sc.bg, 0.25), hi: mix(sc.accent, '#FFFFFF', 0.6), ink: '#000000' }
      : { dark, bg0: mix(sc.bg, '#FFFFFF', 0.55), bg1: sc.bg, c1: mix(sc.accent, '#000000', 0.06), c2: mix(sc.accent2, '#000000', 0.04), c3: sc.ghostA || sc.accent, line: mix(sc.fg, sc.bg, 0.25), hi: mix(sc.accent, '#FFFFFF', 0.7), ink: mix(sc.fg, '#000000', 0.2) };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  const SPEC = ['#FF3B6B', '#FF9A3D', '#FFE14D', '#4DE38B', '#3DC8FF', '#6B6BFF', '#C24DFF'];

  /* ---------- toy bricks (original, generic studded blocks drawn from scratch) ---------- */
  const TOY = ['#E5483C', '#F29B2E', '#F4D03F', '#4FBF6B', '#3FA2E6', '#8B6FE0', '#EE6FA6', '#F2EFE6'];
  const toyCol = (i, sc, T) => mix(mix(TOY[((i % 8) + 8) % 8], sc.accent, 0.12), sc.bg, T.dark ? 0.1 : 0);
  const SPR = new Map();
  const brickGeo = (n, u) => { const p = 2, sh = u * 0.17, sry = u * 0.13, tf = u * 0.32, bodyH = u * 1.15, top0 = p + sh + sry; return { n, u, p, sh, sry, srx: u * 0.29, tf, bodyH, top0, fy: top0 + tf, w: n * u + p * 2, h: top0 + tf + bodyH + p }; };
  const paintBrick = (x, n, col, u, lit) => {
    const g = brickGeo(n, u), p = g.p, Wd = n * u, light = mix(col, '#FFFFFF', 0.34), dark = mix(col, '#000000', 0.3);
    x.fillStyle = light; x.beginPath(); x.moveTo(p + u * 0.06, g.top0); x.lineTo(p + Wd - u * 0.06, g.top0); x.lineTo(p + Wd, g.fy); x.lineTo(p, g.fy); x.closePath(); x.fill();
    for (let k = 0; k < n; k++) {
      const cx = p + (k + 0.5) * u, cy = g.top0 + g.tf * 0.55;
      x.fillStyle = mix(col, '#000000', 0.1); x.beginPath(); x.ellipse(cx, cy, g.srx, g.sry, 0, 0, TAU); x.fill(); x.fillRect(cx - g.srx, cy - g.sh, g.srx * 2, g.sh);
      x.fillStyle = mix(col, '#FFFFFF', 0.45); x.beginPath(); x.ellipse(cx, cy - g.sh, g.srx, g.sry, 0, 0, TAU); x.fill();
      x.strokeStyle = rgba('#FFFFFF', 0.55); x.lineWidth = Math.max(0.8, u * 0.035); x.beginPath(); x.ellipse(cx, cy - g.sh, g.srx * 0.62, g.sry * 0.6, 0, Math.PI * 1.05, Math.PI * 1.55); x.stroke();
    }
    const fy = g.fy, rad = u * 0.08, grd = x.createLinearGradient(0, fy, 0, fy + g.bodyH);
    grd.addColorStop(0, mix(col, '#FFFFFF', lit ? 0.5 : 0.12)); grd.addColorStop(0.55, col); grd.addColorStop(1, mix(col, '#000000', 0.2));
    x.fillStyle = grd; x.beginPath(); if (x.roundRect) x.roundRect(p, fy, Wd, g.bodyH, rad); else x.rect(p, fy, Wd, g.bodyH); x.fill();
    x.strokeStyle = rgba(dark, 0.55); x.lineWidth = Math.max(0.8, u * 0.03); x.stroke();
    x.strokeStyle = rgba('#FFFFFF', 0.4); x.beginPath(); x.moveTo(p + rad, fy + u * 0.05); x.lineTo(p + Wd - rad, fy + u * 0.05); x.stroke();
    x.fillStyle = rgba('#000000', 0.14); x.fillRect(p + 1, fy + g.bodyH - u * 0.1, Wd - 2, u * 0.08);
    return g;
  };
  /* one resident sprite per (size, colour); `blur` bakes a soft focus in once instead of filtering every frame */
  const brickSprite = (env, n, ci, u, T, sc, blur = 0, lit = false) => {
    const s = env.scale || 1, id = `br|${n}|${ci}|${Math.round(u * 10)}|${blur}|${lit ? 1 : 0}|${ck(sc)}|${Math.round(s * 100)}`;
    let c = SPR.get(id); if (c) { SPR.delete(id); SPR.set(id, c); return c; }
    const g = brickGeo(n, u), pad = Math.ceil(blur * 3);
    c = document.createElement('canvas'); c.width = Math.ceil((g.w + pad * 2) * s); c.height = Math.ceil((g.h + pad * 2) * s);
    const x = c.getContext('2d'), col = ci >= 100 ? mix('#FFE9A8', T.hi, 0.2) : toyCol(ci, sc, T);
    if (blur > 0 && typeof x.filter === 'string') {
      const tmp = document.createElement('canvas'); tmp.width = c.width; tmp.height = c.height; const tx = tmp.getContext('2d'); tx.scale(s, s); tx.translate(pad, pad); paintBrick(tx, n, col, u, lit);
      x.filter = `blur(${blur * s}px)`; x.drawImage(tmp, 0, 0); x.filter = 'none'; tmp.width = tmp.height = 0;
    } else { x.scale(s, s); x.translate(pad, pad); paintBrick(x, n, col, u, lit); }
    c.pad = pad; c.g = g; SPR.set(id, c);
    while (SPR.size > 90) { const k = SPR.keys().next().value, o = SPR.get(k); o.width = o.height = 0; SPR.delete(k); }
    return c;
  };
  const splitRow = (w, rnd) => { const out = []; while (w > 0) { const o = [4, 3, 2].filter(l => l <= w && (w - l === 0 || w - l >= 2)); const l = o.length ? o[Math.floor(rnd() * o.length)] : w; out.push(l); w -= l; } return out; };
  /* the two little builds, as rows of [from, to, colour-role] in stud units about the centre line */
  const BUILDS = [
    { name: 'house', rows: [
      [[-7, -1, 'w'], [1, 7, 'w']],
      [[-7, -5, 'w'], [-5, -3, 'L'], [-3, -1, 'w'], [1, 3, 'w'], [3, 5, 'L'], [5, 7, 'w']],
      [[-7, -5, 'w'], [-5, -3, 'L'], [-3, 3, 'w'], [3, 5, 'L'], [5, 7, 'w']],
      [[-7, 7, 'w']],
      [[-8, 8, 'r']],
      [[-6, 4, 'r'], [4, 6, 'c']],
      [[-4, 4, 'r'], [4, 6, 'c']],
      [[-2, 2, 'r'], [4, 6, 'c']]] },
    { name: 'rocket', rows: [
      [[-5, -3, 'f'], [-3, 3, 'w'], [3, 5, 'f']],
      [[-5, -3, 'f'], [-3, 3, 'w'], [3, 5, 'f']],
      [[-3, 3, 'w']],
      [[-3, -1, 'w'], [-1, 1, 'L'], [1, 3, 'w']],
      [[-3, 3, 'b']],
      [[-3, 3, 'w']],
      [[-2, 2, 'n']],
      [[-1, 1, 'n']]] }
  ];

  /* ================= 煉瓦の壁 ================= */
  K.registerBg('echoBrickBuild', '煉瓦の壁', ['calm', 'emotional', 'glitch'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `bb|${seed}|${ck(sc)}`;
    const cyc = 30, cycN = Math.floor(t / cyc), p = frac(t / cyc), tl = t - cycN * cyc;
    const u = Math.min(U * (portrait ? 0.056 : 0.05), W * 0.92 / 17), gy = H * (portrait ? 0.76 : 0.86), cx0 = W * (portrait ? 0.5 : 0.5);
    const bi = (cycN + Math.floor(r(seed, 1, 1) * 2)) % BUILDS.length, B = BUILDS[bi], hue0 = Math.floor(r(seed, cycN, 2) * 8);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg|${Math.round(u)}`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.7, T.bg1], [1, mix(T.bg1, T.c1, T.dark ? 0.25 : 0.2)]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.82, H * 0.2, U * 0.9, T.c1, T.dark ? 0.32 : 0.4); glowDisc(x, W * 0.14, H * 0.55, U * 0.8, T.c2, T.dark ? 0.22 : 0.3);
      /* the wall behind is a giant studded plate, barely there */
      const pitch = U * 0.2; x.fillStyle = rgba(T.dark ? '#FFFFFF' : T.ink, T.dark ? 0.03 : 0.035);
      for (let yy = pitch * 0.5; yy < gy; yy += pitch) for (let xx = pitch * 0.5 + ((Math.round(yy / pitch) % 2) ? pitch * 0.5 : 0); xx < W + pitch; xx += pitch) { x.beginPath(); x.arc(xx, yy, pitch * 0.3, 0, TAU); x.fill(); }
      glowDisc(x, cx0, gy - u * 4, U * 0.8, T.hi, T.dark ? 0.1 : 0.16);
      /* floor + baseplate */
      const floorG = vertical(x, gy - u * 0.3, H, [[0, mix(T.bg1, T.c2, T.dark ? 0.18 : 0.12)], [1, mix(T.bg0, '#000000', T.dark ? 0.35 : 0.04)]]); x.fillStyle = floorG; x.fillRect(0, gy - u * 0.3, W, H - gy + u * 0.3);
      const pw = u * 21, px0 = cx0 - pw / 2, plate = toyCol(3, sc, T), pTop = gy - u * 0.34, pH = u * 0.62;
      x.fillStyle = rgba('#000000', T.dark ? 0.4 : 0.16); x.beginPath(); x.ellipse(cx0, gy + u * 0.95, pw * 0.58, u * 0.55, 0, 0, TAU); x.fill();
      x.fillStyle = mix(plate, '#000000', 0.24); x.fillRect(px0, gy + u * 0.34, pw, pH);
      x.fillStyle = mix(plate, '#FFFFFF', 0.28); x.beginPath(); x.moveTo(px0 + u * 0.8, pTop); x.lineTo(px0 + pw - u * 0.8, pTop); x.lineTo(px0 + pw, gy + u * 0.34); x.lineTo(px0, gy + u * 0.34); x.closePath(); x.fill();
      x.fillStyle = mix(plate, '#FFFFFF', 0.12); x.fillRect(px0, gy + u * 0.34, pw, u * 0.07);
      for (const [yy, sh] of [[gy - u * 0.06, 1], [gy + u * 0.22, 1]]) for (let k = 0; k < 21; k++) {
        const sx = px0 + (k + 0.5) * u; x.fillStyle = mix(plate, '#000000', 0.12); x.beginPath(); x.ellipse(sx, yy, u * 0.29, u * 0.13, 0, 0, TAU); x.fill(); x.fillRect(sx - u * 0.29, yy - u * 0.16, u * 0.58, u * 0.16);
        x.fillStyle = mix(plate, '#FFFFFF', 0.4); x.beginPath(); x.ellipse(sx, yy - u * 0.16, u * 0.29, u * 0.13, 0, 0, TAU); x.fill();
      }
    }), 0, 0, W, H);
    /* lay the build out: one brick per [segment piece], coloured by role */
    const lay = (() => {
      const rnd = (() => { let s = (seed * 9301 + cycN * 49297 + bi * 233) % 233280; return () => (s = (s * 9301 + 49297) % 233280) / 233280; })();
      const bricks = [], roles = { w: hue0, r: (hue0 + 3) % 8, c: (hue0 + 5) % 8, f: (hue0 + 4) % 8, b: (hue0 + 2) % 8, n: (hue0 + 3) % 8 };
      if (bi === 1) { roles.w = 7; roles.f = hue0; roles.b = (hue0 + 3) % 8; roles.n = hue0; }
      B.rows.forEach((row, ri) => { for (const [a, b, role] of row) { if (role === 'L') { bricks.push({ x: a, n: b - a, row: ri, ci: 100, lit: true, o: rnd() }); continue; } let x0 = a; for (const l of splitRow(b - a, rnd)) { bricks.push({ x: x0, n: l, row: ri, ci: roles[role], lit: false, o: rnd() }); x0 += l; } } });
      bricks.sort((A, Bb) => A.row - Bb.row || A.o - Bb.o); return bricks;
    })();
    const rowsN = B.rows.length, buildEnd = cyc * 0.5, launchT = cyc * 0.84, hold = 0.9;
    const nB = lay.length, dtB = (buildEnd - 1.2) / nB, FALL = 0.85;
    const flame = bi === 1 && tl > launchT, ls = Math.max(0, tl - launchT), rocketUp = bi === 1 ? -(ls * ls) * U * 0.22 : 0;
    const dust = [];
    /* glow of the lit windows (and the rocket's porthole) behind the bricks */
    const settled = tl > buildEnd + 0.5 && tl < launchT + 1.2;
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    lay.forEach((b, i) => {
      if (!b.lit) return; const ts = 0.6 + i * dtB; if (tl < ts + FALL) return;
      const wx = cx0 + (b.x + b.n / 2) * u, wy = gy - (b.row + 0.5) * u * 1.15 + rocketUp; const flick = 0.85 + 0.15 * Math.sin(t * 2.3 + i * 1.7);
      glowDisc(ctx, wx, wy, u * 3.6, '#FFD98A', (T.dark ? 0.5 : 0.32) * flick * (tl > launchT + 2.5 ? 0 : 1));
    });
    ctx.restore();
    lay.forEach((b, i) => {
      const ts = 0.6 + i * dtB, a = clamp((tl - ts) / FALL); if (a <= 0) return;
      const spr = brickSprite(env, b.n, b.ci, u, T, sc, 0, b.lit), g = spr.g, bx = cx0 + b.x * u - g.p, byB = gy - b.row * u * 1.15;
      let dy = -Math.pow(1 - a, 2) * U * 0.85, rot = (1 - a) * (b.o - 0.5) * 0.5, al = clamp(a * 6), sy = 1;
      if (a >= 1) { const lt = tl - ts - FALL; dy = 0; rot = 0; if (lt < 0.32) sy = 1 - 0.09 * Math.sin(lt / 0.32 * Math.PI) * (1 - lt / 0.32); }
      if (bi === 0 && tl > launchT) { const o = (rowsN - b.row - 1 + b.o * 0.6) / rowsN, s = clamp((tl - launchT - o * 2.2) / 1.5); dy += -ease(s) * ease(s) * U * 0.75; rot += s * (b.o - 0.5) * 1.6; al *= 1 - clamp((s - 0.35) / 0.65); }
      else if (bi === 1) dy += rocketUp;
      if (al <= 0.01) return;
      const pad = spr.pad, bodyBottom = byB;  /* sprite y: bottom of front face sits at byB */
      ctx.save(); ctx.globalAlpha = e * al; ctx.translate(bx + g.p + b.n * u / 2, bodyBottom + dy);
      if (rot) ctx.rotate(rot); if (sy !== 1) ctx.scale(1 + (1 - sy) * 0.4, sy);
      ctx.drawImage(spr, -b.n * u / 2 - g.p - pad, -(g.fy + g.bodyH) - pad, g.w + pad * 2, g.h + pad * 2); ctx.restore();
      if (a >= 1) { const lt = tl - ts - FALL; if (lt < 0.55 && !(bi === 1 && tl > launchT)) dust.push([bx + g.p + b.n * u / 2, bodyBottom, lt / 0.55, b.n]); }
    });
    /* landing puffs */
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (const [dx, dyy, a, n] of dust) { ctx.fillStyle = rgba(T.hi, 0.5 * (1 - a)); for (let k = -1; k <= 1; k += 2) dot(ctx, dx + k * n * u * (0.35 + 0.35 * a), dyy - u * 0.1 - a * u * 0.25, u * (0.07 + 0.1 * a)); }
    /* house: chimney smoke · rocket: exhaust · both: floating sparkles */
    if (bi === 0 && tl > buildEnd) for (let i = 0; i < 6; i++) { const age = frac(t * 0.14 + i / 6), px = cx0 + 5 * u + Math.sin(age * 5 + i) * u * 0.5 + age * u * 1.4, py = gy - 8 * u * 1.15 - age * u * 4.5; ctx.fillStyle = rgba(T.dark ? mix('#FFFFFF', sc.accent, 0.1) : T.ink, (T.dark ? 0.22 : 0.18) * Math.sin(age * Math.PI)); dot(ctx, px, py, u * (0.35 + 0.8 * age)); }
    if (bi === 1 && flame) {
      const fy = gy - u * 0.2 + rocketUp, fl = 0.85 + 0.15 * Math.sin(t * 30);
      glowDisc(ctx, cx0, fy + u * 1.2, u * 3.2 * fl, '#FFC35A', 0.8); glowDisc(ctx, cx0, fy + u * 0.8, u * 1.6 * fl, '#FFFFFF', 0.7);
      for (let i = 0; i < 12; i++) { const age = clamp((ls - i * 0.12) / 2.2); if (age <= 0 || age >= 1) continue; ctx.fillStyle = rgba(T.dark ? '#FFFFFF' : T.ink, (T.dark ? 0.3 : 0.2) * (1 - age)); dot(ctx, cx0 + Math.sin(i * 2.7) * u * (1 + age * 3), gy + u * 0.3 - age * u * 0.8, u * (0.8 + 2.2 * age)); }
    }
    for (let i = 0; i < 26; i++) { const age = frac(t * (0.03 + 0.04 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(mix(TOY[i % 8], '#FFFFFF', 0.4), (T.dark ? 0.55 : 0.5) * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73) + Math.sin(t * 0.4 + i) * U * 0.02, H * (0.98 - age * 0.95), U * (0.003 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });

  /* ================= 漂う煉瓦 ================= */
  K.registerBg('echoBrickScatter', '無重力の煉瓦', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `bs|${seed}|${ck(sc)}`;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * (portrait ? 0.7 : 0.78), H * 0.28, U * 1.0, T.c1, T.dark ? 0.4 : 0.5); glowDisc(x, W * 0.2, H * 0.78, U * 0.85, T.c2, T.dark ? 0.28 : 0.38);
      glowDisc(x, W * 0.5, H * 0.5, U * 0.5, T.hi, T.dark ? 0.1 : 0.18);
      if (T.dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.18 + 0.55 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * W, r(seed, i, 2) * H, U * (0.0012 + 0.002 * r(seed, i, 4)), 0, TAU); x.fill(); }
      /* a few airy rings, like the orbits of something far away */
      x.strokeStyle = rgba(T.dark ? T.c2 : T.c1, T.dark ? 0.22 : 0.3); x.lineWidth = Math.max(1.2, U * 0.003);
      for (const k of [0.55, 0.85, 1.25]) { x.beginPath(); x.ellipse(W * 0.5, H * 0.52, U * k * 1.2, U * k * 0.34, -0.3, 0, TAU); x.stroke(); }
    }), 0, 0, W, H);
    const N = portrait ? 16 : 22, classes = [{ u: 0.032, z: 0.45, blur: 2.2, al: 0.8, drift: 0.005 }, { u: 0.05, z: 0.7, blur: 0, al: 1, drift: 0.009 }, { u: 0.072, z: 1, blur: 0, al: 1, drift: 0.014 }, { u: 0.13, z: 1.5, blur: 7, al: 0.75, drift: 0.024 }];
    const draws = [];
    for (let i = 0; i < N; i++) {
      const ci = i < N * 0.36 ? 0 : i < N * 0.72 ? 1 : i < N - 2 ? 2 : 3, cl = classes[ci], uu = U * cl.u, n = [2, 2, 3, 4, 4, 1, 2][Math.floor(r(seed, i, 5) * 7)], col = Math.floor(r(seed, i, 6) * 8), ph = r(seed, i, 7) * TAU;
      const x0 = wrap(r(seed, i, 2) * (W + uu * 8) + t * U * cl.drift * (i % 2 ? 1 : -1), W + uu * 12) - uu * 6, y0 = H * (0.06 + 0.88 * r(seed, i, 3)) + Math.sin(t * 0.35 + ph) * U * 0.03 * cl.z;
      const rot = (r(seed, i, 4) - 0.5) * 1.0 + Math.sin(t * (0.18 + 0.1 * r(seed, i, 8)) + ph) * 0.35;
      draws.push({ ci, uu, n, col, x0, y0, rot, cl });
    }
    for (const d of draws) {
      const spr = brickSprite(env, d.n, d.col, d.uu, T, sc, d.cl.blur * U / 540, false), g = spr.g;
      ctx.save(); ctx.globalAlpha = e * d.cl.al; ctx.translate(d.x0, d.y0); ctx.rotate(d.rot);
      if (d.cl.z < 1 || T.dark) { ctx.globalCompositeOperation = 'source-over'; }
      ctx.drawImage(spr, -g.w / 2 - spr.pad, -g.h / 2 - spr.pad, g.w + spr.pad * 2, g.h + spr.pad * 2); ctx.restore();
    }
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 16; i++) { const age = frac(t * (0.025 + 0.03 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(mix(TOY[i % 8], '#FFFFFF', 0.5), 0.6 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), H * (1.02 - age * 1.04), U * (0.003 + 0.004 * r(seed, i, 74))); }
    ctx.restore();
  });

  /* ================= 探照灯 ================= */
  K.registerBg('echoSearchlight', 'サーチライト', ['calm', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `sl|${seed}|${ck(sc)}`;
    const wy = H * (portrait ? 0.8 : 0.76), cloudY = H * 0.3;
    const towers = [[W * (portrait ? 0.16 : 0.13), H * 0.55, 1], [W * (portrait ? 0.84 : 0.87), H * 0.57, 1], [W * 0.5, H * 0.66, 0.6]];
    const sil = T.dark ? mix(sc.bg, '#000000', 0.92) : mix(sc.bg, sc.fg, 0.72);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.5, T.bg1], [0.78, mix(T.bg1, T.c1, T.dark ? 0.5 : 0.45)], [1, mix(T.bg1, T.c1, T.dark ? 0.35 : 0.35)]]); x.fillRect(0, 0, W, H);
      if (T.dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.15 + 0.5 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * W, r(seed, i, 2) * cloudY * 1.2, U * (0.0012 + 0.002 * r(seed, i, 4)), 0, TAU); x.fill(); }
      /* a bank of cloud the beams can strike */
      for (let i = 0; i < 22; i++) { const px = W * (r(seed, i, 11) * 1.2 - 0.1), py = cloudY + U * 0.09 * (r(seed, i, 12) - 0.5), rad = U * (0.14 + 0.2 * r(seed, i, 13)); x.save(); x.translate(px, py); x.scale(2.4, 0.5); glowDisc(x, 0, 0, rad, T.dark ? mix(sc.bg, T.c2, 0.28) : mix(sc.bg, '#FFFFFF', 0.5), T.dark ? 0.55 : 0.6); x.restore(); }
      /* the distant wall: a long low line with a few lit windows in the towers */
      x.fillStyle = sil; x.fillRect(0, wy, W, H - wy);
      x.strokeStyle = rgba(T.line, T.dark ? 0.1 : 0.15); x.lineWidth = 1; const bh = U * 0.045; x.beginPath(); for (let yy = wy + bh; yy < H; yy += bh) { x.moveTo(0, yy); x.lineTo(W, yy); } x.stroke();
      for (const [tx, ty, sz] of towers) {
        const tw = U * 0.05 * sz, top = ty; x.fillStyle = sil;
        x.fillRect(tx - tw * 0.55, top + U * 0.05 * sz, tw * 1.1, wy - top - U * 0.05 * sz + 2);
        x.beginPath(); x.moveTo(tx - tw * 1.15, top + U * 0.05 * sz); x.lineTo(tx + tw * 1.15, top + U * 0.05 * sz); x.lineTo(tx + tw * 0.95, top); x.lineTo(tx - tw * 0.95, top); x.closePath(); x.fill();
        x.beginPath(); x.moveTo(tx - tw * 1.2, top); x.lineTo(tx, top - U * 0.035 * sz); x.lineTo(tx + tw * 1.2, top); x.closePath(); x.fill();
        x.fillStyle = rgba(T.hi, T.dark ? 0.75 : 0.5); x.fillRect(tx - tw * 0.6, top + U * 0.012 * sz, tw * 1.2, U * 0.018 * sz);
      }
      x.strokeStyle = sil; x.lineWidth = Math.max(1.5, U * 0.003); x.beginPath(); for (let px = 0; px < W; px += U * 0.05) { x.moveTo(px, wy); x.lineTo(px + U * 0.008, wy - U * 0.05); } x.moveTo(0, wy - U * 0.038); x.lineTo(W, wy - U * 0.038); x.moveTo(0, wy - U * 0.016); x.lineTo(W, wy - U * 0.016); x.stroke();
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    towers.slice(0, 3).forEach(([tx, ty, sz], k) => {
      const a = -Math.PI / 2 + Math.sin(t * (0.28 + 0.05 * k) + k * 2.4) * 0.62 * (k % 2 ? -1 : 1) + (k === 0 ? 0.3 : k === 1 ? -0.3 : 0), L = Math.hypot(W, H) * 1.1, ox = tx, oy = ty + U * 0.02 * sz, col = k % 2 ? T.c2 : T.c1;
      /* soft beam: stacked cones, widest and faintest first */
      [[0.075, 0.1], [0.055, 0.14], [0.038, 0.2], [0.022, 0.32]].forEach(([hw, al]) => { const g = ctx.createLinearGradient(ox, oy, ox + Math.cos(a) * L, oy + Math.sin(a) * L); g.addColorStop(0, rgba(mix(col, '#FFFFFF', 0.5), (T.dark ? al * 1.5 : al * 1.1) * sz)); g.addColorStop(0.8, rgba(col, (T.dark ? al * 0.5 : al * 0.5) * sz)); g.addColorStop(1, rgba(col, 0)); ctx.fillStyle = g; ctx.beginPath(); ctx.moveTo(ox, oy); ctx.lineTo(ox + Math.cos(a - hw) * L, oy + Math.sin(a - hw) * L); ctx.lineTo(ox + Math.cos(a + hw) * L, oy + Math.sin(a + hw) * L); ctx.fill(); });
      glow(ctx, T, ox, oy, U * 0.13 * sz, T.hi, 0.85);
      /* where the beam meets the cloud a pale patch blooms */
      const d = (cloudY - oy) / Math.sin(a); if (d > 0 && d < L) { const hx = ox + Math.cos(a) * d; ctx.save(); ctx.translate(hx, cloudY); ctx.scale(1.8, 0.5); glowDisc(ctx, 0, 0, U * 0.24 * sz, mix(col, '#FFFFFF', 0.5), T.dark ? 0.55 : 0.5); ctx.restore(); }
    });
    /* haze along the ground and dust drifting in the light */
    for (let i = 0; i < 7; i++) { const x = wrap(r(seed, i, 1) * W * 1.4 + t * U * (0.008 + 0.01 * r(seed, i, 2)), W * 1.4) - W * 0.2, y = wy - U * 0.02 - U * 0.04 * r(seed, i, 3), rad = U * (0.3 + 0.3 * r(seed, i, 4)); ctx.save(); ctx.translate(x, y); ctx.scale(2.4, 0.3); glowDisc(ctx, 0, 0, rad, T.dark ? mix(T.c1, '#FFFFFF', 0.3) : '#FFFFFF', T.dark ? 0.13 : 0.3); ctx.restore(); }
    for (let i = 0; i < 36; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 71)) + r(seed, i, 72)); ctx.fillStyle = rgba(T.hi, 0.5 * Math.sin(age * Math.PI)); dot(ctx, W * r(seed, i, 73), wy * (1 - age * 0.85), U * (0.0015 + 0.003 * r(seed, i, 74))); }
    ctx.restore();
  });

  K.registerFront('echoAshFall', '灰', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones(sc);
    ctx.globalAlpha = e; ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let i = 0; i < 30; i++) { const age = frac(t * (0.02 + 0.03 * r(seed, i, 1)) + r(seed, i, 2)), x = W * r(seed, i, 3) + Math.sin(t * 0.6 + i * 2) * U * 0.03, y = H * (-0.03 + age * 1.06); ctx.fillStyle = rgba(i % 6 ? (T.dark ? '#FFFFFF' : T.ink) : T.c1, (i % 6 ? 0.28 : 0.6) * Math.sin(age * Math.PI)); ctx.save(); ctx.translate(x, y); ctx.rotate(t * 0.5 + i); ctx.fillRect(-U * 0.004, -U * 0.0016, U * (0.006 + 0.006 * r(seed, i, 4)), U * 0.0032); ctx.restore(); }
    ctx.restore();
    if (K.luxe) K.luxe(ctx, env, { seed, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.35, bokeh: 1, glitter: 6 });
  });

  /* ================= オイルライト ================= */
  let OIL = null;
  K.registerBg('echoOilLight', 'オイルライト', ['pop', 'emotional', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W;
    const cw = Math.round(clamp(W / 5, 90, 220)), chh = Math.max(40, Math.round(cw * H / W));
    if (!OIL || OIL.width !== cw || OIL.height !== chh) { OIL = document.createElement('canvas'); OIL.width = cw; OIL.height = chh; OIL.g = OIL.getContext('2d'); OIL.img = OIL.g.createImageData(cw, chh); }
    const hx = c => { const v = parseInt(c.slice(1, 7), 16); return [v >> 16 & 255, v >> 8 & 255, v & 255]; };
    const pal = [T.c1, mix(SPEC[0], sc.accent, 0.45), T.c2, mix(SPEC[2], sc.accent, 0.35), mix(SPEC[4], sc.accent2, 0.4), mix(SPEC[6], sc.accent2, 0.4)].map(hx), NP = pal.length;
    const dk = T.dark, base = hx(dk ? mix(sc.bg, '#000000', 0.6) : mix(sc.bg, '#FFFFFF', 0.5)), D = OIL.img.data, sx = (portrait ? 5.2 : 6.6) / cw, sp = t * 0.16, ph = seed * 1.7;
    for (let y = 0; y < chh; y++) {
      const v = y * sx * (portrait ? 1 : 1);
      for (let x = 0; x < cw; x++) {
        const u = x * sx;
        /* domain-warped field: slow swirls that fold over on themselves */
        const wu = u + 0.9 * Math.sin(v * 1.7 + sp * 1.3 + ph) + 0.5 * Math.sin(v * 3.1 - sp * 2 + u), wv = v + 0.9 * Math.sin(u * 1.5 - sp * 1.1 + ph * 2) + 0.5 * Math.sin(u * 2.9 + sp * 1.7 + v);
        const f = Math.sin(wu * 1.6 + Math.sin(wv * 1.3 + sp)) + Math.sin(wv * 1.5 - Math.sin(wu * 1.2 - sp * 0.8)) * 0.8 + Math.sin((wu + wv) * 0.9 + sp * 0.6) * 0.6;
        const idx = (f * 1.05 + 3.2 + sp * 0.5) % NP, i0 = Math.floor(idx < 0 ? idx + NP : idx) % NP, fr = idx - Math.floor(idx), c0 = pal[i0], c1 = pal[(i0 + 1) % NP], sm = fr * fr * (3 - 2 * fr);
        /* thin dark membranes where the bands meet, as in a pan of oil and dye */
        const wall = Math.pow(Math.abs(Math.sin(f * 2.6)), 10) * (dk ? 0.85 : 0.45), lit = 0.55 + 0.45 * Math.abs(Math.sin(f * 1.3 + 0.7));
        const i = (y * cw + x) * 4;
        for (let k = 0; k < 3; k++) { const c = (c0[k] + (c1[k] - c0[k]) * sm) * (dk ? lit : 0.75 + 0.25 * lit); D[i + k] = c * (1 - wall) + base[k] * wall; }
        D[i + 3] = 255;
      }
    }
    OIL.g.putImageData(OIL.img, 0, 0);
    ctx.globalAlpha = e; ctx.imageSmoothingEnabled = true; ctx.drawImage(OIL, 0, 0, W, H);
    /* the projector's round pool of light, a cool lens sheen and a dark surround */
    const cx = W * 0.5, cy = H * 0.5, R = Math.hypot(W, H) * 0.5;
    const vg = ctx.createRadialGradient(cx, cy, R * 0.45, cx, cy, R * 1.0); vg.addColorStop(0, rgba(dk ? '#000000' : '#FFFFFF', 0)); vg.addColorStop(1, rgba(dk ? '#000000' : mix(sc.bg, sc.fg, 0.55), dk ? 0.72 : 0.4)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = dk ? 'screen' : 'source-over'; glowDisc(ctx, cx - W * 0.12, cy - H * 0.15, U * 0.7, '#FFFFFF', dk ? 0.1 : 0.2);
    ctx.strokeStyle = rgba('#FFFFFF', dk ? 0.16 : 0.3); ctx.lineWidth = Math.max(1, U * 0.0022); for (const k of [0.62, 0.86]) { ctx.beginPath(); ctx.arc(cx, cy, R * k, 0, TAU); ctx.stroke(); } ctx.restore();
  });

  /* ================= レーザードーム ================= */
  K.registerBg('echoLaserDome', 'レーザードーム', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `ld|${seed}|${ck(sc)}`;
    const ox = W * 0.5, oy = H * 1.04;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, ox, oy, U * 1.3, T.c1, T.dark ? 0.32 : 0.3); glowDisc(x, ox, H * 0.35, U * 0.8, T.c2, T.dark ? 0.1 : 0.16);
      if (T.dark) for (let i = 0; i < 90; i++) { x.fillStyle = rgba('#FFFFFF', 0.12 + 0.4 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * W, r(seed, i, 2) * H * 0.8, U * (0.0012 + 0.0018 * r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; ctx.lineCap = 'round';
    const pal = [T.c1, T.c2, mix(SPEC[0], sc.accent, 0.4), mix(SPEC[4], sc.accent2, 0.4), mix(SPEC[3], sc.accent, 0.3)];
    /* turning laser cones: beams leave one point and meet a rotating ellipse, so each reads as a cone in perspective */
    const cone = (k, col, dist, wide, n, spin, lean) => {
      const rot = t * spin, cxr = ox + lean * dist, cyr = oy - dist, rx = dist * wide, ry = rx * 0.17;
      for (const [lw, la] of T.dark ? [[U * 0.012, 0.05], [Math.max(1.2, U * 0.0016), 0.6]] : [[Math.max(1.5, U * 0.0022), 0.5]]) {
        ctx.strokeStyle = rgba(col, la); ctx.lineWidth = lw; ctx.beginPath();
        for (let i = 0; i < n; i++) { const a = rot + i / n * TAU, ex = cxr + Math.cos(a) * rx, ey = cyr + Math.sin(a) * ry; ctx.moveTo(ox, oy); ctx.lineTo(ex, ey); } ctx.stroke();
      }
      for (const [lw, la] of T.dark ? [[U * 0.01, 0.12], [Math.max(1.5, U * 0.0028), 0.9]] : [[Math.max(1.5, U * 0.003), 0.75]]) { ctx.strokeStyle = rgba(col, la); ctx.lineWidth = lw; ctx.beginPath(); ctx.ellipse(cxr, cyr, rx, ry, 0, 0, TAU); ctx.stroke(); }
      ctx.fillStyle = rgba(T.hi, 0.9); for (let i = 0; i < n; i += 2) { const a = rot + i / n * TAU; dot(ctx, cxr + Math.cos(a) * rx, cyr + Math.sin(a) * ry, U * 0.004); }
    };
    const D0 = H * (portrait ? 0.72 : 0.8), br = 0.5 + 0.5 * Math.sin(t * 0.35);
    cone(0, pal[0], D0, portrait ? 0.42 : 0.5, 40, 0.5, Math.sin(t * 0.2) * 0.12);
    cone(1, pal[1], D0 * 0.62, portrait ? 0.3 : 0.34, 28, -0.7, -Math.sin(t * 0.17 + 1) * 0.16);
    cone(2, pal[2], D0 * (0.9 + 0.25 * br), portrait ? 0.62 : 0.72, 22, 0.32, Math.sin(t * 0.13 + 2) * 0.2);
    /* smoke catching the light near the source, and rings hung in the air */
    ctx.strokeStyle = rgba(T.c3, 0.45); ctx.lineWidth = Math.max(1, U * 0.002);
    for (let i = 0; i < 5; i++) { const q = frac(t * 0.05 + i / 5), d = H * (0.1 + 0.85 * q); ctx.globalAlpha = e * Math.sin(q * Math.PI); ctx.beginPath(); ctx.ellipse(ox, oy - d, d * 0.55, d * 0.09, 0, 0, TAU); ctx.stroke(); }
    ctx.globalAlpha = e; glow(ctx, T, ox, oy, U * 0.2, T.hi, 0.9);
    ctx.restore();
  });

  /* ================= 日食 ================= */
  K.registerBg('echoEclipse', '皆既日食', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `ec|${seed}|${ck(sc)}`;
    const cx = W * (portrait ? 0.5 : 0.6), cy = H * (portrait ? 0.4 : 0.46), R = U * (portrait ? 0.2 : 0.19);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      if (T.dark) for (let i = 0; i < 120; i++) { x.fillStyle = rgba('#FFFFFF', 0.12 + 0.55 * r(seed, i, 3)); x.beginPath(); x.arc(r(seed, i, 1) * W, r(seed, i, 2) * H, U * (0.0012 + 0.002 * r(seed, i, 4)), 0, TAU); x.fill(); }
    }), 0, 0, W, H);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    /* the corona: a smooth halo, then long soft streamers and fine hair, all fading with one radial gradient */
    glowDisc(ctx, cx, cy, R * 5, T.c1, T.dark ? 0.32 : 0.28); glowDisc(ctx, cx, cy, R * 2.4, T.hi, T.dark ? 0.5 : 0.36); glowDisc(ctx, cx, cy, R * 1.5, '#FFFFFF', T.dark ? 0.6 : 0.4);
    const cg = ctx.createRadialGradient(cx, cy, R, cx, cy, R * 4.4); cg.addColorStop(0, rgba(mix(T.hi, '#FFFFFF', 0.4), T.dark ? 0.42 : 0.4)); cg.addColorStop(0.3, rgba(T.hi, T.dark ? 0.16 : 0.18)); cg.addColorStop(1, rgba(T.c2, 0));
    ctx.fillStyle = cg;
    for (let i = 0; i < 26; i++) { /* streamers: broad, tapering, lopsided like the real thing */
      const a = i / 26 * TAU + (r(seed, i, 1) - 0.5) * 0.4 + Math.sin(t * 0.08 + i) * 0.03, len = R * (0.9 + 2.9 * Math.pow(r(seed, i, 2), 1.8)) * (1 + 0.06 * Math.sin(t * 0.5 + i * 2)), hw = 0.05 + 0.07 * r(seed, i, 3);
      ctx.beginPath(); ctx.moveTo(cx + Math.cos(a - hw) * R, cy + Math.sin(a - hw) * R); ctx.quadraticCurveTo(cx + Math.cos(a - hw * 0.35) * (R + len * 0.55), cy + Math.sin(a - hw * 0.35) * (R + len * 0.55), cx + Math.cos(a) * (R + len), cy + Math.sin(a) * (R + len)); ctx.quadraticCurveTo(cx + Math.cos(a + hw * 0.35) * (R + len * 0.55), cy + Math.sin(a + hw * 0.35) * (R + len * 0.55), cx + Math.cos(a + hw) * R, cy + Math.sin(a + hw) * R); ctx.fill();
    }
    ctx.lineCap = 'round';
    for (let i = 0; i < 120; i++) { const a = r(seed, i, 21) * TAU, len = R * (0.9 + 1.9 * Math.pow(r(seed, i, 22), 1.5)); ctx.strokeStyle = rgba(T.hi, (T.dark ? 0.18 : 0.22) + 0.15 * r(seed, i, 23)); ctx.lineWidth = Math.max(1, U * 0.0015); ctx.beginPath(); ctx.moveTo(cx + Math.cos(a) * R * 1.02, cy + Math.sin(a) * R * 1.02); ctx.lineTo(cx + Math.cos(a + 0.02 * Math.sin(t * 0.3 + i)) * (R + len), cy + Math.sin(a + 0.02 * Math.sin(t * 0.3 + i)) * (R + len)); ctx.stroke(); }
    /* three thin rings of colour breathing outward, each a hue of the scheme */
    const hc = [T.c1, T.c2, T.c3];
    for (let i = 0; i < 3; i++) { const age = frac(t / 16 + i / 3), rr = R * (2.2 + age * 5.2); ctx.strokeStyle = rgba(hc[i], 0.5 * Math.sin(Math.PI * age)); ctx.lineWidth = Math.max(1.2, U * 0.003); ctx.beginPath(); ctx.arc(cx, cy, rr, 0, TAU); ctx.stroke(); ctx.fillStyle = rgba(hc[i], 0.8 * Math.sin(Math.PI * age)); dot(ctx, cx + Math.cos(t * 0.1 + i * 2.1) * rr, cy + Math.sin(t * 0.1 + i * 2.1) * rr, U * 0.005); }
    ctx.restore();
    /* the moon and its diamond ring */
    ctx.fillStyle = T.dark ? '#000000' : mix(sc.fg, '#000000', 0.5); ctx.beginPath(); ctx.arc(cx, cy, R, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.55 : 0.8); ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.beginPath(); ctx.arc(cx, cy, R, 0, TAU); ctx.stroke();
    const ba = -0.9 + t * 0.1, bx = cx + Math.cos(ba) * R, by = cy + Math.sin(ba) * R, pulse = 0.8 + 0.2 * Math.sin(t * 1.4);
    ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, bx, by, U * 0.13 * pulse, '#FFFFFF', T.dark ? 0.95 : 0.8);
    ctx.strokeStyle = rgba('#FFFFFF', 0.9 * pulse); ctx.lineWidth = Math.max(1.5, U * 0.0028); ctx.beginPath(); ctx.moveTo(bx - U * 0.11, by); ctx.lineTo(bx + U * 0.11, by); ctx.moveTo(bx, by - U * 0.07); ctx.lineTo(bx, by + U * 0.07); ctx.stroke(); ctx.restore();
  });

  K.registerFront('echoPsyLeak', '虹の光', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones(sc);
    ctx.globalAlpha = e; ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over';
    for (let k = 0; k < 2; k++) { const x0 = k ? W * 1.02 : -W * 0.02, dir = k ? -1 : 1, g = ctx.createLinearGradient(x0, 0, x0 + dir * W * 0.4, 0); const sh = frac(t / 22 + k * 0.5); [SPEC[0], SPEC[2], SPEC[4], SPEC[6]].forEach((c, i) => g.addColorStop(i / 3, rgba(mix(c, sc.accent, 0.35), (T.dark ? 0.14 : 0.16) * (0.5 + 0.5 * Math.sin(sh * TAU + i))))); g.addColorStop(1, rgba(sc.accent, 0)); ctx.fillStyle = g; ctx.fillRect(Math.min(x0, x0 + dir * W * 0.4), 0, W * 0.4, H); }
    for (let i = 0; i < 6; i++) { const age = frac(t * 0.04 + i / 6), x = W * r(seed, i, 1), y = H * (0.15 + 0.7 * r(seed, i, 2)); ctx.strokeStyle = rgba(SPEC[(i * 2) % 7], 0.35 * Math.sin(age * Math.PI)); ctx.lineWidth = Math.max(1, U * 0.003); ctx.beginPath(); ctx.arc(x, y, U * (0.02 + 0.06 * age), 0, TAU); ctx.stroke(); }
    ctx.restore();
    if (K.luxe) K.luxe(ctx, env, { seed, tint: ['#FFFFFF', sc.accent, sc.accent2], strength: 0.4, bokeh: 2, glitter: 8 });
  });
})();
