/* ECHO first-party backdrops, general hi-fi scenes with a hard, product-photo feel: スペクトラム, レコード溝, オシロスコープ, チューナー, ディスク, 光のリボン, ヘッドホン.
   No characters. Every colour comes from the current scheme: dark schemes glow with additive light, light schemes turn into inked technical prints. */
(() => {
  'use strict';
  let GV = null, GVx = null;
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

  /* ================= スペクトラム ================= */
  K.registerBg('echoSpectrumTower', 'スペクトラム', ['pop', 'glitch', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `sq|${seed}|${ck(sc)}`;
    const n = portrait ? 18 : 34, rows = portrait ? 26 : 22, fy = H * 0.72, top = H * 0.08, gapx = W * (portrait ? 0.05 : 0.055), bw = (W - gapx * 2) / n, sh = (fy - top) / rows, u = bw * 0.14;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.4, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [0.72, T.bg1], [0.72, T.dark ? mix(T.bg0, T.c1, 0.1) : mix(T.bg1, '#FFFFFF', 0.4)], [1, T.bg0]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W / 2, fy, U * 1.1, T.c1, T.dark ? 0.22 : 0.3);
      x.fillStyle = rgba(T.line, T.dark ? 0.06 : 0.08); for (let i = 0; i <= n; i++) x.fillRect(gapx + i * bw - 0.5, 0, 1, fy);
      x.strokeStyle = rgba(T.hi, T.dark ? 0.7 : 0.5); x.lineWidth = Math.max(1.5, U * 0.003); x.beginPath(); x.moveTo(0, fy); x.lineTo(W, fy); x.stroke();
    }), 0, 0, W, H);
    const segCol = (k, dim) => { const q = k / (rows - 1); const c = q < 0.6 ? mix(T.c2, T.c1, q / 0.6) : q < 0.85 ? mix(T.c1, T.hi, (q - 0.6) / 0.25) : T.hot; return rgba(c, dim); };
    const drawBars = (mirror) => {
      for (let i = 0; i < n; i++) {
        const lv = band(t, i, n), on = Math.round(lv * rows), x0 = gapx + i * bw + u;
        for (let k = 0; k < rows; k++) { const y = fy - (k + 1) * sh + sh * 0.14; if (k < on) { ctx.fillStyle = segCol(k, 1); ctx.fillRect(x0, y, bw - u * 2, sh * 0.72); } else if (!mirror) { ctx.fillStyle = rgba(T.line, T.dark ? 0.07 : 0.1); ctx.fillRect(x0, y, bw - u * 2, sh * 0.72); } }
        const pk = clamp(Math.max(lv, 0.05 + 0.9 * frac(-t * 0.35 + i * 0.13) * 0.0) ), ph = Math.floor(clamp(lv * rows + 1.2 + 2.2 * Math.abs(Math.sin(t * 1.3 + i * 0.6)) * beat(t, 60) )); void pk;
        ctx.fillStyle = rgba(T.dark ? '#FFFFFF' : T.ink, 0.95); ctx.fillRect(x0, fy - Math.min(rows, ph + 1) * sh + sh * 0.14, bw - u * 2, sh * 0.3);
      }
    };
    ctx.save(); add(ctx, T); drawBars(false); ctx.restore();
    /* glass floor: a fading mirror image */
    ctx.save(); ctx.beginPath(); ctx.rect(0, fy, W, H - fy); ctx.clip(); ctx.translate(0, fy * 2); ctx.scale(1, -1); ctx.globalAlpha = e * 0.26; add(ctx, T); drawBars(true); ctx.restore();
    const fg = ctx.createLinearGradient(0, fy, 0, H); fg.addColorStop(0, rgba(T.bg0, 0)); fg.addColorStop(0.55, rgba(T.bg0, 0.85)); fg.addColorStop(1, rgba(T.bg0, 1)); ctx.globalAlpha = e; ctx.fillStyle = fg; ctx.fillRect(0, fy, W, H - fy);
    ctx.save(); add(ctx, T); const kk = beat(t, 120); glow(ctx, T, W / 2, fy, U * (0.5 + 0.3 * kk), T.c1, 0.2 + 0.4 * kk); ctx.restore();
    ctx.fillStyle = rgba(T.line, 0.8); ctx.font = `${Math.max(9, U * 0.016)}px ${MONO}`; ctx.textBaseline = 'top'; const fr = [31, 62, 125, 250, 500, '1k', '2k', '4k', '8k', '16k']; fr.forEach((f, i) => ctx.fillText(String(f), gapx + (i + 0.3) * ((W - gapx * 2) / fr.length), fy + U * 0.02));
  });

  /* ================= レコード溝 ================= */
  K.registerBg('echoGrooves', 'レコード溝', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `gv|${seed}|${ck(sc)}|${W}x${H}`;
    const cx = W * (portrait ? 0.5 : 0.6), cy = H * (portrait ? 0.6 : 0.56), R = portrait ? Math.min(W * 0.98, H * 0.5) : H * 0.92, rl = R * 0.3, rg0 = R * 0.34, rg1 = R * 0.965;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.7, (x) => {
      x.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.9) : mix(sc.bg, sc.fg, 0.12); x.fillRect(0, 0, W, H);
      /* plinth shadow + the disc itself */
      x.save(); x.shadowColor = 'rgba(0,0,0,0.75)'; x.shadowBlur = R * 0.12; x.shadowOffsetY = R * 0.04;
      x.fillStyle = T.dark ? '#050507' : mix(sc.fg, '#000000', 0.55); x.beginPath(); x.arc(cx, cy, R, 0, TAU); x.fill(); x.restore();
      x.strokeStyle = rgba('#FFFFFF', T.dark ? 0.16 : 0.22); x.lineWidth = Math.max(1, U * 0.002); x.beginPath(); x.arc(cx, cy, R - 1, 0, TAU); x.stroke();
      x.strokeStyle = rgba('#FFFFFF', T.dark ? 0.085 : 0.13); x.lineWidth = Math.max(0.6, U * 0.0011); x.beginPath();
      for (let k = 0; k < 300; k++) { const rad = rg0 + (rg1 - rg0) * k / 300; x.moveTo(cx + rad, cy); x.arc(cx, cy, rad, 0, TAU); }
      x.stroke();
      /* smooth bands between tracks */
      x.strokeStyle = rgba('#000000', 0.85); x.lineWidth = R * 0.012; x.beginPath();
      for (const q of [0.18, 0.41, 0.63, 0.83]) { const rad = rg0 + (rg1 - rg0) * q; x.moveTo(cx + rad, cy); x.arc(cx, cy, rad, 0, TAU); } x.stroke();
      /* run-out ring */
      x.strokeStyle = rgba('#000000', 0.9); x.lineWidth = R * 0.02; x.beginPath(); x.arc(cx, cy, rg0 - R * 0.015, 0, TAU); x.stroke();
    }), 0, 0, W, H);
    /* anisotropic light: bright lobes that ride the grooves, drifting slowly. A cached groove mask is lit by a conic gradient each frame. */
    const rot = -0.95 + Math.sin(t * 0.22) * 0.16, gm = layer(env, `${key}|gm`, 0.5, (x) => {
      x.strokeStyle = '#FFFFFF'; x.lineWidth = Math.max(0.8, U * 0.0022); x.beginPath();
      for (let k = 0; k < 190; k++) { const rad = rg0 + (rg1 - rg0) * k / 190; x.moveTo(cx + rad, cy); x.arc(cx, cy, rad, 0, TAU); }
      x.stroke();
    });
    { const rs = gm.width / W; if (!GV || GV.width !== gm.width || GV.height !== gm.height) { GV = document.createElement('canvas'); GV.width = gm.width; GV.height = gm.height; GVx = GV.getContext('2d'); }
      const g2 = GVx; g2.setTransform(1, 0, 0, 1, 0, 0); g2.globalCompositeOperation = 'source-over'; g2.clearRect(0, 0, GV.width, GV.height); g2.setTransform(rs, 0, 0, rs, 0, 0); g2.globalCompositeOperation = 'lighter';
      for (const [wd, al, col, a0] of [[0.16, 0.5, T.c1, rot - 0.06], [0.04, 1, T.hi, rot - 0.005], [0.09, 0.5, T.c2, rot + 0.03]]) {
        const g = g2.createConicGradient(a0, cx, cy); g.addColorStop(0, rgba(col, 0)); g.addColorStop(wd / 2, rgba(col, al)); g.addColorStop(wd, rgba(col, 0)); g.addColorStop(0.5, rgba(col, 0)); g.addColorStop(0.5 + wd / 2, rgba(col, al * 0.85)); g.addColorStop(0.5 + wd, rgba(col, 0)); g.addColorStop(1, rgba(col, 0));
        g2.fillStyle = g; g2.fillRect(0, 0, W, H);
      }
      g2.globalCompositeOperation = 'destination-in'; g2.setTransform(1, 0, 0, 1, 0, 0); g2.drawImage(gm, 0, 0);
      ctx.save(); add(ctx, T); ctx.drawImage(GV, 0, 0, W, H); ctx.restore(); }
    /* label: spinning paper with a pip so the rotation reads */
    const spin = t * 0.9;
    ctx.save(); ctx.translate(cx, cy); ctx.rotate(spin);
    ctx.fillStyle = T.dark ? mix(T.c1, '#000000', 0.45) : mix(T.c1, '#FFFFFF', 0.25); ctx.beginPath(); ctx.arc(0, 0, rl, 0, TAU); ctx.fill();
    ctx.strokeStyle = rgba(T.hi, 0.55); ctx.lineWidth = Math.max(1, R * 0.006); ctx.beginPath(); ctx.arc(0, 0, rl * 0.86, 0, TAU); ctx.stroke();
    ctx.fillStyle = rgba(T.hi, 0.85); ctx.beginPath(); ctx.moveTo(rl * 0.86, 0); ctx.arc(0, 0, rl * 0.86, 0, 0.5); ctx.arc(0, 0, rl * 0.5, 0.5, 0, true); ctx.closePath(); ctx.fill();
    ctx.beginPath(); ctx.moveTo(-rl * 0.86, 0); ctx.arc(0, 0, rl * 0.86, Math.PI, Math.PI + 0.5); ctx.arc(0, 0, rl * 0.5, Math.PI + 0.5, Math.PI, true); ctx.closePath(); ctx.fill();
    ctx.fillStyle = rgba('#000000', 0.55); ctx.beginPath(); ctx.arc(0, 0, rl * 0.16, 0, TAU); ctx.fill();
    ctx.restore();
    const sp = ctx.createRadialGradient(cx - rl * 0.1, cy - rl * 0.1, 0, cx, cy, rl * 0.14); sp.addColorStop(0, T.steelHi); sp.addColorStop(1, T.steel); ctx.fillStyle = sp; ctx.beginPath(); ctx.arc(cx, cy, rl * 0.1, 0, TAU); ctx.fill();
    /* tonearm: pivot off the top-right, headshell riding the groove */
    const px = cx + R * 1.02, py = cy - R * 0.98, ang = Math.atan2(cy - R * 0.02 - py, cx + R * 0.42 - px) , drift = Math.sin(t * 0.3) * 0.006;
    const len = Math.hypot(cx + R * 0.42 - px, cy - R * 0.02 - py);
    ctx.save(); ctx.translate(px, py); ctx.rotate(ang + drift);
    const aw = R * 0.03; ctx.shadowColor = 'rgba(0,0,0,0.6)'; ctx.shadowBlur = R * 0.05; ctx.shadowOffsetY = R * 0.03;
    const ag = ctx.createLinearGradient(0, -aw, 0, aw); ag.addColorStop(0, T.steelHi); ag.addColorStop(0.45, T.steel); ag.addColorStop(1, mix(T.steel, '#000000', 0.55));
    ctx.fillStyle = ag; ctx.beginPath(); ctx.moveTo(0, -aw * 1.1); ctx.lineTo(len * 0.9, -aw * 0.5); ctx.lineTo(len * 0.9, aw * 0.5); ctx.lineTo(0, aw * 1.1); ctx.closePath(); ctx.fill(); ctx.shadowColor = 'transparent';
    /* headshell */
    ctx.translate(len * 0.9, 0); ctx.rotate(0.32);
    ctx.fillStyle = mix(T.c1, '#000000', 0.25); ctx.beginPath(); ctx.roundRect ? ctx.roundRect(-R * 0.02, -R * 0.05, R * 0.15, R * 0.1, R * 0.012) : ctx.rect(-R * 0.02, -R * 0.05, R * 0.15, R * 0.1); ctx.fill();
    ctx.fillStyle = rgba(T.hi, 0.6); ctx.fillRect(0, -R * 0.045, R * 0.13, R * 0.008);
    ctx.restore();
    /* stylus tip location (approx headshell end) glows */
    const hx = px + Math.cos(ang + drift) * len * 0.9 + Math.cos(ang + drift + 0.32) * R * 0.13, hy = py + Math.sin(ang + drift) * len * 0.9 + Math.sin(ang + drift + 0.32) * R * 0.13;
    glow(ctx, T, hx, hy, U * 0.13, T.hi, 0.8 + 0.2 * Math.sin(t * 17));
    ctx.save(); add(ctx, T); for (let i = 0; i < 22; i++) { const age = frac(t * (0.25 + 0.3 * r(seed, i, 1)) + r(seed, i, 2)), a2 = -1.2 + (r(seed, i, 3) - 0.5) * 1.6; ctx.fillStyle = rgba(T.hi, 0.9 * (1 - age)); dot(ctx, hx + Math.cos(a2) * age * U * 0.22, hy + Math.sin(a2) * age * U * 0.22 - age * U * 0.05, U * (0.0015 + 0.002 * r(seed, i, 4))); } ctx.restore();
    const vg = ctx.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.3, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, 0.75)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
  });

  /* ================= オシロスコープ ================= */
  K.registerBg('echoScope', 'オシロスコープ', ['glitch', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `os|${seed}|${ck(sc)}`;
    const m = U * 0.05, sx = m, sy = m, sw = W - m * 2, sh = H - m * 2, cols = 10, rows = portrait ? 12 : 8, cw = sw / cols, chh = sh / rows;
    const wave = (tt, x) => { const env2 = 0.35 + 0.65 * beat(tt, 90), a = Math.sin(x * TAU * 2 + tt * 5) * 0.5 + Math.sin(x * TAU * 5 - tt * 8) * 0.22 * env2 + Math.sin(x * TAU * 11 + tt * 13) * 0.08 * env2; return a * (0.55 + 0.5 * env2); };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.8) : mix(sc.bg, sc.fg, 0.14); x.fillRect(0, 0, W, H);
      const sg = x.createRadialGradient(W / 2, H / 2, U * 0.1, W / 2, H / 2, Math.hypot(W, H) * 0.55); sg.addColorStop(0, rgba(T.c1, T.dark ? 0.18 : 0.14)); sg.addColorStop(1, rgba(T.c1, 0)); x.fillStyle = sg; x.fillRect(sx, sy, sw, sh);
      x.strokeStyle = rgba(T.c1, T.dark ? 0.2 : 0.35); x.lineWidth = 1; x.beginPath(); for (let i = 0; i <= cols; i++) { x.moveTo(sx + i * cw, sy); x.lineTo(sx + i * cw, sy + sh); } for (let j = 0; j <= rows; j++) { x.moveTo(sx, sy + j * chh); x.lineTo(sx + sw, sy + j * chh); } x.stroke();
      x.strokeStyle = rgba(T.c1, T.dark ? 0.55 : 0.7); x.lineWidth = Math.max(1.5, U * 0.0025); x.beginPath(); x.moveTo(sx, sy + sh / 2); x.lineTo(sx + sw, sy + sh / 2); x.moveTo(sx + sw / 2, sy); x.lineTo(sx + sw / 2, sy + sh); x.stroke();
      x.lineWidth = 1; x.beginPath(); for (let i = 0; i < cols * 5; i++) { const px = sx + i * cw / 5, l = i % 5 === 0 ? 0.11 : 0.05; x.moveTo(px, sy + sh / 2 - chh * l); x.lineTo(px, sy + sh / 2 + chh * l); } for (let j = 0; j < rows * 5; j++) { const py = sy + j * chh / 5, l = j % 5 === 0 ? 0.11 : 0.05; x.moveTo(sx + sw / 2 - cw * l, py); x.lineTo(sx + sw / 2 + cw * l, py); } x.stroke();
      x.fillStyle = rgba(T.c1, 0.8); x.font = `${Math.max(9, U * 0.017)}px ${MONO}`; x.textBaseline = 'top'; x.fillText('CH1  0.5V/div', sx + m * 0.4, sy + m * 0.35); x.textAlign = 'right'; x.fillText('1ms/div   AUTO', sx + sw - m * 0.4, sy + m * 0.35);
    }), 0, 0, W, H);
    ctx.save(); add(ctx, T); ctx.lineJoin = 'round';
    const mid = sy + sh / 2, amp = sh * 0.36, N = portrait ? 140 : 260;
    for (let k = 5; k >= 0; k--) { /* phosphor persistence: older traces fade out */
      const tt = t - k * 0.035, al = k ? 0.13 / (k * 0.8) : 1; ctx.strokeStyle = rgba(T.c1, al * (T.dark ? 0.35 : 0.5)); ctx.lineWidth = Math.max(4, U * 0.012);
      if (k === 0) { ctx.beginPath(); for (let i = 0; i <= N; i++) { const xx = sx + sw * i / N, yy = mid - wave(tt, i / N) * amp; i ? ctx.lineTo(xx, yy) : ctx.moveTo(xx, yy); } ctx.stroke(); ctx.strokeStyle = rgba(T.hi, 0.95); ctx.lineWidth = Math.max(2, U * 0.004); ctx.stroke(); }
      else { ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.strokeStyle = rgba(T.c1, al * 0.6); ctx.beginPath(); for (let i = 0; i <= N; i += 2) { const xx = sx + sw * i / N, yy = mid - wave(tt, i / N) * amp; i ? ctx.lineTo(xx, yy) : ctx.moveTo(xx, yy); } ctx.stroke(); }
    }
    /* beam spot riding the trace */
    const bx = frac(t * 0.6); glow(ctx, T, sx + sw * bx, mid - wave(t, bx) * amp, U * 0.05, T.hi, 0.9);
    ctx.restore();
    /* curved glass: scanlines, corner shadow, a diagonal glare */
    ctx.fillStyle = rgba('#000000', T.dark ? 0.16 : 0.05); for (let y = 0; y < H; y += 3) ctx.fillRect(0, y, W, 1);
    const vg = ctx.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.35, W / 2, H / 2, Math.hypot(W, H) * 0.6); vg.addColorStop(0, rgba('#000000', 0)); vg.addColorStop(1, rgba('#000000', T.dark ? 0.7 : 0.3)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H);
    const gl = ctx.createLinearGradient(0, 0, W * 0.6, H * 0.6); gl.addColorStop(0, rgba('#FFFFFF', T.dark ? 0.09 : 0.25)); gl.addColorStop(0.4, rgba('#FFFFFF', 0)); ctx.fillStyle = gl; ctx.fillRect(0, 0, W, H);
    ctx.strokeStyle = rgba(T.c1, 0.6); ctx.lineWidth = Math.max(2, U * 0.004); ctx.beginPath(); ctx.roundRect(sx, sy, sw, sh, U * 0.03); ctx.stroke();
  });

  /* ================= チューナー ================= */
  K.registerBg('echoTuner', 'チューナー', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `tn|${seed}|${ck(sc)}`;
    const dw = Math.min(W * 0.94, H * (portrait ? 1.6 : 2.3)), dh = dw * (portrait ? 0.34 : 0.3), dx = W / 2 - dw / 2, dy = H * 0.42 - dh / 2, f0 = 88, f1 = 108;
    const stations = [89.7, 93.1, 96.5, 99.9, 103.3, 106.7];
    /* the needle drifts between stations and lingers on each one */
    const seg = 5.5, si = Math.floor(t / seg) % stations.length, q = frac(t / seg), from = stations[(si + stations.length - 1) % stations.length], to = stations[si], mv = ease(q / 0.45), freq = q < 0.45 ? from + (to - from) * mv : to + Math.sin(t * 9) * 0.02 * Math.exp(-(q - 0.45) * 6), locked = q > 0.5;
    const fx = f => dx + dw * 0.04 + (dw * 0.92) * (f - f0) / (f1 - f0);
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, W / 2, dy + dh / 2, dw * 0.6, T.c1, T.dark ? 0.32 : 0.3);
      const bz = x.createLinearGradient(0, dy - dh * 0.1, 0, dy + dh * 1.1); bz.addColorStop(0, T.steelHi); bz.addColorStop(0.5, T.steel); bz.addColorStop(1, mix(T.steel, '#000000', 0.5)); x.fillStyle = bz; x.beginPath(); x.roundRect(dx - dw * 0.02, dy - dh * 0.12, dw * 1.04, dh * 1.24, dh * 0.1); x.fill(); x.strokeStyle = rgba('#000000', 0.7); x.lineWidth = Math.max(2, U * 0.004); x.stroke();
      const fg = x.createLinearGradient(0, dy, 0, dy + dh); fg.addColorStop(0, mix(T.c1, '#000000', 0.55)); fg.addColorStop(0.5, mix(T.c1, T.c2, 0.4)); fg.addColorStop(1, mix(T.c1, '#000000', 0.4)); x.fillStyle = T.dark ? fg : mix('#FFF3D2', sc.accent2, 0.1); x.beginPath(); x.roundRect(dx, dy, dw, dh, dh * 0.06); x.fill();
      x.save(); x.beginPath(); x.roundRect(dx, dy, dw, dh, dh * 0.06); x.clip();
      const ink = T.dark ? rgba('#FFF6DF', 0.92) : '#1B1610'; x.strokeStyle = ink; x.fillStyle = ink; x.textAlign = 'center'; x.textBaseline = 'alphabetic';
      for (let f = f0; f <= f1 + 0.001; f += 0.2) { const px = fx(f), big = Math.abs(f - Math.round(f)) < 0.01 && Math.round(f) % 2 === 0, mid = Math.abs(f - Math.round(f)) < 0.01; x.lineWidth = Math.max(1, dh * (big ? 0.012 : 0.006)); x.beginPath(); x.moveTo(px, dy + dh * 0.32); x.lineTo(px, dy + dh * (big ? 0.62 : mid ? 0.52 : 0.45)); x.stroke(); if (big) { x.font = `bold ${dh * 0.14}px ${SERIF}`; x.fillText(String(Math.round(f)), px, dy + dh * 0.8); } }
      x.font = `${dh * 0.08}px ${MONO}`; x.textAlign = 'left'; x.fillText('FM  MHz', dx + dw * 0.04, dy + dh * 0.2); x.textAlign = 'right'; x.fillText('STEREO', dx + dw * 0.96, dy + dh * 0.2);
      x.restore();
    }), 0, 0, W, H);
    /* the needle: a hot line with a lamp behind it */
    const nx = fx(freq);
    ctx.save(); ctx.beginPath(); ctx.roundRect(dx, dy, dw, dh, dh * 0.06); ctx.clip();
    glow(ctx, T, nx, dy + dh * 0.5, dh * (locked ? 1.1 : 0.7), T.hot, locked ? 0.6 : 0.35);
    ctx.fillStyle = rgba('#000000', 0.25); ctx.fillRect(nx + dh * 0.02, dy, Math.max(3, dh * 0.03), dh);
    ctx.fillStyle = T.hot; ctx.fillRect(nx - Math.max(1.5, dh * 0.012), dy, Math.max(3, dh * 0.025), dh);
    const gl = ctx.createLinearGradient(dx, dy, dx + dw * 0.4, dy + dh); gl.addColorStop(0, rgba('#FFFFFF', 0.28)); gl.addColorStop(0.4, rgba('#FFFFFF', 0)); ctx.fillStyle = gl; ctx.fillRect(dx, dy, dw, dh);
    ctx.restore();
    /* big readout, the strength meter and the tuning knob */
    const ry = dy + dh + U * (portrait ? 0.12 : 0.1);
    ctx.textAlign = 'left'; ctx.textBaseline = 'middle'; ctx.font = `bold ${U * (portrait ? 0.17 : 0.2)}px ${MONO}`;
    const txt = freq.toFixed(1); ctx.fillStyle = rgba(locked ? T.hi : T.c1, T.dark ? 0.35 : 0.2); ctx.fillText(txt, dx + dw * 0.03 + U * 0.006, ry + U * 0.006); ctx.save(); add(ctx, T); ctx.fillStyle = rgba(locked ? T.hi : T.c1, locked ? 1 : 0.8); ctx.shadowColor = rgba(T.c1, 0.9); ctx.shadowBlur = T.dark ? U * 0.03 : 0; ctx.fillText(txt, dx + dw * 0.03, ry); ctx.restore();
    ctx.font = `${U * 0.04}px ${MONO}`; ctx.fillStyle = rgba(T.line, 0.9); ctx.fillText('MHz', dx + dw * 0.03 + U * (portrait ? 0.4 : 0.5), ry + U * 0.05);
    const sg = locked ? 1 : 0.2 + 0.5 * Math.abs(Math.sin(t * 5));
    for (let i = 0; i < 16; i++) { const on = i / 15 < sg; ctx.fillStyle = on ? (i > 12 ? T.hot : T.c1) : rgba(T.line, 0.16); if (on) glow(ctx, T, dx + dw * (portrait ? 0.5 : 0.6) + i * U * 0.032, ry, U * 0.03, T.c1, 0.4); ctx.fillRect(dx + dw * (portrait ? 0.5 : 0.6) + i * U * 0.032 - U * 0.011, ry - U * 0.03 * (0.4 + i / 15), U * 0.022, U * 0.06 * (0.4 + i / 15)); }
    const kx = dx + dw * 0.9, ky = ry + U * (portrait ? 0.02 : 0.03), kr = U * (portrait ? 0.09 : 0.1);
    ctx.fillStyle = rgba('#000000', 0.4); dot(ctx, kx + kr * 0.06, ky + kr * 0.1, kr * 1.05); const kg = ctx.createRadialGradient(kx - kr * 0.3, ky - kr * 0.4, kr * 0.1, kx, ky, kr); kg.addColorStop(0, T.steelHi); kg.addColorStop(1, mix(T.steel, '#000000', 0.55)); ctx.fillStyle = kg; dot(ctx, kx, ky, kr); ctx.strokeStyle = rgba('#000000', 0.7); ctx.lineWidth = Math.max(2, U * 0.004); ctx.stroke();
    const kt = freq * 2.4; ctx.strokeStyle = rgba('#FFFFFF', 0.28); ctx.lineWidth = 1; for (let i = 0; i < 36; i++) { const a = i / 36 * TAU + kt; ctx.beginPath(); ctx.moveTo(kx + Math.cos(a) * kr * 0.84, ky + Math.sin(a) * kr * 0.84); ctx.lineTo(kx + Math.cos(a) * kr * 0.98, ky + Math.sin(a) * kr * 0.98); ctx.stroke(); }
    ctx.strokeStyle = T.hot; ctx.lineWidth = Math.max(3, U * 0.006); ctx.lineCap = 'round'; ctx.beginPath(); ctx.moveTo(kx + Math.cos(kt) * kr * 0.35, ky + Math.sin(kt) * kr * 0.35); ctx.lineTo(kx + Math.cos(kt) * kr * 0.8, ky + Math.sin(kt) * kr * 0.8); ctx.stroke();
  });

  /* captions: say what the thing is, in words, plus a few of the numbers a spec sheet would carry */
  const para = (ctx, text, x, y, maxW, lh) => { let line = '', yy = y; for (const ch of text) { if (ctx.measureText(line + ch).width > maxW && line) { ctx.fillText(line, x, yy); yy += lh; line = ch === ' ' ? '' : ch; } else line += ch; } if (line) { ctx.fillText(line, x, yy); yy += lh; } return yy; };
  const inkOf = T => ({ fg: T.dark ? 'rgba(244,246,255,0.92)' : 'rgba(27,31,51,0.9)', dim: T.dark ? 'rgba(244,246,255,0.58)' : 'rgba(27,31,51,0.6)' });
  const caption = (ctx, T, U, x, y, maxW, zh, en, note, chips, align = 'left') => {
    const I = inkOf(T), fs = Math.max(13, U * 0.036); ctx.save(); ctx.textAlign = align === 'center' ? 'center' : 'left'; ctx.textBaseline = 'alphabetic';
    const ax = align === 'center' ? x + maxW / 2 : x;
    ctx.fillStyle = I.fg; ctx.font = `700 ${fs}px system-ui,-apple-system,"Segoe UI","Microsoft YaHei","PingFang SC",sans-serif`; ctx.fillText(zh, ax, y);
    const w1 = ctx.measureText(zh).width; ctx.fillStyle = rgba(T.c1, 0.95); ctx.font = `700 ${fs * 0.62}px ${MONO}`; if (align !== 'center') ctx.fillText(en, ax + w1 + fs * 0.6, y); else ctx.fillText(en, ax, y + fs * 0.95);
    let yy = y + (align === 'center' ? fs * 1.95 : fs * 1.25);
    if (note) { ctx.fillStyle = I.dim; ctx.font = `500 ${fs * 0.66}px system-ui,-apple-system,"Segoe UI","Microsoft YaHei","PingFang SC",sans-serif`; yy = para(ctx, note, align === 'center' ? ax : x, yy, maxW, fs * 0.9) + fs * 0.1; }
    if (chips && chips.length) {
      ctx.font = `600 ${fs * 0.5}px ${MONO}`; let cx0 = x, cy0 = yy + fs * 0.15; const ch = fs * 1.55, gap = fs * 0.4;
      const ws = chips.map(([k, v]) => { ctx.font = `600 ${fs * 0.46}px system-ui,'Microsoft YaHei','PingFang SC',sans-serif`; const a = ctx.measureText(k).width; ctx.font = `700 ${fs * 0.64}px ${MONO}`; return Math.max(a, ctx.measureText(v).width) + fs * 0.7; });
      if (align === 'center') { const tot = ws.reduce((a, b) => a + b, 0) + gap * (ws.length - 1); if (tot < maxW) cx0 = x + (maxW - tot) / 2; }
      chips.forEach(([k, v], i) => {
        if (cx0 + ws[i] > x + maxW + 1 && cx0 > x) { cx0 = x; cy0 += ch + gap * 0.6; }
        ctx.fillStyle = T.dark ? 'rgba(255,255,255,0.06)' : 'rgba(0,0,0,0.05)'; rr(ctx, cx0, cy0, ws[i], ch, fs * 0.28); ctx.fill(); ctx.strokeStyle = rgba(T.c1, T.dark ? 0.45 : 0.55); ctx.lineWidth = 1; ctx.stroke();
        ctx.textAlign = 'left'; ctx.fillStyle = I.dim; ctx.font = `600 ${fs * 0.46}px system-ui,'Microsoft YaHei','PingFang SC',sans-serif`; ctx.fillText(k, cx0 + fs * 0.35, cy0 + fs * 0.55); ctx.fillStyle = I.fg; ctx.font = `700 ${fs * 0.64}px ${MONO}`; ctx.fillText(v, cx0 + fs * 0.35, cy0 + fs * 1.2); cx0 += ws[i] + gap;
      });
    }
    ctx.restore();
  };

  /* Optical disc study: an original prism-and-spectrum tribute, drawn entirely in canvas. */
  K.registerBg('echoDisc', 'Prism Disc', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, U = Umin(env), portrait = H > W, dark = isDark(sc.bg);
    const R = portrait ? W * 0.39 : Math.min(H * 0.39, W * 0.255);
    const cx = portrait ? W * 0.5 : W * 0.29, cy = H * (portrait ? 0.32 : 0.49);
    const textX = portrait ? W * 0.1 : W * 0.61, textY = H * (portrait ? 0.65 : 0.59);
    const textW = portrait ? W * 0.8 : W * 0.33;
    const ground = dark ? mix(sc.bg, '#06080D', 0.9) : mix(sc.bg, '#F0F1F2', 0.94);
    const ink = dark ? '#ECEEEF' : '#222730', muted = dark ? '#969CA6' : '#606875';
    const spectrum = ['#E75952', '#F29C50', '#E9CD6C', '#65B78C', '#639BCB', '#937AC4'];
    const display = '"Arial Narrow","Helvetica Neue",Arial,sans-serif';
    const key = `dc-prism|${ck(sc)}|${portrait}`;
    ctx.globalAlpha = fade(env, 1);
    ctx.drawImage(layer(env, key, 0.65, x => {
      x.fillStyle = ground; x.fillRect(0, 0, W, H);
      const halo = x.createRadialGradient(cx, cy, R * 0.6, cx, cy, R * 1.3);
      halo.addColorStop(0, rgba('#000000', dark ? 0.32 : 0.18)); halo.addColorStop(1, rgba('#000000', 0));
      x.fillStyle = halo; dot(x, cx, cy + R * 0.03, R * 1.3);
      const disc = x.createLinearGradient(cx - R, cy - R, cx + R, cy + R);
      disc.addColorStop(0, dark ? '#303741' : '#E2E5E9'); disc.addColorStop(0.35, dark ? '#10151D' : '#CBD1D9'); disc.addColorStop(0.78, dark ? '#090C12' : '#BAC2CC'); disc.addColorStop(1, dark ? '#222A35' : '#E7EBEF');
      x.fillStyle = disc; dot(x, cx, cy, R);
      x.strokeStyle = rgba(dark ? '#CAD4E1' : '#657386', 0.13); x.lineWidth = Math.max(1, U * 0.001);
      for (let i = 0; i < 48; i++) { x.beginPath(); x.arc(cx, cy, R * (0.59 + i * 0.0082), 0, TAU); x.stroke(); }
      x.strokeStyle = rgba('#D9E0EB', 0.42); x.lineWidth = Math.max(1, U * 0.0016);
      x.beginPath(); x.arc(cx, cy, R * 0.995, 0, TAU); x.stroke();
      x.strokeStyle = rgba('#D9E0EB', 0.15); x.beginPath(); x.arc(cx, cy, R * 0.56, 0, TAU); x.stroke();

      /* The optical path crosses the label; the spectrum opens into the surrounding space. */
      const top = [cx, cy - R * 0.48], left = [cx - R * 0.46, cy + R * 0.31], right = [cx + R * 0.46, cy + R * 0.31];
      const entry = [cx - R * 0.285, cy + R * 0.012], exit = [cx + R * 0.26, cy - R * 0.035];
      const endX = portrait ? W * 1.05 : W * 1.02;
      const endTop = cy - R * 0.62, endBottom = cy + R * 0.13;
      const path = points => { x.beginPath(); points.forEach(([px, py], i) => i ? x.lineTo(px, py) : x.moveTo(px, py)); x.closePath(); };
      const glass = x.createLinearGradient(left[0], top[1], right[0], left[1]);
      glass.addColorStop(0, rgba('#DDE9F8', 0.1)); glass.addColorStop(0.5, rgba('#DDE9F8', 0.02)); glass.addColorStop(1, rgba('#DDE9F8', 0.14));
      path([top, left, right]); x.fillStyle = glass; x.fill();
      for (let i = 0; i < spectrum.length; i++) {
        const y1 = endTop + (endBottom - endTop) * i / spectrum.length;
        const y2 = endTop + (endBottom - endTop) * (i + 1) / spectrum.length;
        const startY = exit[1] + i * R * 0.011;
        const tint = x.createLinearGradient(exit[0], 0, endX, 0);
        tint.addColorStop(0, rgba(spectrum[i], 0.94)); tint.addColorStop(0.58, rgba(spectrum[i], dark ? 0.76 : 0.84)); tint.addColorStop(1, rgba(spectrum[i], dark ? 0.18 : 0.34));
        path([[exit[0], startY], [endX, y1], [endX, y2], [exit[0], startY + R * 0.012]]);
        x.fillStyle = tint; x.fill();
      }
      x.lineCap = 'round'; x.strokeStyle = dark ? '#F0F2F5' : '#444E5C'; x.lineWidth = Math.max(1.5, U * 0.0025);
      x.beginPath(); x.moveTo(-W * 0.02, cy + R * 0.34); x.lineTo(...entry); x.stroke();
      x.strokeStyle = rgba(dark ? '#E9EEF6' : '#444E5C', 0.45); x.lineWidth = Math.max(1, U * 0.0014);
      x.beginPath(); x.moveTo(...entry); x.lineTo(...exit); x.stroke();
      path([top, left, right]); x.strokeStyle = dark ? '#E3E9F0' : '#444E5C'; x.lineWidth = Math.max(1.2, U * 0.002); x.stroke();
      x.strokeStyle = rgba('#FFFFFF', 0.2); x.lineWidth = Math.max(1, U * 0.001);
      x.beginPath(); x.moveTo(top[0] + R * 0.018, top[1] + R * 0.045); x.lineTo(right[0] - R * 0.025, right[1] - R * 0.025); x.stroke();
      x.fillStyle = ground; dot(x, cx, cy + R * 0.2, R * 0.055);
      x.strokeStyle = rgba('#C3CCD7', 0.4); x.lineWidth = 1; x.stroke();

    }), 0, 0, W, H);
    /* Quiet English typography; reference specs describe standard CD audio, not the playing file. */
    ctx.textAlign = 'left'; ctx.textBaseline = 'alphabetic'; ctx.fillStyle = muted;
    ctx.font = `500 ${Math.max(9, U * 0.013)}px ${MONO}`;
    ctx.fillText('ECHO  /  OPTICAL SERIES', W * 0.055, H * 0.07);
    const labelSize = Math.max(10, U * 0.014), titleSize = Math.min(U * (portrait ? 0.062 : 0.066), textW / 7.4);
    ctx.font = `500 ${labelSize}px ${MONO}`; ctx.fillStyle = muted;
    ctx.fillText('LIGHT INTO SOUND', textX, textY);
    ctx.fillStyle = ink; ctx.font = `300 ${titleSize}px ${display}`;
    ctx.fillText('COMPACT DISC', textX, textY + titleSize * 1.35);
    ctx.fillStyle = muted; ctx.font = `400 ${Math.max(13, U * 0.019)}px ${display}`;
    ctx.fillText('Read by light. Heard in detail.', textX, textY + titleSize * 1.35 + U * 0.04);
    const specY = textY + titleSize * 1.35 + U * 0.09;
    ctx.strokeStyle = rgba(ink, 0.18); ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(textX, specY); ctx.lineTo(textX + textW, specY); ctx.stroke();
    const specs = [['CD STANDARD', '44.1 kHz'], ['RESOLUTION', '16 BIT'], ['CHANNELS', 'STEREO']];
    specs.forEach(([label, value], i) => {
      const sx = textX + textW * i / specs.length;
      ctx.fillStyle = muted; ctx.font = `400 ${Math.max(9, U * 0.011)}px ${MONO}`; ctx.fillText(label, sx, specY + U * 0.025);
      ctx.fillStyle = ink; ctx.font = `500 ${Math.max(12, U * 0.017)}px ${MONO}`; ctx.fillText(value, sx, specY + U * 0.052);
    });
    /* One small moving reflection, with no extra canvas or retained animation state. */
    const angle = env.t * 0.12 - Math.PI * 0.68;
    ctx.strokeStyle = rgba('#ECF2FA', 0.5); ctx.lineWidth = Math.max(1, U * 0.002);
    ctx.beginPath(); ctx.arc(cx, cy, R * 0.982, angle, angle + 0.16); ctx.stroke();
  }, false);

  /* ================= 光のリボン — silk ribbons of light that twist to the beat ================= */
  K.registerBg('echoRibbon', '光のリボン', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `rb|${seed}|${ck(sc)}`;
    const cy = H * (portrait ? 0.4 : 0.44), pk = beat(t, 96), cols = [T.c1, T.c2, T.hi, T.c3, T.c1, T.c2, T.hi];
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, x => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      for (let i = 0; i < 7; i++) glowDisc(x, W * (0.1 + 0.8 * r(seed, i, 1)), H * (0.15 + 0.7 * r(seed, i, 2)), U * (0.25 + 0.35 * r(seed, i, 3)), i % 2 ? T.c1 : T.c2, T.dark ? 0.16 : 0.2);
    }), 0, 0, W, H);
    ctx.save(); add(ctx, T);
    const N = portrait ? 70 : 110;
    for (let k = 0; k < 7; k++) {
      const ph = k * 0.9 + seed, a1 = 0.16 + 0.05 * k, sp = 0.35 + 0.08 * k, wmax = U * (0.05 + 0.012 * k) * (1 + 0.45 * pk), yo = (k - 3) * U * 0.028, c = cols[k];
      const yy = x => cy + yo + Math.sin(x * 3.1 + t * sp + ph) * H * a1 * 0.55 + Math.sin(x * 6.7 - t * sp * 1.7 + ph * 2) * H * 0.045 * (0.5 + pk);
      const ww = x => wmax * Math.sin(x * 7.5 + t * 0.9 * sp + ph * 1.3);
      const up = [], dn = []; for (let i = 0; i <= N; i++) { const x = i / N, X = x * W * 1.1 - W * 0.05; up.push([X, yy(x) + ww(x)]); dn.push([X, yy(x) - ww(x)]); }
      const g = ctx.createLinearGradient(0, 0, W, 0); g.addColorStop(0, rgba(c, 0)); g.addColorStop(0.2, rgba(c, T.dark ? 0.42 : 0.5)); g.addColorStop(0.55, rgba(mix(c, '#FFFFFF', 0.35), T.dark ? 0.5 : 0.55)); g.addColorStop(0.85, rgba(c, T.dark ? 0.4 : 0.5)); g.addColorStop(1, rgba(c, 0));
      ctx.fillStyle = g; ctx.beginPath(); up.forEach(([X, Y], i) => i ? ctx.lineTo(X, Y) : ctx.moveTo(X, Y)); for (let i = N; i >= 0; i--) ctx.lineTo(dn[i][0], dn[i][1]); ctx.closePath(); ctx.fill();
      ctx.lineWidth = Math.max(1, U * 0.0024); ctx.strokeStyle = rgba(mix(c, '#FFFFFF', 0.6), T.dark ? 0.75 : 0.7); ctx.beginPath(); up.forEach(([X, Y], i) => i ? ctx.lineTo(X, Y) : ctx.moveTo(X, Y)); ctx.stroke(); ctx.beginPath(); dn.forEach(([X, Y], i) => i ? ctx.lineTo(X, Y) : ctx.moveTo(X, Y)); ctx.stroke();
    }
    for (let i = 0; i < 48; i++) { const x = W * frac(r(seed, i, 5) + t * 0.008 * (0.4 + r(seed, i, 6))), y = H * (0.1 + 0.8 * r(seed, i, 7)) + Math.sin(t * 0.6 + i) * U * 0.02, tw = 0.4 + 0.6 * Math.abs(Math.sin(t * (0.6 + r(seed, i, 8)) + i)); ctx.fillStyle = rgba(T.hi, 0.7 * tw); dot(ctx, x, y, U * (0.0016 + 0.003 * r(seed, i, 9))); }
    ctx.restore();
    caption(ctx, T, U, W * 0.06, H * (portrait ? 0.78 : 0.76), W * 0.88, '音频波形', 'AUDIO WAVEFORM', portrait ? '声音是空气的振动，每一个瞬间的高低连成一条波。' : '声音其实是空气的振动。把每一瞬间的振幅连起来，就是你看到的这些流动的波形；低音越重，光带扭得越厉害。', [['采样率', '96 kHz'], ['位深', '32-bit float'], ['声道', '立体声'], ['峰值', '-0.3 dBFS']]);
    ctx.globalAlpha = 1;
  });

  /* ================= ヘッドホン — a hero shot of a pair of headphones, rim-lit from both sides, breathing with the beat ================= */
  K.registerBg('echoHeadphones', 'ヘッドホン', ['calm', 'emotional', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones(sc), portrait = H > W, key = `hp|${seed}|${ck(sc)}`;
    const R = portrait ? W * 0.34 : Math.min(W * 0.2, H * 0.3), cx = portrait ? W / 2 : W * 0.34, cy = portrait ? H * 0.34 : H * 0.42, pk = beat(t, 96), cL = T.c1, cR = T.c2;
    const bandY = cy + R * 0.12, cupW = R * 0.44, cupH = R * 0.98, cupY = bandY + R * 0.28, floor = cupY + cupH * 0.62;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.55, x => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx - R * 1.2, cy, U * 0.7, cL, T.dark ? 0.22 : 0.2); glowDisc(x, cx + R * 1.2, cy, U * 0.7, cR, T.dark ? 0.22 : 0.2);
      for (let i = 0; i < 14; i++) { const bx = W * r(seed, i, 1), by = H * (0.05 + 0.75 * r(seed, i, 2)), br = U * (0.02 + 0.05 * r(seed, i, 3)); x.fillStyle = rgba(i % 2 ? cL : cR, T.dark ? 0.09 : 0.14); dot(x, bx, by, br); x.strokeStyle = rgba(i % 2 ? cL : cR, 0.16); x.lineWidth = 1; x.beginPath(); x.arc(bx, by, br, 0, TAU); x.stroke(); }
      const fg = x.createRadialGradient(cx, floor + R * 0.15, 0, cx, floor + R * 0.15, R * 2.2); fg.addColorStop(0, rgba('#000000', T.dark ? 0.55 : 0.2)); fg.addColorStop(1, rgba('#000000', 0)); x.save(); x.translate(cx, floor + R * 0.15); x.scale(1, 0.2); x.translate(-cx, -(floor + R * 0.15)); x.fillStyle = fg; x.fillRect(cx - R * 2.4, floor - R * 2.4, R * 4.8, R * 4.8); x.restore();
    }), 0, 0, W, H);
    /* sound rings breathing out of the cups */
    ctx.save(); add(ctx, T);
    for (const [sx, c, dir] of [[cx - R, cL, -1], [cx + R, cR, 1]]) for (let k = 0; k < 4; k++) { const p = frac(t * 0.45 + k / 4), rr2 = R * (0.3 + 1.5 * p); ctx.strokeStyle = rgba(c, (1 - p) * (T.dark ? 0.55 : 0.7) * (0.6 + 0.6 * pk)); ctx.lineWidth = Math.max(1.5, U * 0.004 * (1 - p)); ctx.beginPath(); ctx.ellipse(sx + dir * cupW * 0.4, cupY, rr2 * 0.55, rr2, 0, -Math.PI / 2 + (dir > 0 ? 0 : Math.PI), Math.PI / 2 + (dir > 0 ? 0 : Math.PI)); ctx.stroke(); }
    ctx.restore();
    /* the band */
    const metal = (x0, y0, x1, y1, a, b, c2) => { const g = ctx.createLinearGradient(x0, y0, x1, y1); g.addColorStop(0, a); g.addColorStop(0.5, b); g.addColorStop(1, c2); return g; };
    ctx.save(); ctx.lineCap = 'round';
    ctx.strokeStyle = rgba('#000000', 0.4); ctx.lineWidth = R * 0.12; ctx.beginPath(); ctx.ellipse(cx, bandY + R * 0.03, R, R * 1.1, 0, Math.PI, TAU); ctx.stroke();
    ctx.strokeStyle = metal(cx - R, cy - R, cx + R, cy, T.dark ? '#4A505C' : '#F4F6FA', T.dark ? '#1A1D24' : '#B9C0CC', T.dark ? '#3C424E' : '#E4E8EF'); ctx.lineWidth = R * 0.1; ctx.beginPath(); ctx.ellipse(cx, bandY, R, R * 1.1, 0, Math.PI, TAU); ctx.stroke();
    ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.45 : 0.85); ctx.lineWidth = Math.max(1.5, R * 0.014); ctx.beginPath(); ctx.ellipse(cx, bandY - R * 0.03, R, R * 1.1, 0, Math.PI * 1.06, Math.PI * 1.7); ctx.stroke();
    ctx.strokeStyle = metal(cx - R, 0, cx + R, 0, cL, T.dark ? '#FFFFFF' : '#FFFFFF', cR); ctx.globalAlpha = e * (0.7 + 0.3 * pk); ctx.lineWidth = Math.max(2, R * 0.016); ctx.beginPath(); ctx.ellipse(cx, bandY + R * 0.06, R * 0.985, R * 1.09, 0, Math.PI * 1.02, Math.PI * 1.98); ctx.stroke(); ctx.globalAlpha = e;
    ctx.strokeStyle = T.dark ? '#0B0D11' : '#7C8594'; ctx.lineWidth = R * 0.075; ctx.beginPath(); ctx.ellipse(cx, bandY + R * 0.08, R * 0.7, R * 0.78, 0, Math.PI * 1.15, Math.PI * 1.85); ctx.stroke();
    /* cups */
    for (const [sx, c, dir] of [[cx - R, cL, -1], [cx + R, cR, 1]]) {
      ctx.save(); ctx.translate(sx, cupY);
      ctx.fillStyle = rgba('#000000', 0.35); rr(ctx, -cupW * 0.5 + 4, -cupH * 0.5 + 8, cupW, cupH, cupW * 0.45); ctx.fill();
      ctx.fillStyle = metal(-cupW / 2, 0, cupW / 2, 0, T.dark ? '#2C313B' : '#F4F6FA', T.dark ? '#12151B' : '#C3CAD6', T.dark ? '#242832' : '#E9EDF3'); rr(ctx, -cupW / 2, -cupH / 2, cupW, cupH, cupW * 0.45); ctx.fill();
      ctx.strokeStyle = rgba('#FFFFFF', T.dark ? 0.3 : 0.9); ctx.lineWidth = 1.5; ctx.stroke();
      ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(c, 0.85 * (0.75 + 0.25 * pk)); ctx.lineWidth = Math.max(2, R * 0.03); rr(ctx, -cupW / 2 + cupW * 0.08, -cupH / 2 + cupW * 0.08, cupW * 0.84, cupH - cupW * 0.16, cupW * 0.38); ctx.stroke(); glow(ctx, T, dir * cupW * 0.3, 0, R * 0.9, c, 0.5 + 0.3 * pk); ctx.restore();
      ctx.fillStyle = rgba(T.dark ? '#000000' : '#FFFFFF', 0.35); dot(ctx, 0, 0, cupW * 0.28); ctx.strokeStyle = rgba(c, 0.8); ctx.lineWidth = 1.5; ctx.beginPath(); ctx.arc(0, 0, cupW * 0.28, 0, TAU); ctx.stroke(); ctx.fillStyle = rgba(c, 0.5 + 0.5 * pk); dot(ctx, 0, 0, cupW * 0.09);
      ctx.fillStyle = T.dark ? '#08090C' : '#5E6674'; rr(ctx, dir > 0 ? -cupW / 2 - cupW * 0.28 : cupW / 2 - cupW * 0.02, -cupH * 0.42, cupW * 0.3, cupH * 0.84, cupW * 0.15); ctx.fill();
      ctx.restore();
    }
    ctx.restore();
    caption(ctx, T, U, portrait ? W * 0.07 : W * 0.6, portrait ? H * 0.68 : H * 0.34, portrait ? W * 0.86 : W * 0.34, '头戴式耳机', 'HEADPHONES', portrait ? '' : '音乐从这里直接送进耳朵。驱动单元越大、阻抗和灵敏度配合得越好，声音就越通透。', [['驱动单元', '50 mm'], ['阻抗', '32 Ω'], ['灵敏度', '98 dB/mW'], ['频响', '5 Hz – 40 kHz'], ['采样率', '96 kHz']]);
    ctx.globalAlpha = 1;
  });

})();
