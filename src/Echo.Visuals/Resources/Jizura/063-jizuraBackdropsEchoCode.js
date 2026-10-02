/* ECHO first-party backdrops for the 写代码 and 高等数学 looks: コードエディタ, ターミナル, ブランチ, 黒板, フーリエ円, 曲面 + two drifting fronts.
   Original artwork only. Every colour comes from the current scheme. */
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

  /* ================= コードエディタ ================= */
  const NAMES = ['solve', 'dfs', 'bfs', 'mid', 'lo', 'hi', 'ans', 'node', 'cache', 'queue', 'total', 'idx', 'graph', 'dp', 'best', 'tmp', 'left', 'right', 'seen', 'count'];
  const TYPES = ['int', 'long', 'auto', 'bool', 'size_t', 'double'];
  const FNS = ['solve', 'dfs', 'max', 'min', 'push', 'find', 'merge', 'build', 'query', 'update', 'sort', 'abs'];
  const NOTES = ['binary search on the answer', 'O(n log n)', 'handle the empty case', 'monotone stack', 'fast path', 'rebuild the index', 'tighten the bound', 'memoise this'];
  const genCode = (seed, n) => {
    const lines = []; let depth = 0;
    for (let li = 0; li < n; li++) {
      let k = 0; const R = () => r(seed, 300 + li, ++k), pk = arr => arr[Math.floor(R() * arr.length) % arr.length];
      const nm = () => pk(NAMES), num = () => String(1 + Math.floor(R() * R() * 64)), w = R();
      let toks; let open = 0;
      if (R() < 0.05) toks = [];
      else if (depth >= 3 && w < 0.55) { toks = [['}', 'pun']]; depth--; }
      else if (w < 0.13) { toks = [['for', 'kw'], [' (', 'pun'], [pk(TYPES), 'ty'], [' i = 0; i < ', 'pun'], [nm(), 'id'], ['; i++) {', 'pun']]; open = 1; }
      else if (w < 0.21) { toks = [['while', 'kw'], [' (', 'pun'], [nm(), 'id'], [' < ', 'pun'], [nm(), 'id'], [') {', 'pun']]; open = 1; }
      else if (w < 0.31) { toks = [['if', 'kw'], [' (', 'pun'], [nm(), 'id'], [' > ', 'pun'], [num(), 'num'], [') {', 'pun']]; open = 1; }
      else if (w < 0.53) { toks = [[pk(TYPES), 'ty'], [' ', 'pun'], [nm(), 'id'], [' = ', 'pun'], [pk(FNS), 'fn'], ['(', 'pun'], [nm(), 'id'], [', ', 'pun'], [num(), 'num'], [');', 'pun']]; }
      else if (w < 0.63) { toks = [['// ', 'com'], [pk(NOTES), 'com']]; }
      else if (w < 0.72) { toks = [['return', 'kw'], [' ', 'pun'], [nm(), 'id'], [' + ', 'pun'], [pk(FNS), 'fn'], ['(', 'pun'], [nm(), 'id'], [');', 'pun']]; }
      else if (w < 0.82) { toks = [[nm(), 'id'], ['[', 'pun'], ['i', 'id'], ['] = ', 'pun'], [num(), 'num'], [';', 'pun']]; }
      else if (w < 0.88) { toks = [['const', 'kw'], [' ', 'pun'], [nm(), 'id'], [' = ', 'pun'], ['"' + pk(NAMES) + '"', 'str'], [';', 'pun']]; }
      else if (depth > 0) { toks = [['}', 'pun']]; depth--; }
      else toks = [[pk(TYPES), 'ty'], [' ', 'pun'], [nm(), 'id'], [' = ', 'pun'], [num(), 'num'], [';', 'pun']];
      lines.push({ d: depth, toks });
      depth += open;
    }
    return lines;
  };
  const kindCol = (T, kd) => ({ kw: T.c4, fn: T.c2, str: T.c1, num: T.warm, ty: mix(T.c2, T.c1, 0.5), com: T.dim, pun: mix(T.fg, T.dim, 0.35), id: T.fg })[kd] || T.fg;

  K.registerBg('echoCodeEditor', 'コードエディタ', ['calm', 'glitch', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `ce|${seed}|${ck(sc)}`;
    const fs = Math.max(11, U * (portrait ? 0.05 : 0.038)), lh = fs * 1.62, nL = Math.max(36, Math.ceil(H * 1.5 / lh)), Hd = nL * lh;
    ctx.globalAlpha = e;
    const S = strip(env, `${key}|s${Math.round(fs)}`, W, Hd, 0.95, (x) => {
      x.font = `${fs}px ${MONO}`; x.textBaseline = 'alphabetic'; const cw = x.measureText('M').width, gw = cw * 4.2, code = genCode(seed, nL), ends = [];
      code.forEach((ln, i) => {
        const y = i * lh + lh * 0.72; x.fillStyle = rgba(T.dim, 0.7); x.textAlign = 'right'; x.fillText(String(i + 1 + (seed % 40) * 3), gw - cw * 1.2, y);
        x.textAlign = 'left'; let cx = gw + cw * 1.2 + ln.d * cw * 2;
        for (const [txt, kd] of ln.toks) { x.fillStyle = kindCol(T, kd); x.fillText(txt, cx, y); cx += txt.length * cw; }
        ends.push((cx - gw - cw * 1.2) / cw);
      });
      return { cw, gw, ends };
    });
    const { cw, gw, ends } = S.meta, tabH = lh * 1.5, stH = lh * 1.25, mw = portrait ? 0 : Math.min(W * 0.1, cw * 12);
    /* editor body */
    ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.62) : mix(sc.bg, '#FFFFFF', 0.55); ctx.fillRect(0, 0, W, H);
    const step = t / 1.25, k = Math.floor(step), ph = step - k, o = ((k + ease(clamp(ph * 3.2))) * lh) % Hd;
    ctx.save(); ctx.beginPath(); ctx.rect(0, tabH, W - mw, H - tabH - stH); ctx.clip();
    /* gutter shade + current-line bar + indent guides */
    ctx.fillStyle = rgba('#000000', T.dark ? 0.22 : 0.04); ctx.fillRect(0, tabH, gw, H);
    const rb = Math.floor((H * 0.55) / lh), by = rb * lh, li = (k + rb) % nL;
    ctx.fillStyle = rgba(T.c2, T.dark ? 0.12 : 0.1); ctx.fillRect(0, by, W - mw, lh); ctx.fillStyle = rgba(T.c2, 0.6); ctx.fillRect(gw - 2, by, 2, lh);
    ctx.drawImage(S.cv, 0, -o, W, Hd); ctx.drawImage(S.cv, 0, Hd - o, W, Hd);
    /* squiggle under a token a few lines above the cursor */
    { const sy = by - lh * 3 + lh * 0.86, sx = gw + cw * 6; ctx.strokeStyle = rgba(T.hot, 0.9); ctx.lineWidth = Math.max(1, U * 0.0025); ctx.beginPath(); for (let i = 0; i <= 14; i++) { const xx = sx + i * cw * 0.5; const yy = sy + (i % 2 ? 0 : -lh * 0.09); i ? ctx.lineTo(xx, yy) : ctx.moveTo(xx, yy); } ctx.stroke(); }
    ctx.restore();
    /* caret + autocomplete */
    const cxp = gw + cw * 1.2 + ends[li] * cw + cw * 0.15, blink = Math.floor(t * 2) % 2 === 0;
    if (blink) { ctx.save(); add(ctx, T); ctx.fillStyle = rgba(T.hi, 0.95); ctx.fillRect(cxp, by + lh * 0.12, Math.max(2, cw * 0.14), lh * 0.76); ctx.restore(); }
    if (ph > 0.42 && ph < 0.96) {
      const rows = 4, pw = cw * 15, pxx = Math.min(cxp + cw, W - mw - pw - cw), pyy = by + lh * 1.05, sel = Math.floor(t / 1.25) % rows;
      ctx.fillStyle = T.dark ? mix(sc.bg, '#FFFFFF', 0.08) : mix(sc.bg, '#000000', 0.06); ctx.strokeStyle = rgba(T.line, 0.4); ctx.lineWidth = 1; rr(ctx, pxx, pyy, pw, lh * rows + 4, 3); ctx.fill(); ctx.stroke();
      ctx.font = `${fs * 0.92}px ${MONO}`; ctx.textAlign = 'left';
      for (let i = 0; i < rows; i++) {
        if (i === sel) { ctx.fillStyle = rgba(T.c2, 0.32); ctx.fillRect(pxx + 2, pyy + 2 + i * lh, pw - 4, lh); }
        ctx.fillStyle = [T.c2, T.c1, T.c4, T.warm][i]; ctx.fillRect(pxx + cw * 0.5, pyy + 2 + i * lh + lh * 0.3, cw * 0.7, lh * 0.4);
        ctx.fillStyle = T.fg; ctx.fillText(pickr(seed, k, 40 + i, NAMES.concat(FNS)), pxx + cw * 1.9, pyy + 2 + i * lh + lh * 0.7);
      }
    }
    /* minimap */
    if (mw > 0) {
      ctx.fillStyle = rgba('#000000', T.dark ? 0.2 : 0.04); ctx.fillRect(W - mw, tabH, mw, H - tabH - stH);
      const sc2 = mw / W * 0.9, mh = Hd * sc2; ctx.save(); ctx.beginPath(); ctx.rect(W - mw, tabH, mw, H - tabH - stH); ctx.clip(); ctx.globalAlpha = e * 0.75;
      for (let m = -1; m <= 1; m++) ctx.drawImage(S.cv, W - mw + mw * 0.05, tabH + m * mh - o * sc2, W * sc2, mh);
      ctx.globalAlpha = e; ctx.fillStyle = rgba(T.fg, 0.08); ctx.fillRect(W - mw, tabH, mw, (H - tabH - stH) * 0.62 * 1); ctx.restore();
    }
    /* tab bar + status bar */
    ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.78) : mix(sc.bg, '#000000', 0.06); ctx.fillRect(0, 0, W, tabH);
    ctx.font = `${fs * 0.86}px ${MONO}`; ctx.textAlign = 'left'; let tx = 0;
    ['solve.cpp', 'graph.ts', 'notes.md'].forEach((nm2, i) => { const w2 = cw * 0.86 * (nm2.length + 5); ctx.fillStyle = i === 0 ? (T.dark ? mix(sc.bg, '#000000', 0.62) : mix(sc.bg, '#FFFFFF', 0.55)) : rgba('#000000', 0); ctx.fillRect(tx, 0, w2, tabH); if (i === 0) { ctx.fillStyle = T.c2; ctx.fillRect(tx, 0, w2, 2); } ctx.fillStyle = i === 0 ? T.fg : T.dim; ctx.fillText(nm2, tx + cw, tabH * 0.64); tx += w2; });
    ctx.fillStyle = mix(T.c2, T.dark ? '#000000' : '#FFFFFF', T.dark ? 0.45 : 0.15); ctx.fillRect(0, H - stH, W, stH);
    ctx.fillStyle = T.dark ? '#EAF2FF' : '#0B1B33'; ctx.font = `${fs * 0.82}px ${MONO}`; ctx.fillText(`main  ✓ 0  ⚠ 1   Ln ${(k + rb) % 900 + 12}, Col ${Math.round(ends[li]) + 1}   UTF-8   LF   C++`, cw, H - stH * 0.3);
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.4, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.55 : 0.18)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
  });

  /* ================= ターミナル ================= */
  K.registerBg('echoTerminal', 'ターミナル', ['glitch', 'graphic', 'calm'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `tm|${seed}|${ck(sc)}`;
    const fs = Math.max(11, U * (portrait ? 0.05 : 0.04)), lh = fs * 1.5, nL = Math.max(40, Math.ceil(H * 1.5 / lh)), Hd = nL * lh, bar = lh * 1.7;
    ctx.globalAlpha = e;
    const S = strip(env, `${key}|s${Math.round(fs)}`, W, Hd, 0.95, (x) => {
      x.font = `${fs}px ${MONO}`; x.textBaseline = 'alphabetic'; const cw = x.measureText('M').width; let pct = 3; const files = ['solver', 'graph', 'heap', 'io', 'segtree', 'fenwick', 'geometry', 'main', 'bitset', 'lca'];
      for (let i = 0; i < nL; i++) {
        const y = i * lh + lh * 0.72, w = r(seed, 700 + i, 1); let parts;
        if (w < 0.62) { pct = Math.min(100, pct + 1 + Math.floor(r(seed, 700 + i, 2) * 4)); parts = [[`[${String(pct).padStart(3)}%] `, 'pun'], ['Building ', 'id'], [`src/${pickr(seed, i, 3, files)}.cpp.o`, 'fn']]; }
        else if (w < 0.78) parts = [[' ✓ ', 'ok'], [`test_${pickr(seed, i, 4, files)}`, 'id'], [` .... ${Math.floor(3 + r(seed, i, 5) * 90)} ms`, 'com']];
        else if (w < 0.86) parts = [['warning: ', 'warn'], ['unused variable ', 'id'], [`'${pickr(seed, i, 6, files)}'`, 'str']];
        else if (w < 0.9) parts = [['error: ', 'err'], ['expected ; before ', 'id'], ['}', 'str']];
        else parts = [['$ ', 'ok'], [pickr(seed, i, 7, ['make -j8', './run < in.txt', 'git status', 'ctest', 'ninja']), 'id']];
        let cx = cw;
        for (const [txt, kd] of parts) { x.fillStyle = ({ ok: T.c1, warn: T.warm, err: T.hot, pun: T.dim, com: T.dim, fn: T.c2, str: T.c4, id: T.fg })[kd]; x.fillText(txt, cx, y); cx += txt.length * cw; }
      }
      return { cw };
    });
    const cw = S.meta.cw;
    ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.72) : mix(sc.bg, '#FFFFFF', 0.6); ctx.fillRect(0, 0, W, H);
    const o = (t * lh * 1.7) % Hd;
    const logBot = H - lh * 3.4; ctx.save(); ctx.beginPath(); ctx.rect(0, bar, W, logBot - bar); ctx.clip(); ctx.globalAlpha = e * 0.92; ctx.drawImage(S.cv, 0, -o + bar, W, Hd); ctx.drawImage(S.cv, 0, Hd - o + bar, W, Hd); ctx.restore(); ctx.globalAlpha = e;
    /* fade the text toward the top so the log feels like it is emerging */
    { const g = ctx.createLinearGradient(0, bar, 0, H * 0.4); g.addColorStop(0, rgba(T.dark ? mix(sc.bg, '#000000', 0.72) : mix(sc.bg, '#FFFFFF', 0.6), 0.95)); g.addColorStop(1, rgba(T.bg0, 0)); ctx.fillStyle = g; ctx.fillRect(0, bar, W, H * 0.4 - bar); }
    { const g = ctx.createLinearGradient(0, logBot - lh * 2, 0, logBot); g.addColorStop(0, rgba(T.dark ? mix(sc.bg, '#000000', 0.72) : mix(sc.bg, '#FFFFFF', 0.6), 0)); g.addColorStop(1, rgba(T.dark ? mix(sc.bg, '#000000', 0.72) : mix(sc.bg, '#FFFFFF', 0.6), 1)); ctx.fillStyle = g; ctx.fillRect(0, logBot - lh * 2, W, lh * 2); ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.72) : mix(sc.bg, '#FFFFFF', 0.6); ctx.fillRect(0, logBot, W, H - logBot); ctx.fillStyle = rgba(T.line, 0.25); ctx.fillRect(0, logBot, W, 1); }
    /* progress bar */
    const pct = frac(t / 16), py = H - lh * 3.05, bw = W - cw * 12, cells = Math.floor(bw / (cw * 0.9));
    ctx.font = `${fs}px ${MONO}`; ctx.textAlign = 'left'; ctx.fillStyle = T.fg; ctx.fillText('build', cw, py + lh * 0.7);
    for (let i = 0; i < cells; i++) { const on = i / cells < pct; ctx.fillStyle = on ? rgba(T.c1, 0.9) : rgba(T.dim, 0.35); ctx.fillRect(cw * 7 + i * cw * 0.9, py + lh * 0.15, cw * 0.62, lh * 0.7); }
    ctx.save(); add(ctx, T); glow(ctx, T, cw * 7 + pct * cells * cw * 0.9, py + lh * 0.5, U * 0.1, T.hi, 0.6); ctx.restore();
    ctx.fillStyle = T.fg; ctx.fillText(`${Math.floor(pct * 100)}%`, W - cw * 4.8, py + lh * 0.7);
    /* prompt with blinking block cursor */
    ctx.fillStyle = T.c1; ctx.fillText('$', cw, H - lh * 1.1); ctx.fillStyle = T.fg; ctx.fillText('make test', cw * 3, H - lh * 1.1);
    if (Math.floor(t * 2) % 2 === 0) { ctx.save(); add(ctx, T); ctx.fillStyle = rgba(T.hi, 0.9); ctx.fillRect(cw * 12.2, H - lh * 1.1 - lh * 0.62, cw * 0.62, lh * 0.8); ctx.restore(); }
    /* title bar */
    ctx.fillStyle = T.dark ? mix(sc.bg, '#000000', 0.86) : mix(sc.bg, '#000000', 0.07); ctx.fillRect(0, 0, W, bar);
    [T.hot, T.warm, T.c1].forEach((c, i) => { ctx.fillStyle = rgba(c, 0.85); dot(ctx, cw * (1.6 + i * 1.7), bar / 2, bar * 0.18); });
    ctx.font = `${fs * 0.85}px ${MONO}`; ctx.textAlign = 'center'; ctx.fillStyle = T.dim; ctx.fillText('~/contest  —  build', W / 2, bar * 0.64);
    { const vg = ctx.createRadialGradient(W / 2, H / 2, U * 0.4, W / 2, H / 2, Math.hypot(W, H) * 0.62); vg.addColorStop(0, rgba(T.bg0, 0)); vg.addColorStop(1, rgba(T.bg0, T.dark ? 0.5 : 0.15)); ctx.fillStyle = vg; ctx.fillRect(0, 0, W, H); }
    /* faint scanlines */
    if (T.dark) { ctx.fillStyle = 'rgba(0,0,0,0.12)'; for (let y = 0; y < H; y += 3) ctx.fillRect(0, y, W, 1); }
  });

  /* ================= ブランチ ================= */
  K.registerBg('echoGitGraph', 'ブランチ', ['calm', 'pop', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `gg|${seed}|${ck(sc)}`;
    const lanes = 5, ly = i => H * (0.24 + 0.13 * i), step = U * (portrait ? 0.2 : 0.16), speed = U * 0.05, nr = U * 0.014, cols = [T.c1, T.c2, T.c4, T.warm, T.hi];
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba(T.line, 0.09); x.lineWidth = 1; x.beginPath(); for (let i = 0; i < lanes; i++) { x.moveTo(0, ly(i)); x.lineTo(W, ly(i)); } x.stroke();
    }), 0, 0, W, H);
    const off = t * speed, c0 = Math.floor(off / step) - 3, c1 = c0 + Math.ceil(W / step) + 8;
    const xOf = c => c * step - off;
    ctx.save(); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    const nodes = [];
    /* trunk */
    ctx.strokeStyle = rgba(cols[0], 0.85); ctx.lineWidth = Math.max(2, U * 0.006); ctx.beginPath(); ctx.moveTo(xOf(c0), ly(0)); ctx.lineTo(xOf(c1), ly(0)); ctx.stroke();
    for (let c = c0; c <= c1; c++) nodes.push([xOf(c), ly(0), 0, false]);
    /* branches: each column may open one, on lane 1 + c mod 4, living 2-3 columns, then merging back */
    for (let c = c0 - 6; c <= c1; c++) {
      const rc = ((c % 4) + 4) % 4, seedc = c * 7 + 3; if (r(seed, seedc, 1) > 0.62) continue;
      const lane = 1 + rc, len = 2 + Math.floor(r(seed, seedc, 2) * 2), col = cols[lane % cols.length], sx = xOf(c), ex = xOf(c + len + 1), sy = ly(0), yy = ly(lane);
      ctx.strokeStyle = rgba(col, 0.85); ctx.lineWidth = Math.max(2, U * 0.0055);
      ctx.beginPath(); ctx.moveTo(sx, sy); ctx.bezierCurveTo(sx + step * 0.45, sy, sx + step * 0.1, yy, sx + step * 0.6, yy); ctx.lineTo(xOf(c + len + 0.4), yy); ctx.bezierCurveTo(xOf(c + len + 0.4) + step * 0.5, yy, ex - step * 0.5, sy, ex, sy); ctx.stroke();
      for (let j = 1; j <= len; j++) nodes.push([xOf(c + j * 0.9 + 0.1), yy, lane, false]);
      nodes.push([ex, sy, 0, true]);
      if (r(seed, seedc, 3) > 0.5 && sx > -step && sx < W) { ctx.font = `${Math.max(9, U * 0.022)}px ${MONO}`; ctx.textAlign = 'left'; const nm = (['feat/', 'fix/', 'perf/', 'chore/'])[Math.floor(r(seed, seedc, 4) * 4)] + pickr(seed, seedc, 5, ['dp', 'lca', 'io', 'flow', 'sieve', 'hash']); const tw2 = ctx.measureText(nm).width + U * 0.03; ctx.fillStyle = rgba(col, 0.2); rr(ctx, sx + step * 0.7, yy - U * 0.05, tw2, U * 0.032, U * 0.016); ctx.fill(); ctx.fillStyle = col; ctx.fillText(nm, sx + step * 0.7 + U * 0.015, yy - U * 0.027); }
    }
    /* commit nodes with glow */
    for (const [x, y, lane, merge] of nodes) {
      if (x < -nr * 3 || x > W + nr * 3) continue; const col = cols[lane % cols.length], rad = merge ? nr * 1.35 : nr;
      ctx.save(); add(ctx, T); glow(ctx, T, x, y, rad * 3.2, col, 0.32); ctx.restore();
      ctx.fillStyle = T.dark ? T.bg0 : T.bg0; dot(ctx, x, y, rad); ctx.strokeStyle = col; ctx.lineWidth = Math.max(2, U * 0.005); ctx.stroke();
      if (merge) { ctx.fillStyle = col; dot(ctx, x, y, rad * 0.5); }
    }
    /* HEAD marker riding the trunk */
    { const hx = W * 0.72, pulse = frac(t * 0.7); ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(T.hi, 0.6 * (1 - pulse)); ctx.lineWidth = Math.max(2, U * 0.004); ctx.beginPath(); ctx.arc(hx, ly(0), nr * (1.6 + pulse * 2.4), 0, TAU); ctx.stroke(); ctx.fillStyle = T.hi; dot(ctx, hx, ly(0), nr * 0.8); ctx.restore();
      ctx.font = `bold ${Math.max(10, U * 0.026)}px ${MONO}`; ctx.textAlign = 'center'; const hw = U * 0.12; ctx.fillStyle = T.hi; rr(ctx, hx - hw / 2, ly(0) - U * 0.085, hw, U * 0.04, U * 0.008); ctx.fill(); ctx.fillStyle = T.dark ? '#000000' : '#FFFFFF'; ctx.fillText('HEAD', hx, ly(0) - U * 0.055); }
    ctx.restore();
  });

  /* ================= fronts ================= */
  const drifter = (key, name, glyphs, font) => K.registerFront(key, name, (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, U = Umin(env), seed = P.seed || 1, e = fade(env, 1.2), T = tones2(sc);
    ctx.globalAlpha = e; ctx.save(); add(ctx, T); ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    for (let i = 0; i < 16; i++) {
      const age = frac(t * (0.018 + 0.03 * r(seed, i, 1)) + r(seed, i, 2)), x = W * r(seed, i, 3) + Math.sin(t * 0.3 + i * 2.1) * U * 0.03, y = H * 1.05 - age * H * 1.1, sz = U * (0.03 + 0.06 * r(seed, i, 4)), g = glyphs[Math.floor(r(seed, i, 5) * glyphs.length) % glyphs.length];
      ctx.font = `${font} ${sz}px ${SERIF}`; if (font === '') ctx.font = `${sz}px ${MONO}`; ctx.fillStyle = rgba(i % 3 ? T.c1 : T.c2, (T.dark ? 0.32 : 0.26) * Math.sin(age * Math.PI)); ctx.fillText(g, x, y);
    }
    ctx.restore();
  });
  drifter('echoBraceDrift', '記号', ['{ }', '</>', '=>', '[ ]', '&&', '::', '0x', '//', '( )', ';', '!=', '++'], '');
  drifter('echoFormulaDrift', '数式', ['∑', '∫', 'π', '∞', '√', '∂', 'λ', 'θ', '≈', 'Δ', 'ε', '∇', 'φ', '≠'], 'italic');

  /* ================= 黒板 ================= */
  const EQS = ['∫ e^(−x²) dx = √π', 'e^(iπ) + 1 = 0', 'Σ 1/n² = π²/6', 'lim sin x / x = 1', '∂u/∂t = α ∇²u', 'det(A − λI) = 0', 'F(ξ) = ∫ f(x) e^(−2πixξ) dx', 'dy/dx = f(x, y)', 'P(A|B) = P(B|A) P(A) / P(B)', '∮ F·dr = ∬ (∇×F)·dS'];
  const chalk = (x, pts, w, col, al, sd) => { x.lineCap = 'round'; x.lineJoin = 'round'; for (const [ww, aa, o] of [[w, al, 0], [w * 0.4, al * 0.55, w * 0.35]]) { x.strokeStyle = rgba(col, aa); x.lineWidth = ww; x.beginPath(); pts.forEach((p, i) => { const jx = (r(sd, i, 1) - 0.5) * w * 0.7 + o, jy = (r(sd, i, 2) - 0.5) * w * 0.7; i ? x.lineTo(p[0] + jx, p[1] + jy) : x.moveTo(p[0] + jx, p[1] + jy); }); x.stroke(); } };
  K.registerBg('echoChalkboard', '黒板', ['calm', 'editorial', 'graphic'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `cb|${seed}|${ck(sc)}`;
    const ink = T.dark ? mix(sc.fg, sc.bg, 0.06) : mix(sc.fg, '#000000', 0.1), ledge = H * 0.065;
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.75, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.dark ? mix(sc.bg, '#000000', 0.25) : mix(sc.bg, '#FFFFFF', 0.4)], [1, T.dark ? mix(sc.bg, '#000000', 0.5) : sc.bg]]); x.fillRect(0, 0, W, H);
      /* eraser smudges */
      x.lineCap = 'round'; for (let i = 0; i < 16; i++) { x.strokeStyle = rgba(T.dark ? '#FFFFFF' : '#000000', 0.018 + 0.03 * r(seed, i, 6)); x.lineWidth = U * (0.05 + 0.1 * r(seed, i, 7)); const ax = W * r(seed, i, 1), ay = H * r(seed, i, 2), ang = -0.35 + r(seed, i, 3) * 0.7, l = U * (0.3 + 0.6 * r(seed, i, 4)); x.beginPath(); x.moveTo(ax, ay); x.quadraticCurveTo(ax + Math.cos(ang) * l * 0.5, ay + Math.sin(ang) * l * 0.5 + U * 0.02, ax + Math.cos(ang) * l, ay + Math.sin(ang) * l); x.stroke(); }
      x.fillStyle = rgba(T.dark ? '#FFFFFF' : '#000000', 0.05); for (let i = 0; i < 260; i++) x.fillRect(W * r(seed, i, 8), H * r(seed, i, 9), 1.4, 1.4);
      /* diagrams, chalk-drawn */
      const lw = Math.max(2, U * 0.0048), dx = portrait ? W * 0.08 : W * 0.6, dy = portrait ? H * 0.66 : H * 0.14, dw = portrait ? W * 0.84 : W * 0.34, dh = portrait ? H * 0.24 : H * 0.42;
      chalk(x, [[dx, dy + dh * 0.55], [dx + dw, dy + dh * 0.55]], lw, ink, 0.75, 11); chalk(x, [[dx + dw * 0.08, dy], [dx + dw * 0.08, dy + dh]], lw, ink, 0.75, 12);
      const cur = []; for (let i = 0; i <= 60; i++) { const u = i / 60; cur.push([dx + dw * (0.08 + 0.88 * u), dy + dh * (0.55 - 0.36 * Math.sin(u * TAU * 1.15 + 0.4) * Math.exp(-u * 0.9))]); }
      chalk(x, cur, lw * 1.2, T.c1, 0.95, 13);
      x.strokeStyle = rgba(T.c2, 0.35); x.lineWidth = Math.max(1.5, lw * 0.7); x.beginPath(); for (let i = 4; i <= 26; i += 2) { const p = cur[i]; x.moveTo(p[0], dy + dh * 0.55); x.lineTo(p[0], p[1]); } x.stroke();
      const tr = portrait ? [W * 0.12, H * 0.62] : [dx + dw * 0.05, dy + dh * 1.32]; const ts = U * (portrait ? 0.14 : 0.2);
      chalk(x, [[tr[0], tr[1]], [tr[0] + ts * 1.5, tr[1]], [tr[0], tr[1] - ts], [tr[0], tr[1]]], lw, ink, 0.75, 14); chalk(x, [[tr[0] + ts * 0.14, tr[1]], [tr[0] + ts * 0.14, tr[1] - ts * 0.14], [tr[0], tr[1] - ts * 0.14]], lw * 0.8, T.c2, 0.9, 15);
      const cc = [dx + dw * 0.78, portrait ? H * 0.6 : dy + dh * 1.5], cr = U * (portrait ? 0.08 : 0.11), ring = []; for (let i = 0; i <= 40; i++) ring.push([cc[0] + Math.cos(i / 40 * TAU) * cr, cc[1] + Math.sin(i / 40 * TAU) * cr]);
      chalk(x, ring, lw, ink, 0.75, 16); chalk(x, [[cc[0] - cr * 0.3, cc[1] - cr * 1.5], [cc[0] - cr * 0.3, cc[1] + cr * 1.5]], lw * 0.8, T.c1, 0.9, 17); chalk(x, [[cc[0], cc[1]], [cc[0] + cr * 0.95, cc[1] - cr * 0.3]], lw * 0.8, ink, 0.6, 18);
      /* ledge, chalk sticks and an eraser */
      const lg = x.createLinearGradient(0, H - ledge, 0, H); lg.addColorStop(0, T.dark ? mix(T.steel, '#000000', 0.2) : mix(T.steelHi, '#8C6A3F', 0.4)); lg.addColorStop(1, T.dark ? mix(T.steel, '#000000', 0.7) : mix(sc.fg, '#8C6A3F', 0.6)); x.fillStyle = lg; x.fillRect(0, H - ledge, W, ledge);
      x.fillStyle = rgba('#FFFFFF', 0.25); x.fillRect(0, H - ledge, W, Math.max(1.5, U * 0.003));
      x.fillStyle = mix(T.dark ? '#EEEEEE' : '#FFFFFF', sc.bg, 0.1); rr(x, W * 0.08, H - ledge * 0.72, U * 0.09, ledge * 0.32, ledge * 0.14); x.fill();
      x.fillStyle = T.c1; rr(x, W * 0.08 + U * 0.11, H - ledge * 0.7, U * 0.07, ledge * 0.3, ledge * 0.14); x.fill();
      x.fillStyle = mix(T.steel, '#3A2F28', 0.5); rr(x, W * 0.86, H - ledge * 0.82, U * 0.2, ledge * 0.5, ledge * 0.1); x.fill(); x.fillStyle = rgba('#FFFFFF', 0.8); x.fillRect(W * 0.86, H - ledge * 0.82, U * 0.2, ledge * 0.14);
    }), 0, 0, W, H);
    /* equations written on, then wiped */
    const nEq = portrait ? 4 : 5, gap = 1.9, period = nEq * gap + 5.5, cyc = Math.floor(t / period), tl = t - cyc * period, fs = U * (portrait ? 0.062 : 0.074), x0 = W * (portrait ? 0.07 : 0.06);
    const wipe = 1 - ease(clamp((tl - (nEq * gap + 3.6)) / 1.4));
    const order = EQS.map((q, i) => [q, r(seed + cyc * 13, i, 7)]).sort((a, b) => a[1] - b[1]).map(q => q[0]);
    ctx.save(); ctx.textBaseline = 'alphabetic'; ctx.textAlign = 'left';
    for (let i = 0; i < nEq; i++) {
      const s = order[i], y = H * (portrait ? 0.15 + i * 0.115 : 0.2 + i * 0.13), st = i * gap, p = clamp((tl - st) / 1.5);
      if (p <= 0) continue; ctx.font = `italic ${fs}px ${SERIF}`; const w = ctx.measureText(s).width, rw = w * ease(p);
      ctx.save(); ctx.globalAlpha = e * wipe; ctx.beginPath(); ctx.rect(x0 - 6, y - fs * 1.05, rw + 8, fs * 1.5); ctx.clip();
      ctx.fillStyle = rgba(ink, 0.92); ctx.fillText(s, x0, y); ctx.fillStyle = rgba(ink, 0.3); ctx.fillText(s, x0 + 1.2, y + 0.8); ctx.restore();
      if (p < 1) { ctx.save(); add(ctx, T); ctx.globalAlpha = e * wipe; for (let d = 0; d < 9; d++) { const a2 = frac(t * 1.7 + d * 0.11); ctx.fillStyle = rgba(ink, 0.5 * (1 - a2)); dot(ctx, x0 + rw + (r(seed, d, i) - 0.5) * fs * 0.4, y - fs * 0.2 + a2 * fs * 1.1, U * 0.0022); } ctx.restore(); }
      if (i === nEq - 1 && tl - st > 2.0) { ctx.save(); ctx.globalAlpha = e * wipe * ease(clamp((tl - st - 2) / 0.6)); const pts = []; const bx = x0 - fs * 0.3, by = y - fs * 0.95, bw = w + fs * 0.6, bh = fs * 1.4; [[bx, by], [bx + bw, by + 2], [bx + bw + 3, by + bh], [bx + 2, by + bh + 3], [bx - 2, by + 2]].forEach(q => pts.push(q)); ctx.beginPath(); chalk(ctx, pts, Math.max(2, U * 0.0042), T.c1, 0.95, 31 + cyc); ctx.restore(); }
    }
    ctx.restore();
  });

  /* ================= フーリエ円 ================= */
  K.registerBg('echoFourier', 'フーリエ円', ['calm', 'pop', 'emotional'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `fo|${seed}|${ck(sc)}`;
    const N = 8, cx = W * (portrait ? 0.5 : 0.3), cy = H * (portrait ? 0.4 : 0.5), A = U * (portrait ? 0.42 : 0.4), w0 = 0.5, FR = [1, -2, 3, -5, 4, 7, -6, 9];
    const co = []; for (let k = 0; k < N; k++) co.push({ f: FR[k], a: A * (0.62 / Math.pow(k + 1, 0.85)) * (0.85 + 0.3 * r(seed, k, 1)) * (k ? 1 : 1.2), p: r(seed, k, 2) * TAU });
    const at = (tt, n) => { let x = 0, y = 0; for (let k = 0; k < (n === undefined ? N : n); k++) { const c = co[k], ang = c.f * w0 * tt + c.p; x += Math.cos(ang) * c.a; y += Math.sin(ang) * c.a; } return [x, y]; };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      x.strokeStyle = rgba(T.line, 0.09); x.lineWidth = 1; x.beginPath(); const gs = U * 0.09; for (let gx = cx % gs; gx < W; gx += gs) { x.moveTo(gx, 0); x.lineTo(gx, H); } for (let gy = cy % gs; gy < H; gy += gs) { x.moveTo(0, gy); x.lineTo(W, gy); } x.stroke();
      x.strokeStyle = rgba(T.line, 0.25); x.beginPath(); x.moveTo(0, cy); x.lineTo(W, cy); x.moveTo(cx, 0); x.lineTo(cx, H); x.stroke();
    }), 0, 0, W, H);
    /* the whole closed curve, faint, so the shape reads; then the bright trail on top */
    { const per = TAU / (w0 * 1); ctx.save(); add(ctx, T); ctx.strokeStyle = rgba(T.c2, 0.22); ctx.lineWidth = Math.max(1.2, U * 0.003); ctx.beginPath(); for (let i = 0; i <= 260; i++) { const p = at(i / 260 * per); i ? ctx.lineTo(cx + p[0], cy + p[1]) : ctx.moveTo(cx + p[0], cy + p[1]); } ctx.stroke(); ctx.restore(); }
    /* trail of the tip */
    const tip = at(t), TR = 120, dt = 0.05; let prev = null;
    ctx.save(); add(ctx, T); ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    for (let g = 0; g < 6; g++) {
      ctx.beginPath(); for (let n = g * 20; n <= g * 20 + 20; n++) { const p = at(t - n * dt); n === g * 20 ? ctx.moveTo(cx + p[0], cy + p[1]) : ctx.lineTo(cx + p[0], cy + p[1]); }
      const a = 1 - g / 6; ctx.strokeStyle = rgba(T.c1, 0.3 * a); ctx.lineWidth = Math.max(6, U * 0.03 * a); ctx.stroke();
      ctx.strokeStyle = rgba(T.hi, 0.95 * a); ctx.lineWidth = Math.max(2.5, U * 0.009 * (0.5 + a * 0.7)); ctx.stroke();
    }
    /* epicycles */
    let px = cx, py = cy;
    for (let k = 0; k < N; k++) {
      const c = co[k], ang = c.f * w0 * t + c.p, nx = px + Math.cos(ang) * c.a, ny = py + Math.sin(ang) * c.a;
      ctx.strokeStyle = rgba(T.c2, 0.32); ctx.lineWidth = Math.max(1, U * 0.0022); ctx.beginPath(); ctx.arc(px, py, c.a, 0, TAU); ctx.stroke();
      ctx.strokeStyle = rgba(T.hi, 0.7); ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(nx, ny); ctx.stroke();
      ctx.fillStyle = rgba(T.hi, 0.9); dot(ctx, nx, ny, U * 0.006); px = nx; py = ny;
    }
    glow(ctx, T, px, py, U * 0.09, T.hi, 0.9); ctx.fillStyle = '#FFFFFF'; dot(ctx, px, py, U * 0.008);
    /* unrolled wave: the tip's height carried off to the right */
    if (!portrait) {
      const wx0 = W * 0.6, span = W * 0.36, n = 150;
      ctx.setLineDash([U * 0.008, U * 0.012]); ctx.strokeStyle = rgba(T.hi, 0.5); ctx.lineWidth = Math.max(1, U * 0.002); ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(wx0, py); ctx.stroke(); ctx.setLineDash([]);
      for (let g = 0; g < 5; g++) { ctx.beginPath(); for (let i = g * 30; i <= g * 30 + 30; i++) { const p = at(t - i * 0.028); const xx = wx0 + span * i / n, yy = cy + p[1]; i === g * 30 ? ctx.moveTo(xx, yy) : ctx.lineTo(xx, yy); } const a = 1 - g / 5; ctx.strokeStyle = rgba(T.c1, 0.85 * a); ctx.lineWidth = Math.max(2, U * 0.005); ctx.stroke(); }
      ctx.fillStyle = T.hi; dot(ctx, wx0, py, U * 0.007);
    }
    ctx.restore();
  });

  /* ================= 曲面 ================= */
  K.registerBg('echoSurface', '曲面', ['calm', 'graphic', 'pop'], (env, P, ctx) => {
    const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), T = tones2(sc), portrait = H > W, key = `su|${seed}|${ck(sc)}`;
    const G = portrait ? 16 : 20, S = U * (portrait ? 0.33 : 0.3), cx = W * 0.5, cy = H * (portrait ? 0.55 : 0.52), th = 0.92, a = 0.55 + t * 0.15, sa = Math.sin(a), ca = Math.cos(a), st = Math.sin(th), ct = Math.cos(th);
    const k1 = 2.8 + r(seed, 1, 1) * 1.4, amp = 0.55;
    const z = (x, y) => { const rr2 = Math.hypot(x, y); return amp * Math.sin(k1 * rr2 * 2.2 - t * 1.4) / (1 + 3 * rr2) * 1.6 + 0.12 * Math.sin(2.2 * x + t * 0.5) * Math.cos(2.0 * y - t * 0.4); };
    ctx.globalAlpha = e;
    ctx.drawImage(layer(env, `${key}|bg`, 0.5, (x) => {
      x.fillStyle = vertical(x, 0, H, [[0, T.bg0], [1, T.bg1]]); x.fillRect(0, 0, W, H);
      glowDisc(x, cx, cy, U * 0.9, T.c2, T.dark ? 0.16 : 0.2);
    }), 0, 0, W, H);
    const pts = []; let zmin = 9, zmax = -9;
    for (let i = 0; i <= G; i++) for (let j = 0; j <= G; j++) {
      const x = -1 + 2 * i / G, y = -1 + 2 * j / G, zz = z(x, y), X = x * ca - y * sa, Y = x * sa + y * ca, k = 1 / (1 + Y * 0.18);
      pts[i * (G + 1) + j] = [cx + X * S * k, cy - (Y * st + zz * ct * 1.7) * S * k + S * 0.1, zz]; if (zz < zmin) zmin = zz; if (zz > zmax) zmax = zz;
    }
    const sx = sa > 0 ? -1 : 1, sy = ca > 0 ? -1 : 1, outerX = Math.abs(sa) >= Math.abs(ca);
    const cell = (i, j) => {
      const p0 = pts[i * (G + 1) + j], p1 = pts[(i + 1) * (G + 1) + j], p2 = pts[(i + 1) * (G + 1) + j + 1], p3 = pts[i * (G + 1) + j + 1], zm = (p0[2] + p1[2] + p2[2] + p3[2]) / 4, u = clamp((zm - zmin) / (zmax - zmin + 1e-6));
      ctx.beginPath(); ctx.moveTo(p0[0], p0[1]); ctx.lineTo(p1[0], p1[1]); ctx.lineTo(p2[0], p2[1]); ctx.lineTo(p3[0], p3[1]); ctx.closePath();
      ctx.fillStyle = rgba(mix(mix(T.bg1, T.c2, 0.35), T.c1, u), 0.62 + 0.3 * u); ctx.fill();
      ctx.strokeStyle = rgba(T.dark ? mix(T.c1, '#FFFFFF', 0.3) : T.ink, 0.28 + 0.4 * u); ctx.lineWidth = Math.max(0.8, U * 0.0016); ctx.stroke();
    };
    const ia = sx < 0 ? [G - 1, 0] : [0, G - 1], ja = sy < 0 ? [G - 1, 0] : [0, G - 1];
    const loopA = outerX ? ia : ja, loopB = outerX ? ja : ia;
    for (let a1 = loopA[0]; loopA[0] > loopA[1] ? a1 >= loopA[1] : a1 <= loopA[1]; a1 += loopA[0] > loopA[1] ? -1 : 1)
      for (let b1 = loopB[0]; loopB[0] > loopB[1] ? b1 >= loopB[1] : b1 <= loopB[1]; b1 += loopB[0] > loopB[1] ? -1 : 1) outerX ? cell(a1, b1) : cell(b1, a1);
    /* corner axes */
    const ax = W * (portrait ? 0.12 : 0.08), ay = H * (portrait ? 0.9 : 0.86), L = U * 0.09;
    ctx.save(); ctx.font = `italic ${U * 0.03}px ${SERIF}`; ctx.textAlign = 'center';
    for (const [dx2, dy2, lab, col] of [[ca, -sa * st, 'x', T.c1], [sa, ca * st, 'y', T.c2], [0, -ct * 1.4, 'z', T.hi]]) { ctx.strokeStyle = rgba(col, 0.9); ctx.lineWidth = Math.max(1.5, U * 0.003); ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(ax + dx2 * L, ay + dy2 * L); ctx.stroke(); ctx.fillStyle = col; ctx.fillText(lab, ax + dx2 * L * 1.25, ay + dy2 * L * 1.25 + U * 0.01); }
    ctx.restore();
  });

})();
