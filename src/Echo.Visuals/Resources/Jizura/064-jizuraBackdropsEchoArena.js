/* ECHO first-party backdrops for the ICPC and ブロック(voxel-world) looks: スコアボード, バルーン, 判定, ボクセル, ブロック洞窟, ブロックの空 + a cube-drift front.
   Original artwork only: no game textures, mobs or logos. Every colour comes from the current scheme. */
(() => {
  'use strict';
  const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
  const { U: Umin, isDark, wrap, fade, vertical, glowDisc, layer } = K;
  const ck = sc => [sc.bg, sc.fg, sc.accent, sc.accent2, sc.ghostA, sc.ghostB].join('');
  const frac = v => v - Math.floor(v);
  const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
  const dot = (ctx, x, y, rad) => { ctx.beginPath(); ctx.arc(x, y, rad, 0, TAU); ctx.fill(); };
  const MONO = 'ui-monospace,"SF Mono",Menlo,Consolas,"Courier New",monospace';
  const SERIF = 'Georgia,"Times New Roman",serif';
  const tones = sc => {
    const dark = isDark(sc.bg);
    return {
      dark, bg0: dark ? mix(sc.bg, '#000000', 0.68) : mix(sc.bg, '#FFFFFF', 0.6), bg1: dark ? mix(sc.bg, '#000000', 0.4) : sc.bg,
      c1: dark ? mix(sc.accent, '#FFFFFF', 0.1) : mix(sc.accent, '#000000', 0.06), c2: dark ? mix(sc.accent2, '#FFFFFF', 0.08) : mix(sc.accent2, '#000000', 0.04), c3: sc.ghostA || sc.accent,
      hi: mix(sc.accent, '#FFFFFF', dark ? 0.62 : 0.7), line: dark ? mix(sc.fg, sc.bg, 0.3) : mix(sc.fg, '#000000', 0.2), ink: dark ? '#000000' : mix(sc.fg, '#000000', 0.2),
      steel: dark ? mix(sc.bg, sc.fg, 0.18) : mix(sc.bg, sc.fg, 0.1), steelHi: dark ? mix(sc.bg, sc.fg, 0.42) : mix(sc.bg, '#FFFFFF', 0.6), hot: mix('#FF4D3D', sc.accent, 0.22),
    };
  };
  const glow = (ctx, T, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, T.dark ? a : a * 0.7); ctx.restore(); };
  const add = (ctx, T) => { ctx.globalCompositeOperation = T.dark ? 'screen' : 'source-over'; };
  const rr = (ctx, x, y, w, h, rad) => { ctx.beginPath(); ctx.roundRect(x, y, w, h, rad); };
  const beat = (t, bpm) => { const p = frac(t * bpm / 60); return Math.pow(Math.max(0, 1 - p * 2.3), 2); };
  /* a believable little spectrum: the kick lives low, other parts move about above it */
  const band = (t, i, n) => {
    const x = i / Math.max(1, n - 1), k = beat(t, 120) * Math.pow(1 - x, 1.5), sn = beat(t + 0.25, 120) * (0.3 + 0.7 * Math.sin(x * 3.1)), mid = 0.5 + 0.5 * Math.sin(t * (1.1 + x * 3.2) + i * 2.1), hat = beat(t * 2 + 0.1, 120) * x * 0.6;
    return clamp(0.06 + k * 0.85 + sn * 0.3 + 0.3 * mid * (1 - 0.55 * x) * (0.35 + 0.65 * beat(t + 0.12, 60)) + hat + 0.05 * Math.sin(t * 7 + i));
  };

  const tones2 = sc => { const T = tones(sc); T.c4 = sc.ghostB || sc.accent2; T.warm = mix('#F2A65A', sc.accent, 0.2); T.fg = T.dark ? sc.fg : mix(sc.fg, '#000000', 0.1); T.dim = mix(sc.fg, sc.bg, T.dark ? 0.55 : 0.5); return T; };
  /* tall/odd-sized static canvases (scrolling strips) that the kit's frame-sized layer() cannot make */
  const STRIPS = new Map();
  const strip = (env, key, wd, hd, res, draw) => {
    const q = (env.scale || 1) * res, w = Math.max(8, Math.ceil(wd * q)), h = Math.max(8, Math.ceil(hd * q)), id = `${key}|${w}x${h}`;
    let c = STRIPS.get(id); if (c) { STRIPS.delete(id); STRIPS.set(id, c); return c; }
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h; const x = cv.getContext('2d'); x.setTransform(w / wd, 0, 0, h / hd, 0, 0);
    const meta = draw(x, wd, hd) || {}; c = { cv, meta }; STRIPS.set(id, c);
    while (STRIPS.size > 8) { const o = STRIPS.keys().next().value, oc = STRIPS.get(o); oc.cv.width = oc.cv.height = 0; STRIPS.delete(o); }
    return c;
  };
  const pickr = (seed, i, k, arr) => arr[Math.floor(r(seed, i, k) * arr.length) % arr.length];

  const BASECOL = ['#E5484D', '#3B82F6', '#22A45D', '#F5B400', '#8E5BD9', '#F97316', '#14B8A6', '#EC4899', '#84CC16', '#06B6D4'];
  const PCOL = (T, sc) => BASECOL.map(c => mix(T.dark ? mix(c, '#FFFFFF', 0.06) : mix(c, '#000000', 0.04), sc.accent, 0.08));
  const fmtTime = s => { s = Math.max(0, Math.floor(s)); const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), ss = s % 60; return `${h}:${String(m).padStart(2, '0')}:${String(ss).padStart(2, '0')}`; };
  const TEAMS = ['Alpha', 'Bison', 'Cobalt', 'Delta', 'Ember', 'Falcon', 'Garnet', 'Helix', 'Ivory', 'Jasper', 'Kestrel', 'Lumen'];
  const SCHOOLS = ['North University', 'Harbor Institute', 'Summit College', 'Lakeside Tech', 'Ridge University', 'Meridian Academy', 'Coastal Polytechnic', 'Highland U.', 'Riverside Institute', 'Orchard College', 'Atlas Tech', 'Crescent University'];

  /* ================= スコアボード ================= */
  K.registerBg('echoScoreboard', 'スコアボード', ['glitch', 'graphic', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `sb|${seed}|${ck(sc)}`, q = env.scale || 1, pc = PCOL(T, sc);
    const nT = portrait ? 7 : 9, nP = portrait ? 4 : 8, top = H * (portrait ? 0.3 : 0.3), bot = H * 0.035, rh = (H - top - bot) / nT, fs = Math.min(rh * 0.42, U * 0.04);
    const mx = W * 0.04, cwR = W * (portrait ? 0.09 : 0.055), cwN = W * (portrait ? 0.3 : 0.2), cwS = W * (portrait ? 0.1 : 0.06), cwC = (W - mx * 2 - cwR - cwN - cwS) / nP;
    const perm = TEAMS.map((n, i) => [n, r(seed, i, 50)]).sort((a, b) => a[1] - b[1]).map(q2 => q2[0]);
    const teams = []; const minTm = new Array(nP).fill(1e9), probSolved = new Array(nP).fill(0);
    for (let i = 0; i < nT; i++) { const cells = []; let solved = 0, pen = 0; for (let j = 0; j < nP; j++) { const qq = r(seed, i * 20 + j, 60); if (qq < 0.42) { const tm = Math.floor(8 + r(seed, i * 20 + j, 61) * 270); cells.push({ st: 'ac', tm, tr: Math.floor(r(seed, i * 20 + j, 62) * 3) }); solved++; pen += tm; probSolved[j]++; if (tm < minTm[j]) minTm[j] = tm; } else if (qq < 0.54) cells.push({ st: 'wa', tr: 1 + Math.floor(r(seed, i * 20 + j, 63) * 4) }); else if (qq < 0.62) cells.push({ st: 'pd', tr: 1 }); else cells.push({ st: 'no' }); } teams.push({ name: perm[i % perm.length] + (i >= perm.length ? ' II' : ''), school: SCHOOLS[Math.floor(r(seed, i, 64) * SCHOOLS.length)], cells, solved, pen }); }
    teams.sort((a, b) => b.solved - a.solved || a.pen - b.pen);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg2`, 0.6, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      const hg = x.createLinearGradient(0, 0, 0, top - rh * 1.15); hg.addColorStop(0, rgba(T.c1, T.dark ? 0.1 : 0.12)); hg.addColorStop(1, rgba(T.c1, 0)); x.fillStyle = hg; x.fillRect(0, 0, W, top);
      x.font = `800 ${fs * 1.75}px ${MONO}`; x.textAlign = 'left'; x.fillStyle = T.fg; x.fillText('STANDINGS', mx, top * 0.32);
      x.font = `${fs * 0.82}px ${MONO}`; x.fillStyle = T.dim; x.fillText('Regional Final  ·  Day 2  ·  5 hours', mx, top * 0.32 + fs * 1.4);
      x.strokeStyle = rgba(T.line, 0.16); x.lineWidth = 1; x.beginPath(); x.moveTo(mx, top - rh * 0.02); x.lineTo(W - mx, top - rh * 0.02); x.stroke();
      for (let j = 0; j < nP; j++) { const cxp = mx + cwR + cwN + cwS + cwC * (j + 0.5), br = Math.min(cwC * 0.3, fs * 0.95); x.fillStyle = pc[j % pc.length]; rr(x, cxp - br, top - rh * 0.95, br * 2, br * 2, br * 0.55); x.fill(); x.fillStyle = 'rgba(255,255,255,0.96)'; x.font = `800 ${fs * 1.05}px ${MONO}`; x.textAlign = 'center'; x.fillText('ABCDEFGHIJ'[j], cxp, top - rh * 0.95 + br * 1.35); x.font = `${fs * 0.72}px ${MONO}`; x.textAlign = 'center'; x.fillStyle = T.dim; x.fillText(`${probSolved[j]}/${nT}`, cxp, top - rh * 0.06); }
      x.font = `${fs * 0.72}px ${MONO}`; x.fillStyle = T.dim; x.textAlign = 'left'; x.fillText('RANK', mx + cwR * 0.05, top - rh * 0.1); x.fillText('TEAM', mx + cwR, top - rh * 0.1); x.textAlign = 'center'; x.fillText('SOLVED', mx + cwR + cwN + cwS * 0.5, top - rh * 0.1);
    }), 0, 0, W, H);
    /* clock + elapsed bar with the frozen stretch hatched */
    const el = frac(t / 70) * 0.9 + 0.05, bx0 = W * (portrait ? 0.45 : 0.62), bx1 = W - mx, byy = top * 0.32 + fs * 0.9;
    ctx.textAlign = 'right'; ctx.font = `800 ${fs * 2.1}px ${MONO}`; ctx.fillStyle = T.fg; ctx.fillText(fmtTime((1 - el) * 5 * 3600), W - mx, top * 0.32);
    ctx.fillStyle = rgba(T.dim, 0.3); rr(ctx, bx0, byy, bx1 - bx0, fs * 0.28, fs * 0.14); ctx.fill();
    ctx.fillStyle = T.c1; rr(ctx, bx0, byy, Math.min(el, 0.8) * (bx1 - bx0), fs * 0.28, fs * 0.14); ctx.fill();
    if (el > 0.8) { ctx.fillStyle = T.c2; rr(ctx, bx0 + 0.8 * (bx1 - bx0), byy, (el - 0.8) * (bx1 - bx0), fs * 0.28, fs * 0.14); ctx.fill(); }
    ctx.strokeStyle = rgba(T.c2, 0.7); ctx.lineWidth = 1.2; ctx.beginPath(); ctx.moveTo(bx0 + 0.8 * (bx1 - bx0), byy - fs * 0.35); ctx.lineTo(bx0 + 0.8 * (bx1 - bx0), byy + fs * 0.65); ctx.stroke(); ctx.font = `${fs * 0.7}px ${MONO}`; ctx.fillStyle = T.c2; ctx.textAlign = 'center'; ctx.fillText('FROZEN', bx0 + 0.8 * (bx1 - bx0), byy + fs * 1.4);
    /* one team climbs rank by rank; everyone it passes slides down a place */
    const step = 0.85, D = (nT - 1) * step, Tc = D + 2.6, cyc = Math.floor(t / Tc), tl = t - cyc * Tc, pp = tl / step, ss = Math.floor(pp), ff = pp - ss;
    const qpos = tl >= D ? 0 : (nT - 1) - (ss + ease(ff)), climber = (((nT - 1 - cyc) % nT) + nT) % nT;
    for (const pass of [0, 1]) for (let i = 0; i < nT; i++) {
      if ((i === climber) !== (pass === 1)) continue;
      const b0 = (i + cyc) % nT; let rank, isC = i === climber; rank = isC ? qpos : b0 + clamp(b0 + 1 - qpos, 0, 1);
      const tm = teams[i], y = top + rank * rh, mvDown = !isC && rank - b0 > 0.04 && rank - b0 < 0.96;
      const S = strip(env, `${key}|row${i}`, W, rh, 0.9, (x) => {
        x.fillStyle = rgba(T.fg, T.dark ? 0.06 : 0.05); rr(x, mx * 0.5, rh * 0.07, W - mx, rh * 0.86, rh * 0.2); x.fill();
        x.textAlign = 'left'; x.font = `800 ${fs}px ${MONO}`; x.fillStyle = T.fg; x.fillText(tm.name, mx + cwR, rh * 0.5); x.font = `${fs * 0.6}px ${MONO}`; x.fillStyle = T.dim; x.fillText(tm.school, mx + cwR, rh * 0.5 + fs * 0.85);
        x.textAlign = 'center'; x.font = `800 ${fs * 1.1}px ${MONO}`; x.fillStyle = T.fg; x.fillText(String(tm.solved), mx + cwR + cwN + cwS * 0.36, rh * 0.5 + fs * 0.3); x.font = `${fs * 0.62}px ${MONO}`; x.fillStyle = T.dim; x.fillText(String(tm.pen), mx + cwR + cwN + cwS * 0.78, rh * 0.5 + fs * 0.25);
        tm.cells.forEach((c, j) => {
          const cxp = mx + cwR + cwN + cwS + cwC * (j + 0.5), bw = cwC * 0.88, bh = rh * 0.62, by2 = rh * 0.19, col = pc[j % pc.length];
          if (c.st === 'no') { x.strokeStyle = rgba(T.dim, 0.22); x.lineWidth = 1; rr(x, cxp - bw / 2, by2, bw, bh, 5); x.stroke(); return; }
          if (c.st === 'ac') { const g = x.createLinearGradient(0, by2, 0, by2 + bh); g.addColorStop(0, mix(col, '#FFFFFF', 0.22)); g.addColorStop(1, mix(col, '#000000', 0.12)); x.fillStyle = g; rr(x, cxp - bw / 2, by2, bw, bh, 5); x.fill(); if (c.tm === minTm[j]) { x.strokeStyle = 'rgba(255,255,255,0.9)'; x.lineWidth = 1.6; rr(x, cxp - bw / 2 + 1, by2 + 1, bw - 2, bh - 2, 4); x.stroke(); } }
          else if (c.st === 'wa') { x.fillStyle = rgba(T.hot, 0.2); rr(x, cxp - bw / 2, by2, bw, bh, 5); x.fill(); x.strokeStyle = rgba(T.hot, 0.7); x.lineWidth = 1.2; x.stroke(); }
          else { x.fillStyle = rgba(T.warm, 0.14); rr(x, cxp - bw / 2, by2, bw, bh, 5); x.fill(); x.strokeStyle = rgba(T.warm, 0.6); x.lineWidth = 1.2; x.setLineDash([3, 3]); x.stroke(); x.setLineDash([]); }
          x.textAlign = 'center'; x.font = `800 ${fs * 0.74}px ${MONO}`;
          if (c.st === 'ac') { x.fillStyle = 'rgba(255,255,255,0.95)'; x.fillText(`${Math.floor(c.tm / 60)}:${String(c.tm % 60).padStart(2, '0')}`, cxp, by2 + bh * 0.52); if (c.tr) { x.font = `${fs * 0.52}px ${MONO}`; x.fillStyle = 'rgba(255,255,255,0.75)'; x.fillText(`+${c.tr}`, cxp, by2 + bh * 0.86); } }
          else { x.fillStyle = c.st === 'wa' ? T.hot : T.warm; x.fillText(c.st === 'wa' ? `−${c.tr}` : '?', cxp, by2 + bh * 0.66); }
        });
        return {};
      });
      if (isC) { ctx.save(); ctx.shadowColor = 'rgba(0,0,0,0.55)'; ctx.shadowBlur = rh * 0.5; ctx.shadowOffsetY = rh * 0.12; ctx.fillStyle = rgba(T.bg1, 0.96); rr(ctx, mx * 0.5, y + rh * 0.07, W - mx, rh * 0.86, rh * 0.2); ctx.fill(); ctx.restore(); }
      ctx.drawImage(S.cv, 0, y, W, rh);
      if (isC) { ctx.save(); add(ctx, T); const a = 0.5 + 0.5 * Math.sin(t * 8); ctx.fillStyle = rgba(T.c1, 0.1 + 0.05 * a); ctx.fillRect(mx * 0.5, y + rh * 0.07, W - mx, rh * 0.86); ctx.fillStyle = rgba(T.hi, 0.9); ctx.fillRect(mx * 0.5, y + rh * 0.07, 3, rh * 0.86); glow(ctx, T, mx + cwR * 0.5, y + rh * 0.5, rh * 1.2, T.c1, 0.5); ctx.restore(); }
      else if (mvDown) { ctx.fillStyle = rgba(T.hot, 0.05); ctx.fillRect(mx * 0.5, y + rh * 0.07, W - mx, rh * 0.86); }
      tm.cells.forEach((c, j) => { if (c.st !== 'pd') return; const cxp = mx + cwR + cwN + cwS + cwC * (j + 0.5), bw = cwC * 0.88, bh = rh * 0.62; ctx.fillStyle = rgba(T.warm, 0.1 + 0.2 * (0.5 + 0.5 * Math.sin(t * 5 + i + j))); rr(ctx, cxp - bw / 2, y + rh * 0.19, bw, bh, 5); ctx.fill(); });
      const rk = Math.round(rank), medal = rk < 3 ? ['#F5C518', '#C6CDD6', '#CD8B4A'][rk] : null;
      if (medal) { ctx.fillStyle = medal; ctx.beginPath(); ctx.arc(mx + cwR * 0.38, y + rh * 0.5, fs * 0.78, 0, TAU); ctx.fill(); ctx.fillStyle = 'rgba(0,0,0,0.72)'; } else ctx.fillStyle = T.dim;
      ctx.font = `800 ${fs * 1.05}px ${MONO}`; ctx.textAlign = 'center'; ctx.fillText(String(rk + 1), mx + cwR * 0.38, y + rh * 0.5 + fs * 0.36);
      if (isC && tl < D) { ctx.fillStyle = T.c1; ctx.beginPath(); const ax2 = mx + cwR * 0.9, ay2 = y + rh * 0.5; ctx.moveTo(ax2, ay2 - fs * 0.45); ctx.lineTo(ax2 + fs * 0.4, ay2 + fs * 0.3); ctx.lineTo(ax2 - fs * 0.4, ay2 + fs * 0.3); ctx.closePath(); ctx.fill(); }
    }
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.5, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.4 : 0.1)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });

  /* ================= 判定 ================= */
  const VD = [['AC', 'Accepted', 0.38], ['WA', 'Wrong Answer', 0.3], ['TLE', 'Time Limit', 0.12], ['RE', 'Runtime Error', 0.08], ['CE', 'Compile Error', 0.05], ['PD', 'Pending', 0.07]];
  const vdPick = q => { let a = 0; for (const v of VD) { a += v[2]; if (q < a) return v; } return VD[0]; };
  K.registerBg('echoVerdicts', '判定', ['glitch', 'graphic', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `vd|${seed}|${ck(sc)}`, pc = PCOL(T, sc);
    const vcol = { AC: T.c1, WA: T.hot, TLE: T.warm, RE: T.c4, CE: T.dim, PD: T.hi };
    const fs = Math.max(11, U * (portrait ? 0.042 : 0.032)), rowH = fs * 2.5, nL = Math.max(24, Math.ceil(H * 1.6 / rowH)), Hd = nL * rowH, lx = portrait ? W * 0.05 : W * 0.52, lw = portrait ? W * 0.9 : W * 0.44, ly0 = portrait ? H * 0.42 : H * 0.07, lh0 = portrait ? H * 0.55 : H * 0.86;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba(T.line, 0.06); x.lineWidth = 1; x.beginPath(); for (let gx = 0; gx < W; gx += U * 0.08) { x.moveTo(gx, 0); x.lineTo(gx, H); } for (let gy = 0; gy < H; gy += U * 0.08) { x.moveTo(0, gy); x.lineTo(W, gy); } x.stroke();
      glowDisc(x, portrait ? W / 2 : W * 0.26, portrait ? H * 0.18 : H * 0.34, U * 0.7, T.c1, T.dark ? 0.1 : 0.12);
    }), 0, 0, W, H);
    const S = strip(env, `${key}|q${Math.round(fs)}`, lw, Hd, 0.95, (x) => {
      x.textBaseline = 'middle';
      for (let i = 0; i < nL; i++) {
        const v = vdPick(r(seed, 900 + i, 1)), y = i * rowH + rowH / 2, pr = Math.floor(r(seed, 900 + i, 2) * 8), col = vcol[v[0]], hh = rowH * 0.8;
        const g = x.createLinearGradient(0, 0, lw, 0); g.addColorStop(0, rgba(T.fg, T.dark ? 0.09 : 0.07)); g.addColorStop(1, rgba(T.fg, T.dark ? 0.03 : 0.03)); x.fillStyle = g; rr(x, 0, y - hh / 2, lw, hh, hh * 0.22); x.fill();
        x.fillStyle = col; rr(x, 0, y - hh / 2, fs * 0.3, hh, fs * 0.15); x.fill();
        x.font = `${fs * 0.8}px ${MONO}`; x.textAlign = 'left'; x.fillStyle = T.dim; x.fillText(`${String(Math.floor(r(seed, i, 3) * 4)).padStart(1, '0')}:${String(Math.floor(r(seed, i, 4) * 60)).padStart(2, '0')}:${String(Math.floor(r(seed, i, 5) * 60)).padStart(2, '0')}`, fs * 1.0, y);
        x.fillStyle = T.fg; x.font = `800 ${fs}px ${MONO}`; x.fillText(TEAMS[Math.floor(r(seed, 900 + i, 6) * TEAMS.length)], fs * 5.3, y);
        x.fillStyle = pc[pr % pc.length]; x.beginPath(); x.arc(lw * 0.5, y, fs * 0.72, 0, TAU); x.fill(); x.fillStyle = 'rgba(255,255,255,0.95)'; x.textAlign = 'center'; x.font = `800 ${fs * 0.85}px ${MONO}`; x.fillText('ABCDEFGH'[pr], lw * 0.5, y + 1);
        if (v[0] === 'AC' || v[0] === 'WA') { x.font = `${fs * 0.7}px ${MONO}`; x.fillStyle = T.dim; x.textAlign = 'right'; x.fillText(`${Math.floor(12 + r(seed, i, 7) * 380)} ms`, lw * 0.72, y); }
        const cw2 = fs * 4.6; x.fillStyle = rgba(col, 0.2); rr(x, lw - cw2 - fs * 0.7, y - hh * 0.3, cw2, hh * 0.6, hh * 0.3); x.fill(); x.strokeStyle = col; x.lineWidth = 1.2; x.stroke(); x.fillStyle = col; x.font = `800 ${fs * 0.82}px ${MONO}`; x.textAlign = 'center'; x.fillText(v[0], lw - cw2 / 2 - fs * 0.7, y);
      }
      return {};
    });
    const o = (t * rowH * 0.55) % Hd;
    ctx.save(); ctx.beginPath(); ctx.rect(lx, ly0, lw, lh0); ctx.clip(); ctx.drawImage(S.cv, lx, ly0 - o, lw, Hd); ctx.drawImage(S.cv, lx, ly0 + Hd - o, lw, Hd);
    const tg = ctx.createLinearGradient(0, ly0, 0, ly0 + lh0 * 0.2); tg.addColorStop(0, rgba(T.bg0, 1)); tg.addColorStop(1, rgba(T.bg0, 0)); ctx.fillStyle = tg; ctx.fillRect(lx, ly0, lw, lh0 * 0.2);
    const bg2 = ctx.createLinearGradient(0, ly0 + lh0 * 0.85, 0, ly0 + lh0); bg2.addColorStop(0, rgba(T.bg1, 0)); bg2.addColorStop(1, rgba(T.bg1, 1)); ctx.fillStyle = bg2; ctx.fillRect(lx, ly0 + lh0 * 0.85, lw, lh0 * 0.15); ctx.restore();
    /* ring clock */
    const cx0 = portrait ? W * 0.5 : W * 0.26, cy0 = portrait ? H * 0.18 : H * 0.34, R0 = U * (portrait ? 0.15 : 0.21), el = frac(t / 90) * 0.85 + 0.05;
    ctx.save(); ctx.lineCap = 'butt'; ctx.strokeStyle = rgba(T.dim, 0.35); ctx.lineWidth = Math.max(1, U * 0.003); for (let i = 0; i < 60; i++) { const a = i / 60 * TAU - Math.PI / 2, l = i % 5 ? R0 * 0.05 : R0 * 0.1; ctx.beginPath(); ctx.moveTo(cx0 + Math.cos(a) * R0, cy0 + Math.sin(a) * R0); ctx.lineTo(cx0 + Math.cos(a) * (R0 - l), cy0 + Math.sin(a) * (R0 - l)); ctx.stroke(); }
    ctx.lineWidth = Math.max(3, U * 0.012); ctx.strokeStyle = rgba(T.dim, 0.25); ctx.beginPath(); ctx.arc(cx0, cy0, R0 * 0.86, 0, TAU); ctx.stroke(); ctx.lineCap = 'round'; ctx.strokeStyle = T.c1; ctx.beginPath(); ctx.arc(cx0, cy0, R0 * 0.86, -Math.PI / 2, -Math.PI / 2 + el * TAU); ctx.stroke();
    ctx.save(); add(ctx, T); const ta = -Math.PI / 2 + el * TAU; glow(ctx, T, cx0 + Math.cos(ta) * R0 * 0.86, cy0 + Math.sin(ta) * R0 * 0.86, U * 0.06, T.hi, 0.9); ctx.restore(); ctx.restore();
    ctx.textAlign = 'center'; ctx.font = `800 ${R0 * 0.36}px ${MONO}`; ctx.fillStyle = T.fg; ctx.fillText(fmtTime((1 - el) * 5 * 3600), cx0, cy0 + R0 * 0.1); ctx.font = `${fs * 0.85}px ${MONO}`; ctx.fillStyle = T.dim; ctx.fillText('TIME REMAINING', cx0, cy0 - R0 * 0.3);
    /* judging tally */
    if (!portrait) {
      const rows = [['AC', 0.62], ['WA', 0.9], ['TLE', 0.33], ['RE', 0.2]], sx = W * 0.07, sw = W * 0.38, sy0 = H * 0.76, gap = H * 0.055;
      rows.forEach(([nm, w], i) => { const y = sy0 + i * gap, cnt = Math.floor(30 + w * 120 + t * (0.6 + w * 0.8)), fill = clamp((cnt % 400) / 260 * 0.6 + w * 0.4); ctx.textAlign = 'left'; ctx.font = `800 ${fs * 0.9}px ${MONO}`; ctx.fillStyle = vcol[nm]; ctx.fillText(nm, sx, y + fs * 0.3); ctx.fillStyle = rgba(T.dim, 0.22); rr(ctx, sx + fs * 3.4, y - fs * 0.25, sw - fs * 8, fs * 0.55, fs * 0.28); ctx.fill(); ctx.fillStyle = vcol[nm]; rr(ctx, sx + fs * 3.4, y - fs * 0.25, (sw - fs * 8) * fill, fs * 0.55, fs * 0.28); ctx.fill(); ctx.textAlign = 'right'; ctx.fillStyle = T.fg; ctx.fillText(String(cnt), sx + sw, y + fs * 0.3); });
    }
    /* verdict stamp, kept clear of the ring */
    const per = 1.8, k = Math.floor(t / per), p = t / per - k, v = vdPick(r(seed, 5000 + k, 1)), col = vcol[v[0]], sc2 = 1 + 0.7 * (1 - ease(clamp(p / 0.12))), al = clamp(p / 0.05) * (1 - ease(clamp((p - 0.62) / 0.3)));
    const sx0 = cx0, sy1 = portrait ? H * 0.335 : H * 0.615;
    ctx.save(); ctx.translate(sx0, sy1); ctx.rotate(-0.08 + (r(seed, k, 9) - 0.5) * 0.06); ctx.scale(sc2, sc2); ctx.globalAlpha = e * al;
    ctx.font = `900 ${U * (portrait ? 0.06 : 0.058)}px "Arial Black",Impact,sans-serif`; const label = v[1].toUpperCase(), tw = ctx.measureText(label).width, bh = U * (portrait ? 0.09 : 0.088);
    ctx.strokeStyle = col; ctx.lineWidth = Math.max(3, U * 0.009); rr(ctx, -tw / 2 - U * 0.03, -bh / 2, tw + U * 0.06, bh, U * 0.015); ctx.stroke(); ctx.lineWidth = Math.max(1.2, U * 0.003); rr(ctx, -tw / 2 - U * 0.045, -bh / 2 - U * 0.015, tw + U * 0.09, bh + U * 0.03, U * 0.02); ctx.stroke();
    ctx.fillStyle = col; ctx.textBaseline = 'middle'; ctx.fillText(label, 0, U * 0.004); ctx.restore(); ctx.globalAlpha = e;
    if (al > 0.02) { ctx.save(); add(ctx, T); glow(ctx, T, sx0, sy1, U * 0.4, col, 0.3 * al * (1 - p)); ctx.restore(); }
  });

  /* ---- tiny syntax painter shared by the code panels ---- */
  const KWS = new Set(['for', 'while', 'if', 'else', 'auto', 'int', 'long', 'continue', 'return', 'const', 'bool', 'vector', 'struct', 'using', 'namespace']);
  const paintCode = (x, T, lines, px, py, fs, lh, active, cw) => {
    x.textBaseline = 'alphabetic'; x.textAlign = 'left'; x.font = `${fs}px ${MONO}`;
    lines.forEach((ln, i) => {
      const y = py + i * lh; if (active && active.has(i)) { x.fillStyle = rgba(T.c1, 0.16); x.fillRect(px - cw * 0.6, y - lh * 0.72, cw * (ln.length + 2) + cw * 3, lh); x.fillStyle = rgba(T.hi, 0.9); x.fillRect(px - cw * 0.6, y - lh * 0.72, 2.5, lh); }
      let cx = px; const toks = ln.match(/[A-Za-z_]\w*|\d+|\s+|./g) || [];
      toks.forEach((tk, k) => { let col = mix(T.fg, T.dim, 0.35); if (KWS.has(tk)) col = T.c4; else if (/^\d+$/.test(tk)) col = T.warm; else if (/^[A-Za-z_]/.test(tk)) col = (toks[k + 1] === '(' ? T.c2 : T.fg); else if (tk.trim() === '') col = T.fg; x.fillStyle = col; x.fillText(tk, cx, y); cx += tk.length * cw; });
    });
  };
  const PANEL = (x, T, sc, px, py, pw, ph, r0, title, fs) => { x.fillStyle = T.dark ? rgba(mix(sc.bg, '#000000', 0.5), 0.85) : rgba(mix(sc.bg, '#FFFFFF', 0.6), 0.9); rr(x, px, py, pw, ph, r0); x.fill(); x.strokeStyle = rgba(T.line, 0.25); x.lineWidth = 1; x.stroke(); if (title) { x.font = `800 ${fs * 0.72}px ${MONO}`; x.textAlign = 'left'; x.fillStyle = T.dim; x.fillText(title, px + fs * 0.8, py + fs * 1.3); } };

  /* ================= 題面 ================= */
  const PROBS = [
    { id: 'D', title: 'Shortest Detour', meta: 'time limit 2 s  ·  memory 256 MB', body: ['A delivery robot drives through a city of n intersections joined by m two-way roads. Driving along road i takes w_i seconds.', 'The robot may ignore the delay of at most one road. Find the least total time to get from intersection 1 to intersection n.'], inp: 'The first line has n and m (2 ≤ n ≤ 2·10^5, 1 ≤ m ≤ 4·10^5). Each of the next m lines holds u, v and w (1 ≤ w ≤ 10^9).', out: 'Print one integer: the minimum time, or −1 if intersection n cannot be reached.', si: ['4 4', '1 2 5', '2 3 4', '3 4 2', '1 4 12'], so: ['11'] },
    { id: 'F', title: 'Balanced Windows', meta: 'time limit 1 s  ·  memory 512 MB', body: ['You are given a string s of length n made of the characters ( and ). A window is a substring that is a correct bracket sequence.', 'Answer q queries: for each range [l, r], count the windows lying completely inside it.'], inp: 'The first line has n and q (1 ≤ n, q ≤ 3·10^5). The second line is s. Each of the next q lines holds l and r.', out: 'For every query print the number of windows on its own line.', si: ['6 2', '(())()', '1 6', '2 5'], so: ['3', '1'] },
    { id: 'B', title: 'Coin Towers', meta: 'time limit 3 s  ·  memory 256 MB', body: ['There are n towers, the i-th made of a_i coins. In one move you take the top coin of any tower and place it on a different tower.', 'What is the minimum number of moves needed to make all towers the same height, or is it impossible?'], inp: 'The first line has n (1 ≤ n ≤ 10^5). The second line has a_1, …, a_n (0 ≤ a_i ≤ 10^9).', out: 'Print the minimum number of moves, or −1 if it cannot be done.', si: ['3', '4 1 1'], so: ['2'] }];
  K.registerBg('echoProblemSheet', '題面', ['calm', 'editorial', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `ps|${seed}|${ck(sc)}`, pc = PCOL(T, sc), pb = PROBS[seed % PROBS.length];
    const pw = portrait ? W * 0.9 : Math.min(W * 0.78, U * 1.6), px = (W - pw) / 2, py = H * 0.05, ph = H * 0.9, fs = Math.max(11, Math.min(U * (portrait ? 0.036 : 0.03), pw * 0.032));
    ctx.globalAlpha = e;
    const S = strip(env, `${key}|${Math.round(fs)}|${Math.round(pw)}`, W, H, 0.95, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W * 0.5, H * 0.4, U * 0.9, T.c1, T.dark ? 0.08 : 0.1);
      x.save(); x.shadowColor = 'rgba(0,0,0,0.45)'; x.shadowBlur = U * 0.06; x.shadowOffsetY = U * 0.02; x.fillStyle = T.dark ? mix(sc.bg, '#FFFFFF', 0.05) : mix(sc.bg, '#FFFFFF', 0.8); rr(x, px, py, pw, ph, U * 0.014); x.fill(); x.restore();
      x.strokeStyle = rgba(T.line, 0.25); x.lineWidth = 1; rr(x, px, py, pw, ph, U * 0.014); x.stroke();
      const lines = []; let cy = py + fs * 1.6; const ix = px + fs * 2.2, iw = pw - fs * 4.4;
      const bx = pc[seed % pc.length]; x.fillStyle = bx; rr(x, ix, cy - fs * 0.4, fs * 3, fs * 3, fs * 0.55); x.fill(); x.fillStyle = 'rgba(255,255,255,0.96)'; x.font = `800 ${fs * 2}px ${MONO}`; x.textAlign = 'center'; x.fillText(pb.id, ix + fs * 1.5, cy + fs * 1.85);
      x.textAlign = 'left'; x.fillStyle = T.fg; x.font = `700 ${fs * 1.9}px ${SERIF}`; x.fillText(pb.title, ix + fs * 3.9, cy + fs * 0.9); x.font = `${fs * 0.82}px ${MONO}`; x.fillStyle = T.dim; x.fillText(portrait ? pb.meta : pb.meta + '  ·  standard input / output', ix + fs * 3.9, cy + fs * 2.3);
      cy += fs * 4.4; x.strokeStyle = rgba(T.line, 0.3); x.beginPath(); x.moveTo(ix, cy - fs); x.lineTo(ix + iw, cy - fs); x.stroke();
      const para = (txt, ital) => { x.font = `${ital ? 'italic ' : ''}${fs}px ${SERIF}`; x.fillStyle = mix(T.fg, T.dim, 0.12); const words = txt.split(' '); let ln = ''; for (const w of words) { const test = ln ? ln + ' ' + w : w; if (x.measureText(test).width > iw && ln) { x.fillText(ln, ix, cy); lines.push({ x: ix, y: cy - fs * 0.85, w: x.measureText(ln).width, h: fs * 1.15 }); cy += fs * 1.55; ln = w; } else ln = test; } if (ln) { x.fillText(ln, ix, cy); lines.push({ x: ix, y: cy - fs * 0.85, w: x.measureText(ln).width, h: fs * 1.15 }); cy += fs * 1.55; } cy += fs * 0.5; };
      pb.body.forEach(b => para(b));
      const head = (h) => { x.font = `800 ${fs * 0.95}px ${MONO}`; x.fillStyle = T.c1; x.fillText(h.toUpperCase(), ix, cy); cy += fs * 1.5; };
      head('Input'); para(pb.inp); head('Output'); para(pb.out);
      /* samples side by side */
      head('Examples'); const bw = (iw - fs) / 2, bh = Math.max(pb.si.length, pb.so.length) * fs * 1.45 + fs * 2.2;
      [['Input', pb.si], ['Output', pb.so]].forEach(([nm, arr], k) => { const bxx = ix + k * (bw + fs); x.fillStyle = rgba(T.fg, T.dark ? 0.06 : 0.05); rr(x, bxx, cy - fs * 0.6, bw, bh, fs * 0.5); x.fill(); x.font = `800 ${fs * 0.7}px ${MONO}`; x.fillStyle = T.dim; x.fillText(nm.toUpperCase(), bxx + fs * 0.8, cy + fs * 0.35); x.font = `${fs * 0.95}px ${MONO}`; arr.forEach((l, i) => { x.fillStyle = rgba(T.dim, 0.6); x.fillText(String(i + 1), bxx + fs * 0.8, cy + fs * 1.9 + i * fs * 1.45); x.fillStyle = T.fg; x.fillText(l, bxx + fs * 2.6, cy + fs * 1.9 + i * fs * 1.45); }); });
      return { lines, endY: cy + bh };
    });
    ctx.drawImage(S.cv, 0, 0, W, H);
    const L = S.meta.lines; if (L.length) {
      const per = 1.7, k = Math.floor(t / per) % L.length, p = clamp((t / per - Math.floor(t / per)) * 1.6), ln = L[k];
      ctx.fillStyle = rgba(T.warm, T.dark ? 0.3 : 0.4); ctx.fillRect(ln.x - 3, ln.y, (ln.w + 6) * ease(p), ln.h);
      ctx.fillStyle = rgba(T.hi, 0.9); ctx.fillRect(ln.x - 3, ln.y + ln.h, (ln.w + 6) * ease(p), Math.max(1.5, U * 0.003));
    }
    /* stopwatch chip */
    ctx.save(); ctx.textAlign = 'right'; ctx.font = `800 ${fs * 1.1}px ${MONO}`; const tx = px + pw - fs * 2.2, ty = py + fs * 2.2; ctx.fillStyle = rgba(T.c1, 0.16); rr(ctx, tx - fs * 6.4, ty - fs * 1.15, fs * 6.4, fs * 1.8, fs * 0.9); ctx.fill(); ctx.fillStyle = T.c1; ctx.fillText(fmtTime(t * 23 + 1900).slice(2), tx - fs * 0.8, ty + fs * 0.1); ctx.restore();
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.5, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.45 : 0.1)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });

  /* ================= グラフ探索 ================= */
  const DJ_CODE = ['while (!pq.empty()) {', '  auto [d, u] = pq.top(); pq.pop();', '  if (d > dist[u]) continue;', '  for (auto [v, w] : g[u])', '    if (dist[u] + w < dist[v]) {', '      dist[v] = dist[u] + w;', '      pq.push({dist[v], v});', '    }', '}'];
  const DJ = new Map();
  const djPlan = (seed) => {
    if (DJ.has(seed)) return DJ.get(seed);
    const cols = 4, rows = 3, N = cols * rows, nodes = [], edges = [];
    for (let i = 0; i < N; i++) { const cx = i % cols, cy = Math.floor(i / cols); nodes.push([(cx + 0.5 + (r(seed, i, 1) - 0.5) * 0.45) / cols, (cy + 0.5 + (r(seed, i, 2) - 0.5) * 0.45) / rows]); }
    const add = (a, b) => edges.push({ a, b, w: 1 + Math.floor(r(seed, a * 20 + b, 3) * 9) });
    for (let i = 0; i < N; i++) { const cx = i % cols, cy = Math.floor(i / cols); if (cx + 1 < cols) add(i, i + 1); if (cy + 1 < rows) add(i, i + cols); if (cx + 1 < cols && cy + 1 < rows && r(seed, i, 4) < 0.35) add(i, i + cols + 1); if (cx > 0 && cy + 1 < rows && r(seed, i, 5) < 0.25) add(i, i + cols - 1); }
    const adj = Array.from({ length: N }, () => []); edges.forEach((e2, ei) => { adj[e2.a].push([e2.b, e2.w, ei]); adj[e2.b].push([e2.a, e2.w, ei]); });
    const dist = new Array(N).fill(1e9), par = new Array(N).fill(-1), done = new Array(N).fill(false), pq = [[0, 0]]; dist[0] = 0; const ev = [];
    while (pq.length) { pq.sort((x, y) => x[0] - y[0] || x[1] - y[1]); const [d, u] = pq.shift(); if (d > dist[u] || done[u]) continue; done[u] = true; ev.push({ k: 'pop', u, d, pq: pq.slice() });
      for (const [v, w, ei] of adj[u]) { const nd = d + w, ok = nd < dist[v]; if (ok) { dist[v] = nd; par[v] = u; pq.push([nd, v]); } ev.push({ k: 'relax', u, v, w, ok, ei, nd, pq: pq.slice() }); } }
    const plan = { N, nodes, edges, ev, dist: dist.slice(), par: par.slice() }; DJ.set(seed, plan); if (DJ.size > 8) DJ.delete(DJ.keys().next().value); return plan;
  };
  K.registerBg('echoGraphSearch', 'グラフ探索', ['glitch', 'graphic', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `gs|${seed}|${ck(sc)}`;
    const pl = djPlan(seed % 97), gx0 = W * (portrait ? 0.08 : 0.06), gy0 = H * (portrait ? 0.08 : 0.14), gw = W * (portrait ? 0.84 : 0.52), gh = H * (portrait ? 0.4 : 0.72), nr = U * (portrait ? 0.034 : 0.036), fs = Math.max(10, U * (portrait ? 0.03 : 0.026));
    const stepT = 0.62, total = pl.ev.length * stepT, hold = 3.2, cyc = total + hold, tc = t % cyc, kk = Math.min(pl.ev.length - 1, Math.floor(tc / stepT)), ph = tc < total ? (tc / stepT - kk) : 1, finished = tc >= total;
    const NP = pl.nodes.map(([a, b]) => [gx0 + a * gw, gy0 + b * gh]);
    /* replay events up to the current one */
    const dist = new Array(pl.N).fill(null), done = new Array(pl.N).fill(false), par = new Array(pl.N).fill(-1), seen = new Array(pl.N).fill(false); dist[0] = 0; seen[0] = true; let cur = pl.ev[kk], curPop = -1;
    for (let i = 0; i <= kk; i++) { const ev = pl.ev[i]; if (ev.k === 'pop') { done[ev.u] = true; curPop = ev.u; } else if (ev.ok) { dist[ev.v] = ev.nd; par[ev.v] = ev.u; seen[ev.v] = true; } }
    if (finished) { cur = pl.ev[pl.ev.length - 1]; }
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => { x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H); x.strokeStyle = rgba(T.line, 0.06); x.lineWidth = 1; x.beginPath(); for (let gx = 0; gx < W; gx += U * 0.08) { x.moveTo(gx, 0); x.lineTo(gx, H); } for (let gy = 0; gy < H; gy += U * 0.08) { x.moveTo(0, gy); x.lineTo(W, gy); } x.stroke(); }), 0, 0, W, H);
    /* edges */
    ctx.lineCap = 'round';
    pl.edges.forEach((ed, ei) => {
      const [ax, ay] = NP[ed.a], [bx, by] = NP[ed.b], inTree = par[ed.b] === ed.a && done[ed.b] || par[ed.a] === ed.b && done[ed.a], isCur = cur && cur.k === 'relax' && cur.ei === ei && !finished;
      ctx.strokeStyle = inTree ? T.c1 : rgba(T.dim, 0.38); ctx.lineWidth = inTree ? Math.max(3, U * 0.008) : Math.max(1.5, U * 0.0035); ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke();
      if (inTree) { ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(T.c1, 0.3); ctx.lineWidth = Math.max(8, U * 0.02); ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); ctx.restore(); }
      if (isCur) { const from = NP[cur.u], to = NP[cur.v], pp = ease(clamp(ph * 1.4)); ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(cur.ok ? T.hi : T.hot, 0.95); ctx.lineWidth = Math.max(3, U * 0.009); ctx.beginPath(); ctx.moveTo(from[0], from[1]); ctx.lineTo(from[0] + (to[0] - from[0]) * pp, from[1] + (to[1] - from[1]) * pp); ctx.stroke(); ctx.restore(); }
      const mx2 = (ax + bx) / 2, my2 = (ay + by) / 2; ctx.font = `800 ${fs * 0.85}px ${MONO}`; ctx.textAlign = 'center'; const wl = fs * 1.35; ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.5) : mix(sc.bg, '#FFFFFF', 0.7); rr(ctx, mx2 - wl / 2, my2 - fs * 0.6, wl, fs * 1.2, fs * 0.5); ctx.fill(); ctx.fillStyle = isCur ? T.hi : T.dim; ctx.fillText(String(ed.w), mx2, my2 + fs * 0.3);
    });
    /* final shortest path to the far corner */
    if (finished) { let v = pl.N - 1; ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(T.hi, 0.7 + 0.3 * Math.sin(t * 6)); ctx.lineWidth = Math.max(4, U * 0.011); ctx.beginPath(); ctx.moveTo(NP[v][0], NP[v][1]); let guard = 0; while (pl.par[v] >= 0 && guard++ < 30) { v = pl.par[v]; ctx.lineTo(NP[v][0], NP[v][1]); } ctx.stroke(); ctx.restore(); }
    /* nodes */
    NP.forEach(([x0, y0], i) => {
      const isCurN = i === curPop && !finished, front = seen[i] && !done[i];
      ctx.save(); if (isCurN || done[i]) { add(ctx, T); glow(ctx, T, x0, y0, nr * (isCurN ? 3.4 : 2), isCurN ? T.hi : T.c1, isCurN ? 0.7 : 0.3); } ctx.restore();
      ctx.fillStyle = done[i] ? T.c1 : (T.dark ? mix(sc.bg, '#000000', 0.55) : mix(sc.bg, '#FFFFFF', 0.7)); dot(ctx, x0, y0, nr);
      ctx.strokeStyle = isCurN ? T.hi : front ? T.c2 : rgba(T.dim, 0.7); ctx.lineWidth = Math.max(2, U * (front || isCurN ? 0.006 : 0.004)) * (front ? 1 + 0.25 * Math.sin(t * 8 + i) : 1); ctx.beginPath(); ctx.arc(x0, y0, nr, 0, TAU); ctx.stroke();
      ctx.font = `800 ${fs * 1.05}px ${MONO}`; ctx.textAlign = 'center'; ctx.fillStyle = done[i] ? (T.dark ? '#000000' : '#FFFFFF') : T.fg; ctx.fillText(i === 0 ? 'S' : dist[i] === null ? '∞' : String(dist[i]), x0, y0 + fs * 0.36);
      if (i === pl.N - 1) { ctx.font = `800 ${fs * 0.8}px ${MONO}`; ctx.fillStyle = T.warm; ctx.fillText('T', x0 + nr * 1.15, y0 - nr * 0.9); }
    });
    /* code + queue panels */
    const cw = fs * 0.6, px = portrait ? W * 0.06 : W * 0.62, py = portrait ? H * 0.56 : H * 0.14, pw = portrait ? W * 0.88 : W * 0.34, ph2 = portrait ? H * 0.38 : H * 0.42;
    const act = new Set(); if (cur && !finished) { if (cur.k === 'pop') { act.add(0); act.add(1); act.add(2); } else { act.add(3); act.add(4); if (cur.ok) { act.add(5); act.add(6); } } }
    PANEL(ctx, T, sc, px, py, pw, ph2, fs * 0.6, 'dijkstra.cpp', fs);
    paintCode(ctx, T, DJ_CODE, px + fs * 1.2, py + fs * 3, Math.min(fs * 0.9, pw / 40), Math.min(fs * 1.5, ph2 / 11), act, Math.min(fs * 0.9, pw / 40) * 0.6);
    if (!portrait) {
      const qy = py + ph2 + fs * 1.2, qh = H * 0.28; PANEL(ctx, T, sc, px, qy, pw, qh, fs * 0.6, 'priority_queue<pair<int,int>>', fs);
      const q0 = cur ? cur.pq : []; ctx.font = `800 ${fs}px ${MONO}`; ctx.textAlign = 'left';
      for (let i = 0; i < 5; i++) { const y = qy + fs * 2.9 + i * fs * 1.5, it = q0[i]; ctx.fillStyle = rgba(T.fg, it ? (i === 0 ? 0.14 : 0.07) : 0.03); rr(ctx, px + fs * 0.8, y - fs * 1, pw - fs * 1.6, fs * 1.3, fs * 0.4); ctx.fill(); if (it) { ctx.fillStyle = i === 0 ? T.hi : T.fg; ctx.fillText(`(${it[0]}, ${it[1]})`, px + fs * 1.6, y); } }
    }
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.5, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.4 : 0.1)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });

  /* ================= DP表 ================= */
  const DP_CODE = ['for (int i = 1; i <= n; i++)', '  for (int j = 1; j <= m; j++)', '    dp[i][j] = a[i-1] == b[j-1]', '      ? dp[i-1][j-1] + 1', '      : max(dp[i-1][j], dp[i][j-1]);'];
  const WORDS = [['SEGMENT', 'ELEMENT'], ['TREAP', 'REPEAT'], ['DIJKSTRA', 'DYNAMIC'], ['MATRIX', 'MAXIMUM']];
  K.registerBg('echoDPTable', 'DP表', ['calm', 'graphic', 'glitch'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `dp|${seed}|${ck(sc)}`;
    const [A, B] = WORDS[seed % WORDS.length], n = A.length, m = B.length;
    const dp = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0)); for (let i = 1; i <= n; i++) for (let j = 1; j <= m; j++) dp[i][j] = A[i - 1] === B[j - 1] ? dp[i - 1][j - 1] + 1 : Math.max(dp[i - 1][j], dp[i][j - 1]);
    const path = []; { let i = n, j = m; while (i > 0 && j > 0) { if (A[i - 1] === B[j - 1]) { path.push([i, j]); i--; j--; } else if (dp[i - 1][j] >= dp[i][j - 1]) i--; else j--; } path.reverse(); }
    const tblW = W * (portrait ? 0.9 : 0.56), tblH = H * (portrait ? 0.46 : 0.68), cs = Math.min(tblW / (m + 1), tblH / (n + 1)), tx0 = W * (portrait ? 0.05 : 0.05) + (tblW - cs * (m + 1)) / 2, ty0 = H * (portrait ? 0.06 : 0.11), fs = Math.max(10, cs * 0.44);
    const stepT = 0.11, fillT = n * m * stepT, backT = path.length * 0.35, hold = 2.6, cyc = fillT + backT + hold, tc = t % cyc, filled = Math.min(n * m, Math.floor(tc / stepT)), backP = tc > fillT ? (tc - fillT) / 0.35 : -1;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => { x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H); x.strokeStyle = rgba(T.line, 0.06); x.lineWidth = 1; x.beginPath(); for (let gx = 0; gx < W; gx += U * 0.08) { x.moveTo(gx, 0); x.lineTo(gx, H); } for (let gy = 0; gy < H; gy += U * 0.08) { x.moveTo(0, gy); x.lineTo(W, gy); } x.stroke(); }), 0, 0, W, H);
    const maxV = dp[n][m] || 1; ctx.textAlign = 'center';
    /* headers */
    ctx.font = `800 ${fs * 1.1}px ${MONO}`; for (let j = 0; j < m; j++) { ctx.fillStyle = T.c2; ctx.fillText(B[j], tx0 + cs * (j + 1.5), ty0 + cs * 0.62); } for (let i = 0; i < n; i++) { ctx.fillStyle = T.c4; ctx.fillText(A[i], tx0 + cs * 0.5, ty0 + cs * (i + 1.62)); }
    const curIdx = tc < fillT ? filled : -1;
    for (let i = 1; i <= n; i++) for (let j = 1; j <= m; j++) {
      const idx = (i - 1) * m + (j - 1), x0 = tx0 + cs * j, y0 = ty0 + cs * i, isDone = idx < filled, isCur = idx === curIdx, match = A[i - 1] === B[j - 1];
      ctx.fillStyle = isDone ? rgba(mix(T.bg1, T.c1, 0.15 + 0.7 * dp[i][j] / maxV), T.dark ? 0.9 : 0.85) : rgba(T.fg, T.dark ? 0.05 : 0.05); rr(ctx, x0 + cs * 0.05, y0 + cs * 0.05, cs * 0.9, cs * 0.9, cs * 0.14); ctx.fill();
      if (isDone && match) { ctx.strokeStyle = T.c2; ctx.lineWidth = Math.max(1.5, cs * 0.05); rr(ctx, x0 + cs * 0.05, y0 + cs * 0.05, cs * 0.9, cs * 0.9, cs * 0.14); ctx.stroke(); }
      if (isDone) { ctx.fillStyle = T.fg; ctx.font = `800 ${fs}px ${MONO}`; ctx.fillText(String(dp[i][j]), x0 + cs * 0.5, y0 + cs * 0.5 + fs * 0.34); ctx.fillStyle = rgba(T.hi, 0.85); const ax = x0 + cs * 0.17, ay = y0 + cs * 0.24, as = cs * 0.09; ctx.beginPath(); if (match) { ctx.moveTo(ax - as, ay - as); ctx.lineTo(ax + as * 1.2, ay - as * 0.2); ctx.lineTo(ax - as * 0.2, ay + as * 1.2); } else if (dp[i - 1][j] >= dp[i][j - 1]) { ctx.moveTo(ax, ay - as * 1.1); ctx.lineTo(ax + as, ay + as * 0.7); ctx.lineTo(ax - as, ay + as * 0.7); } else { ctx.moveTo(ax - as * 1.1, ay); ctx.lineTo(ax + as * 0.7, ay - as); ctx.lineTo(ax + as * 0.7, ay + as); } ctx.closePath(); ctx.fill(); }
      if (isCur) { ctx.save(); add(ctx, T); glow(ctx, T, x0 + cs * 0.5, y0 + cs * 0.5, cs * 1.4, T.hi, 0.6); ctx.strokeStyle = T.hi; ctx.lineWidth = Math.max(2, cs * 0.07); rr(ctx, x0 + cs * 0.05, y0 + cs * 0.05, cs * 0.9, cs * 0.9, cs * 0.14); ctx.stroke(); ctx.restore(); }
    }
    /* traceback */
    if (backP >= 0) for (let k = 0; k < path.length; k++) { const a = clamp(backP - (path.length - 1 - k)); if (a <= 0) continue; const [i, j] = path[path.length - 1 - k]; const x0 = tx0 + cs * j, y0 = ty0 + cs * i; ctx.save(); add(ctx, T); ctx.globalAlpha = e * a; glow(ctx, T, x0 + cs * 0.5, y0 + cs * 0.5, cs * 1.3, T.hi, 0.8); ctx.strokeStyle = T.hi; ctx.lineWidth = Math.max(2.5, cs * 0.09); rr(ctx, x0 + cs * 0.03, y0 + cs * 0.03, cs * 0.94, cs * 0.94, cs * 0.16); ctx.stroke(); ctx.restore(); }
    /* the code panel and result line */
    const fs2 = Math.max(10, U * (portrait ? 0.028 : 0.026)), px = portrait ? W * 0.05 : W * 0.64, py = portrait ? H * 0.6 : H * 0.14, pw = portrait ? W * 0.9 : W * 0.32, ph2 = portrait ? H * 0.22 : H * 0.34;
    const act = new Set(); if (curIdx >= 0) { const i = Math.floor(curIdx / m) + 1, j = curIdx % m + 1; act.add(0); act.add(1); act.add(2); if (A[i - 1] === B[j - 1]) act.add(3); else act.add(4); }
    PANEL(ctx, T, sc, px, py, pw, ph2, fs2 * 0.6, 'lcs.cpp', fs2); const cf = Math.min(fs2 * 0.92, pw / 38); paintCode(ctx, T, DP_CODE, px + fs2 * 1.2, py + fs2 * 3, cf, cf * 1.65, act, cf * 0.6);
    const rx = px, ry = py + ph2 + fs2 * 1.4; ctx.textAlign = 'left'; ctx.font = `800 ${fs2 * 0.85}px ${MONO}`; ctx.fillStyle = T.dim; ctx.fillText('lcs =', rx, ry + fs2 * 0.6);
    const got = backP >= 0 ? path.slice(0, Math.min(path.length, Math.floor(backP) + 1)).map(([i]) => A[i - 1]).join('') : ''; ctx.fillStyle = T.hi; ctx.font = `800 ${fs2 * 2.2}px ${MONO}`; ctx.fillText(got || '…', rx + fs2 * 3.6, ry + fs2 * 1.2);
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.5, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.4 : 0.1)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });


  /* ---- shared day/night state for the block-world scenes ---- */
  const skyState = (t, T, sc, W, H) => {
    const a = (frac(t / 60 + 0.16)) * TAU, alt = Math.sin(a), d = clamp(alt * 2.2 + 0.45) * (T.dark ? 0.8 : 1), warm = clamp(1 - Math.abs(alt) * 3.5), k = T.dark ? 0.22 : 0.08;
    const top = mix(mix(mix('#050A1E', sc.bg, 0.3), mix('#4E9CF0', sc.bg, k), d), mix('#5B4FA0', sc.bg, k), warm * 0.5), bot = mix(mix(mix('#1B2A55', sc.bg, 0.3), mix('#BFE6FF', sc.bg, k), d), '#FF9A5C', warm * 0.75);
    return { top, bot, night: 1 - d, warm, alt, sun: [W * (0.5 - Math.cos(a) * 0.42), H * (0.62 - alt * 0.5)], moon: [W * (0.5 + Math.cos(a) * 0.42), H * (0.62 + alt * 0.5)] };
  };
  const blk = (ctx, x, y, s, c) => { ctx.fillStyle = c; ctx.fillRect(Math.round(x), Math.round(y), Math.ceil(s), Math.ceil(s)); };

  /* ================= ボクセル ================= */
  K.registerBg('echoVoxelWorld', 'ボクセル', ['calm', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `vx|${seed}|${ck(sc)}`;
    const G = 44, tw = Math.max(W, H) / (portrait ? 14 : 22), bh = tw / 2, ox = W / 2, hz = H * 0.3, s0 = Math.ceil(W / tw) + 3, oy = hz - s0 * tw / 4, wl = 3;
    const hgt = (x, y) => { if (x < 0 || y < 0 || x >= G || y >= G) return 1; const n = 3 * Math.sin(x * 0.31 + seed) * Math.cos(y * 0.27 + seed * 1.7) + 2 * Math.sin((x + y) * 0.17 + seed * 0.5) + 1.1 * Math.sin(x * 0.9 + seed) * Math.sin(y * 0.8); return Math.max(1, Math.min(10, Math.round(4.7 + n * 0.78))); };
    let sk;
    const S = strip(env, `${key}|${W}x${H}`, W, H, 0.85, (x) => {
      sk = { top: T.dark ? mix('#3E6FB0', sc.bg, 0.4) : mix('#7EC8FF', sc.bg, 0.1), bot: T.dark ? mix('#8FB4D8', sc.bg, 0.45) : mix('#D8F0FF', sc.bg, 0.05) };
      x.fillStyle = vertical(x, 0, H, [[0, sk.top], [hz / H + 0.05, sk.bot], [1, sk.bot]]); x.fillRect(0, 0, W, H);
      const lit = T.dark ? 0.72 : 1, tint = c => mix(mix('#000000', c, lit), sc.accent, 0.08);
      const cols = { grass: tint(mix('#5FBF3F', sc.accent, 0.16)), grass2: tint(mix('#4FA836', sc.accent, 0.16)), dirt: tint('#8B5E3C'), stone: tint('#8E8E96'), sand: tint('#E2D08A'), snow: tint('#F2F6FA'), water: tint('#3E86E8'), wood: tint('#6B4A2B'), leaf: tint('#2F8F3A') };
      const waters = []; const shade = (c, f, rr0) => mix('#000000', c, clamp(f * (0.94 + 0.12 * rr0)));
      const quad = (pts, c) => { x.fillStyle = c; x.beginPath(); pts.forEach((p, i) => i ? x.lineTo(p[0], p[1]) : x.moveTo(p[0], p[1])); x.closePath(); x.fill(); x.strokeStyle = 'rgba(0,0,0,0.16)'; x.lineWidth = Math.max(0.6, tw * 0.012); x.stroke(); };
      const cube = (gx, gy, z, top, sideCol, showL, showR, showT, rr0, alpha) => {
        const sx = ox + (gx - gy) * tw / 2, st = oy + (gx + gy) * tw / 4 - (z + 1) * bh; if (sx < -tw || sx > W + tw || st > H + bh || st < -bh * 3) return;
        x.globalAlpha = alpha || 1;
        if (showT) quad([[sx, st], [sx + tw / 2, st + tw / 4], [sx, st + tw / 2], [sx - tw / 2, st + tw / 4]], shade(top, 1, rr0));
        if (showL) quad([[sx - tw / 2, st + tw / 4], [sx, st + tw / 2], [sx, st + tw / 2 + bh], [sx - tw / 2, st + tw / 4 + bh]], shade(sideCol, 0.8, rr0));
        if (showR) quad([[sx, st + tw / 2], [sx + tw / 2, st + tw / 4], [sx + tw / 2, st + tw / 4 + bh], [sx, st + tw / 2 + bh]], shade(sideCol, 0.62, rr0));
        x.globalAlpha = 1;
      };
      for (let s = Math.max(0, s0 - 4); s <= 2 * G - 2; s++) for (let gx = Math.max(0, s - G + 1); gx <= Math.min(G - 1, s); gx++) {
        const gy = s - gx, h = hgt(gx, gy), hl = hgt(gx, gy + 1), hr = hgt(gx + 1, gy), rr0 = r(seed, gx * 64 + gy, 5);
        const sx = ox + (gx - gy) * tw / 2, sy = oy + (gx + gy) * tw / 4; if (sx < -tw || sx > W + tw || sy < hz - tw || sy - h * bh > H + bh) continue;
        const beach = h <= wl + 1 && h >= wl, snow = h >= 10, rock = h >= 8;
        for (let z = Math.max(0, Math.min(hl, hr, h - 1)); z < h; z++) {
          const isTop = z === h - 1, topC = snow ? cols.snow : rock ? cols.stone : beach ? cols.sand : (rr0 > 0.5 ? cols.grass : cols.grass2), sideC = rock || snow ? cols.stone : beach ? cols.sand : (isTop ? cols.dirt : cols.dirt);
          cube(gx, gy, z, isTop ? topC : sideC, sideC, z >= hl, z >= hr, isTop, r(seed, gx * 64 + gy, 6 + z));
        }
        if (h > wl + 1 && !rock && r(seed, gx * 64 + gy, 30) > 0.93) { /* a blocky tree */
          for (let z = h; z < h + 2; z++) cube(gx, gy, z, cols.wood, cols.wood, true, true, false, rr0);
          for (let z = h + 2; z < h + 4; z++) cube(gx, gy, z, cols.leaf, cols.leaf, true, true, z === h + 3, rr0);
        }
        if (h <= wl) { for (let z = h; z < wl; z++) cube(gx, gy, z, cols.water, cols.water, true, true, z === wl - 1, rr0, 0.8); waters.push([sx, oy + (gx + gy) * tw / 4 - wl * bh + tw / 4, rr0]); }
      }
      { const y0 = hz - tw * 3, y1 = hz + H * 0.24, fg = x.createLinearGradient(0, y0, 0, y1); fg.addColorStop(0, rgba(sk.bot, 0)); fg.addColorStop(0.28, rgba(sk.bot, 0.92)); fg.addColorStop(1, rgba(sk.bot, 0)); x.fillStyle = fg; x.fillRect(0, y0, W, y1 - y0); }
      return { waters };
    });
    const st = skyState(t, T, sc, W, H);
    ctx.globalAlpha = e; ctx.save(); ctx.translate(W / 2, H * 0.55); const zoom = 1.06 + 0.015 * Math.sin(t * 0.13); ctx.scale(zoom, zoom); ctx.translate(-W / 2 + Math.sin(t * 0.09) * U * 0.03, -H * 0.55);
    ctx.drawImage(S.cv, 0, 0, W, H);
    /* water sparkle */
    ctx.fillStyle = 'rgba(255,255,255,0.8)'; for (const [x, y, q] of S.meta.waters) { const v = Math.sin(t * 2.2 + q * 40); if (v > 0.86) ctx.fillRect(x - tw * 0.1, y - tw * 0.02, tw * 0.2 * (v - 0.8) * 5, Math.max(1.5, tw * 0.03)); }
    /* blocky clouds on top */
    ctx.fillStyle = T.dark ? 'rgba(230,240,255,0.5)' : 'rgba(255,255,255,0.8)';
    for (let i = 0; i < 6; i++) { const cw = tw * (2 + 2 * r(seed, i, 41)), cxp = ((r(seed, i, 42) * (W + cw * 2) + t * U * 0.012 * (0.6 + r(seed, i, 43))) % (W + cw * 2)) - cw, cyp = H * (0.04 + 0.13 * r(seed, i, 44)), ch = tw * 0.3; ctx.fillRect(cxp, cyp, cw, ch); ctx.fillRect(cxp + cw * 0.15, cyp - ch, cw * 0.5, ch); ctx.fillRect(cxp - cw * 0.1, cyp + ch, cw * 0.7, ch * 0.7); }
    ctx.restore();
    /* sun / moon as flat squares with a glow, and the light of the hour over everything */
    const sunUp = st.alt > -0.1, [ux, uy] = sunUp ? st.sun : st.moon;
    ctx.save(); add(ctx, T); glow(ctx, T, ux, uy, U * 0.5, sunUp ? '#FFD98A' : '#BFD4FF', 0.32); ctx.restore(); ctx.fillStyle = sunUp ? '#FFF1B8' : '#EAF0FF'; ctx.fillRect(ux - U * 0.045, uy - U * 0.045, U * 0.09, U * 0.09);
    ctx.fillStyle = rgba(mix('#0A1240', sc.bg, 0.2), 0.55 * st.night); ctx.fillRect(0, 0, W, H); if (st.warm > 0.05) { ctx.fillStyle = rgba('#FF8A3D', 0.16 * st.warm); ctx.fillRect(0, 0, W, H); }
    ctx.save(); add(ctx, T); ctx.fillStyle = rgba('#FFFFFF', 0.9 * st.night); for (let i = 0; i < 34; i++) { if (st.night < 0.3) break; ctx.globalAlpha = e * st.night * (0.4 + 0.6 * Math.abs(Math.sin(t * 1.3 + i))); ctx.fillRect(W * r(seed, i, 51), H * 0.28 * r(seed, i, 52), U * 0.007, U * 0.007); } ctx.restore(); ctx.globalAlpha = e;
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.5, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.4 : 0.1)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });

  /* ================= ブロック洞窟 ================= */
  K.registerBg('echoBlockCave', 'ブロック洞窟', ['calm', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, cs = Math.max(14, U * (portrait ? 0.075 : 0.052));
    const cols = Math.ceil(W / cs) + 1, rows = Math.ceil(H / cs) + 1, key = `bc|${seed}|${ck(sc)}|${cols}x${rows}`;
    const S = strip(env, key, W, H, 0.8, (x) => {
      const lit = T.dark ? 0.78 : 1, tint = c => mix(mix('#000000', c, lit), sc.accent, 0.06);
      const C = { grass: tint(mix('#5FBF3F', sc.accent, 0.16)), dirt: tint('#8B5E3C'), stone: tint(mix('#8E8E96', sc.fg, 0.08)), coal: '#22232A', iron: tint('#D8AF93'), gold: '#F5C518', dia: '#5BF1E6', lava: '#FF6A1A', water: tint('#3E86E8') };
      const cell = Array.from({ length: rows }, () => new Array(cols).fill('stone'));
      const surf = []; for (let cx = 0; cx < cols; cx++) surf[cx] = Math.round(2.4 + 1.6 * Math.sin(cx * 0.31 + seed) + 0.9 * Math.sin(cx * 0.83 + seed * 2));
      for (let cx = 0; cx < cols; cx++) for (let cy = 0; cy < rows; cy++) { const s0 = surf[cx]; cell[cy][cx] = cy < s0 ? 'sky' : cy === s0 ? 'grass' : cy <= s0 + 3 ? 'dirt' : 'stone'; }
      /* tunnels and chambers */
      const carve = (cx, cy) => { if (cx >= 0 && cx < cols && cy > surf[Math.min(cols - 1, Math.max(0, cx))] + 3 && cy < rows) cell[cy][cx] = 'air'; };
      for (let tn = 0; tn < 3; tn++) { let cx = Math.floor(r(seed, tn, 70) * cols * 0.3), cy = Math.floor(rows * (0.45 + 0.4 * r(seed, tn, 71))); for (let s = 0; s < cols * 1.2; s++) { carve(cx, cy); carve(cx, cy - 1); carve(cx + 1, cy); cx += 1; if (r(seed, tn * 300 + s, 72) < 0.35) cy += r(seed, tn * 300 + s, 73) < 0.5 ? -1 : 1; cy = Math.max(surf[Math.min(cols - 1, cx)] + 5, Math.min(rows - 2, cy)); } }
      for (let ch = 0; ch < 2; ch++) { const cx0 = Math.floor(r(seed, ch, 74) * cols), cy0 = Math.floor(rows * (0.6 + 0.3 * r(seed, ch, 75))), rx = 3 + Math.floor(r(seed, ch, 76) * 3), ry = 2 + Math.floor(r(seed, ch, 77) * 2); for (let a = -rx; a <= rx; a++) for (let b = -ry; b <= ry; b++) if ((a * a) / (rx * rx) + (b * b) / (ry * ry) <= 1) carve(cx0 + a, cy0 + b); }
      /* ores */
      for (let cy = 0; cy < rows; cy++) for (let cx = 0; cx < cols; cx++) { if (cell[cy][cx] !== 'stone') continue; const dep = cy / rows, q = r(seed, cy * 200 + cx, 80); const kind = q < 0.045 ? 'coal' : q < 0.07 ? 'iron' : (q < 0.085 && dep > 0.5) ? 'gold' : (q < 0.095 && dep > 0.7) ? 'dia' : null; if (kind) { cell[cy][cx] = kind; if (r(seed, cy * 200 + cx, 81) < 0.5 && cx + 1 < cols && cell[cy][cx + 1] === 'stone') cell[cy][cx + 1] = kind; } }
      /* lava at the bottom, a little water above the caves */
      for (let cx = 0; cx < cols; cx++) for (let cy = rows - 3; cy < rows; cy++) if (cell[cy][cx] === 'air') cell[cy][cx] = 'lava';
      const air = [], torches = [], dias = [], lavas = [];
      for (let cy = 0; cy < rows; cy++) for (let cx = 0; cx < cols; cx++) {
        const k = cell[cy][cx], rr0 = r(seed, cy * 200 + cx, 90); let base;
        if (k === 'sky') continue;
        else if (k === 'air') { air.push([cx, cy]); base = mix(C.stone, '#000000', 0.78); }
        else base = C[k] || C.stone;
        const j = k === 'lava' ? 1.05 : (0.9 + 0.2 * rr0);
        x.fillStyle = k === 'lava' || k === 'gold' || k === 'dia' ? base : mix('#000000', base, clamp(j)); x.fillRect(cx * cs, cy * cs, cs + 0.5, cs + 0.5);
        if (k !== 'air') { const sp = cs / 5; x.fillStyle = 'rgba(0,0,0,0.13)'; for (let q = 0; q < 3; q++) x.fillRect(cx * cs + r(seed, cy * 200 + cx, 100 + q) * (cs - sp), cy * cs + r(seed, cy * 200 + cx, 110 + q) * (cs - sp), sp, sp); x.fillStyle = 'rgba(255,255,255,0.06)'; x.fillRect(cx * cs + r(seed, cy * 200 + cx, 120) * (cs - sp), cy * cs + r(seed, cy * 200 + cx, 121) * (cs - sp), sp, sp); x.strokeStyle = 'rgba(0,0,0,0.16)'; x.lineWidth = 0.8; x.strokeRect(cx * cs + 0.4, cy * cs + 0.4, cs - 0.8, cs - 0.8); }
        if (k === 'coal' || k === 'iron' || k === 'gold' || k === 'dia') { x.fillStyle = C[k]; for (let q = 0; q < 4; q++) x.fillRect(cx * cs + r(seed, cy * 200 + cx, 130 + q) * cs * 0.7, cy * cs + r(seed, cy * 200 + cx, 140 + q) * cs * 0.7, cs * 0.3, cs * 0.3); if (k === 'dia') dias.push([cx * cs + cs * 0.5, cy * cs + cs * 0.5]); }
        if (k === 'lava') lavas.push([cx * cs + cs * 0.5, cy * cs + cs * 0.5]);
      }
      /* torches on cave walls */
      for (const [cx, cy] of air) { if (torches.length >= 5) break; if (cy < 1 || cy + 1 >= rows) continue; const floor = cell[cy + 1][cx] === 'stone' && cell[cy][cx - 1] === 'air'; if (floor && r(seed, cy * 200 + cx, 150) < 0.16) torches.push([cx * cs + cs * 0.5, cy * cs + cs * 0.5]); }
      for (const [tx, ty] of torches) { x.fillStyle = '#6B4A2B'; x.fillRect(tx - cs * 0.07, ty - cs * 0.05, cs * 0.14, cs * 0.55); x.fillStyle = '#FFB03A'; x.fillRect(tx - cs * 0.13, ty - cs * 0.28, cs * 0.26, cs * 0.26); }
      /* the sky above the ground */
      return { air, torches, dias, lavas, surf };
    });
    const { air, torches, dias, lavas, surf } = S.meta;
    const sk = skyState(t, T, sc, W, H);
    ctx.globalAlpha = e;
    ctx.fillStyle = vertical(ctx, 0, cs * 7, [[0, sk.top], [1, sk.bot]]); ctx.fillRect(0, 0, W, cs * 7);
    ctx.drawImage(S.cv, 0, 0, W, H);
    /* darkness with depth, so the torches carry the picture */
    { const g = ctx.createLinearGradient(0, cs * 3, 0, H); g.addColorStop(0, rgba('#000000', 0)); g.addColorStop(1, rgba('#000000', T.dark ? 0.66 : 0.4)); ctx.fillStyle = g; ctx.fillRect(0, cs * 3, W, H); }
    ctx.save(); add(ctx, T);
    for (let i = 0; i < torches.length; i++) { const [tx, ty] = torches[i], fl = 0.75 + 0.15 * Math.sin(t * 9 + i * 3) + 0.1 * Math.sin(t * 23 + i); glow(ctx, T, tx, ty, cs * 6.5, '#FFB03A', 0.75 * fl); ctx.fillStyle = rgba('#FFE08A', 0.85); ctx.fillRect(tx - cs * 0.08, ty - cs * (0.34 + 0.05 * Math.sin(t * 17 + i)), cs * 0.16, cs * 0.14); }
    for (let i = 0; i < lavas.length; i += 2) { const [lx, ly] = lavas[i]; glow(ctx, T, lx, ly, cs * 3.4, '#FF6A1A', 0.22 + 0.08 * Math.sin(t * 1.7 + i)); }
    for (let i = 0; i < dias.length; i++) { const [dx, dy] = dias[i], v = Math.sin(t * 2.4 + i * 5); if (v > 0.75) { ctx.fillStyle = rgba('#FFFFFF', (v - 0.75) * 4); ctx.fillRect(dx - cs * 0.05, dy - cs * 0.3, cs * 0.1, cs * 0.6); ctx.fillRect(dx - cs * 0.3, dy - cs * 0.05, cs * 0.6, cs * 0.1); } }
    /* dust hanging in the cave air */
    for (let i = 0; i < 26 && air.length; i++) { const [ax, ay] = air[Math.floor(r(seed, i, 160) * air.length) % air.length], age = frac(t * 0.05 + r(seed, i, 161)); ctx.fillStyle = rgba('#FFE7B0', 0.4 * Math.sin(age * Math.PI)); ctx.fillRect(ax * cs + cs * r(seed, i, 162) + Math.sin(t + i) * cs * 0.2, ay * cs + cs * 0.9 - age * cs * 1.6, cs * 0.06, cs * 0.06); }
    ctx.restore();
  });

  /* ================= ブロックの空 ================= */
  K.registerBg('echoBlockSky', 'ブロックの空', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, ps = Math.max(6, U * 0.02), key = `bs|${seed}|${ck(sc)}|${Math.round(ps)}`;
    const st = skyState(t, T, sc, W, H);
    ctx.globalAlpha = e;
    ctx.fillStyle = vertical(ctx, 0, H, [[0, st.top], [0.75, st.bot], [1, st.bot]]); ctx.fillRect(0, 0, W, H);
    /* stars */
    if (st.night > 0.25) { ctx.fillStyle = '#FFFFFF'; for (let i = 0; i < 70; i++) { ctx.globalAlpha = e * clamp(st.night - 0.2) * (0.35 + 0.65 * Math.abs(Math.sin(t * 0.9 + i * 1.7))); const sz = i % 9 === 0 ? ps * 0.7 : ps * 0.4; ctx.fillRect(Math.round(W * r(seed, i, 51) / ps) * ps, Math.round(H * 0.6 * r(seed, i, 52) / ps) * ps, sz, sz); } ctx.globalAlpha = e; }
    /* sun and moon: big flat squares */
    const sunUp = st.alt > -0.15;
    for (const [[px, py], body, halo, sz, vis] of [[st.sun, '#FFF1B8', '#FFD98A', 4.4, clamp(st.alt + 0.25)], [st.moon, '#EAF0FF', '#BFD4FF', 3.4, clamp(-st.alt + 0.25)]]) {
      if (vis <= 0.02) continue; ctx.save(); ctx.globalAlpha = e * vis; add(ctx, T); glow(ctx, T, px, py, ps * 14, halo, 0.5); ctx.restore(); ctx.globalAlpha = e * vis; const q = Math.round(px / ps) * ps, w2 = Math.round(py / ps) * ps; ctx.fillStyle = body; ctx.fillRect(q - ps * sz / 2, w2 - ps * sz / 2, ps * sz, ps * sz);
      if (body === '#EAF0FF') { ctx.fillStyle = 'rgba(120,140,190,0.35)'; ctx.fillRect(q - ps * 0.9, w2 - ps * 0.9, ps * 0.9, ps * 0.9); ctx.fillRect(q + ps * 0.3, w2 + ps * 0.3, ps * 0.7, ps * 0.7); } ctx.globalAlpha = e;
    }
    void sunUp;
    /* clouds: stepped stacks of slabs */
    ctx.fillStyle = rgba(mix('#FFFFFF', st.bot, 0.15 + 0.6 * st.night), 0.85);
    for (let i = 0; i < 7; i++) { const cw = ps * (5 + Math.floor(r(seed, i, 41) * 6)), span = W + cw * 3, cxp = Math.round(((r(seed, i, 42) * span + t * U * 0.008 * (0.6 + r(seed, i, 43))) % span - cw) / ps) * ps, cyp = Math.round(H * (0.1 + 0.3 * r(seed, i, 44)) / ps) * ps; ctx.fillRect(cxp, cyp, cw, ps); ctx.fillRect(cxp + ps, cyp - ps, cw - ps * 3, ps); ctx.fillRect(cxp - ps, cyp + ps, cw + ps * 2, ps); ctx.fillRect(cxp + ps * 2, cyp + ps * 2, cw - ps * 4, ps * 0.7); }
    /* hills (cached), then the day's light and fireflies */
    const hills = strip(env, `${key}|h`, W, H, 0.9, (x) => {
      const lit = T.dark ? 0.7 : 1, gcol = mix('#000000', mix('#5FBF3F', sc.accent, 0.16), lit);
      [[0.62, 0.5, 0.11, 0.35], [0.72, 0.75, 0.13, 0.65], [0.84, 1, 0.12, 1]].forEach(([base, dark, amp, ph], li) => {
        x.fillStyle = mix('#000000', mix(gcol, '#7FA8C8', 0.55 - li * 0.25), 0.5 + dark * 0.5);
        for (let cx = 0; cx * ps < W; cx++) { const hh = Math.round((H * base + Math.sin(cx * 0.11 * (li + 1) + seed + li * 2) * H * amp * 0.5 + Math.sin(cx * 0.31 + li) * ps * 0.8) / ps) * ps; x.fillRect(cx * ps, hh, ps + 0.5, H - hh); }
        if (li === 2) { x.fillStyle = mix('#000000', '#8B5E3C', lit * 0.85); for (let cx = 0; cx * ps < W; cx++) { const hh = Math.round((H * base + Math.sin(cx * 0.11 * 3 + seed + 4) * H * amp * 0.5 + Math.sin(cx * 0.31 + 2) * ps * 0.8) / ps) * ps; x.fillRect(cx * ps, hh + ps, ps + 0.5, ps * 3); } }
        /* blocky trees */
        for (let i = 0; i < 6 + li * 2; i++) { const cx = Math.floor(r(seed + li, i, 60) * (W / ps)), hh = Math.round((H * base + Math.sin(cx * 0.11 * (li + 1) + seed + li * 2) * H * amp * 0.5 + Math.sin(cx * 0.31 + li) * ps * 0.8) / ps) * ps, th = ps * (2 + li), lw = 3 + li; x.fillStyle = mix('#000000', '#6B4A2B', lit * (0.55 + li * 0.2)); x.fillRect(cx * ps, hh - th, ps, th); x.fillStyle = mix('#000000', '#2F8F3A', lit * (0.55 + li * 0.2)); x.fillRect((cx - (lw - 1) / 2) * ps, hh - th - ps * lw * 0.9, lw * ps, ps * lw * 0.9); x.fillRect((cx - (lw - 3) / 2) * ps, hh - th - ps * (lw * 0.9 + 1), (lw - 2) * ps, ps); }
      });
    });
    ctx.drawImage(hills.cv, 0, 0, W, H);
    ctx.fillStyle = rgba(mix('#0A1240', sc.bg, 0.2), 0.5 * st.night); ctx.fillRect(0, 0, W, H); if (st.warm > 0.05) { ctx.fillStyle = rgba('#FF8A3D', 0.14 * st.warm); ctx.fillRect(0, 0, W, H); }
    if (st.night > 0.3) { ctx.save(); add(ctx, T); for (let i = 0; i < 16; i++) { const x = W * (0.05 + 0.9 * r(seed, i, 70)) + Math.sin(t * 0.6 + i * 3) * U * 0.05, y = H * (0.68 + 0.25 * r(seed, i, 71)) + Math.sin(t * 0.8 + i * 2) * U * 0.03, v = 0.4 + 0.6 * Math.abs(Math.sin(t * 1.4 + i)); ctx.globalAlpha = e * clamp(st.night - 0.2) * v; glow(ctx, T, x, y, ps * 2.4, '#DFFF7A', 0.6); ctx.fillStyle = '#F4FFB0'; ctx.fillRect(x - ps * 0.2, y - ps * 0.2, ps * 0.4, ps * 0.4); } ctx.restore(); }
    ctx.globalAlpha = e;
  });

  /* ================= front: cubes drifting up ================= */
  K.registerFront('echoCubeDrift', '立方体', (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones2(sc), pc = PCOL(T, sc);
    ctx.globalAlpha = e;
    for (let i = 0; i < 14; i++) {
      const age = frac(t * (0.018 + 0.03 * r(seed, i, 1)) + r(seed, i, 2)), x = W * r(seed, i, 3) + Math.sin(t * 0.4 + i * 2.3) * U * 0.03, y = H * 1.05 - age * H * 1.1, s = U * (0.018 + 0.035 * r(seed, i, 4)), c = pc[i % pc.length];
      ctx.globalAlpha = e * (T.dark ? 0.6 : 0.5) * Math.sin(age * Math.PI);
      ctx.fillStyle = mix(c, '#FFFFFF', 0.25); ctx.beginPath(); ctx.moveTo(x, y - s); ctx.lineTo(x + s, y - s / 2); ctx.lineTo(x, y); ctx.lineTo(x - s, y - s / 2); ctx.closePath(); ctx.fill();
      ctx.fillStyle = mix(c, '#000000', 0.15); ctx.beginPath(); ctx.moveTo(x - s, y - s / 2); ctx.lineTo(x, y); ctx.lineTo(x, y + s); ctx.lineTo(x - s, y + s / 2); ctx.closePath(); ctx.fill();
      ctx.fillStyle = mix(c, '#000000', 0.38); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + s, y - s / 2); ctx.lineTo(x + s, y + s / 2); ctx.lineTo(x, y + s); ctx.closePath(); ctx.fill();
    }
    ctx.globalAlpha = e;
  });

})();
