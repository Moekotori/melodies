/* Shared rhythm chart, drawing primitives and bounded caches. Original first-party artwork. */
(() => {
    'use strict';
    const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
    const { U: Umin, isDark, fade, vertical, glowDisc, layer } = K;
    const frac = v => v - Math.floor(v);
    const ease = v => { v = clamp(v); return v * v * (3 - 2 * v); };
    const easeOut = v => { v = clamp(v); return 1 - Math.pow(1 - v, 3); };
    const MONO = 'ui-monospace,"SF Mono",Menlo,Consolas,"Courier New",monospace';
    const SANS = 'system-ui,-apple-system,"Segoe UI",Roboto,"Helvetica Neue",Arial,sans-serif';
    const ROUND = '"Hiragino Maru Gothic ProN","Arial Rounded MT Bold","Yu Gothic UI",system-ui,sans-serif';
    const rr = (ctx, x, y, w, h, rad) => { ctx.beginPath(); ctx.roundRect(x, y, w, h, Math.max(0, Math.min(rad, Math.abs(w) / 2, Math.abs(h) / 2))); };
    const BPM = 148, SD = 60 / BPM / 2; /* one step = an eighth note */
    /* every scene brings its own palette, only leaning a little on the scheme's accent */
    const lean = (sc, cols, k = 0.1) => cols.map(c => mix(c, sc.accent, k));
    const tx = sc => { const dk = isDark(sc.bg); return { dk, fg: dk ? '#F4F6FF' : '#1B1F33', dim: dk ? 'rgba(244,246,255,0.55)' : 'rgba(27,31,51,0.55)', ink: dk ? '#000000' : '#FFFFFF' }; };
    const add = (ctx, dk) => { ctx.globalCompositeOperation = dk ? 'screen' : 'source-over'; };
    const glow = (ctx, dk, x, y, rad, c, a) => { ctx.save(); ctx.globalCompositeOperation = dk ? 'screen' : 'source-over'; glowDisc(ctx, x, y, rad, c, dk ? a : a * 0.7); ctx.restore(); };
    const gradV = (ctx, y0, y1, stops) => { const g = ctx.createLinearGradient(0, y0, 0, y1); for (const [o, c] of stops)
        g.addColorStop(o, c); return g; };
    const wash = (ctx, W, H, stops) => { ctx.fillStyle = gradV(ctx, 0, H, stops); ctx.fillRect(0, 0, W, H); };
    const star4 = (ctx, x, y, R, rot = 0, pinch = 0.22) => { ctx.beginPath(); for (let i = 0; i < 8; i++) {
        const a = rot + i * Math.PI / 4, rad = i % 2 ? R * pinch : R;
        const px = x + Math.cos(a) * rad, py = y + Math.sin(a) * rad;
        if (i)
            ctx.lineTo(px, py);
        else
            ctx.moveTo(px, py);
    } ctx.closePath(); };
    /* ---------- a stateless chart: the same seed always gives the same notes, so a scene needs no memory ---------- */
    const holdsAt = (seed, nL, b) => {
        const out = [], bi = b / 8;
        if (r(seed, bi, 91) < 0.42) {
            const l = Math.floor(r(seed, bi, 92) * nL);
            out.push({ l, len: 3 + Math.floor(r(seed, bi, 93) * 4) });
            if (r(seed, bi, 94) < 0.16)
                out.push({ l: (l + 1 + Math.floor(r(seed, bi, 95) * (nL - 1))) % nL, len: 2 + Math.floor(r(seed, bi, 96) * 3) });
        }
        return out;
    };
    const stepNotes = (seed, nL, s) => {
        const out = [];
        if (s < 0)
            return out;
        const d = s % 8, b = s - d, hs = holdsAt(seed, nL, b);
        const busy = l => { for (const h of hs)
            if (h.l === l && d <= h.len)
                return true; return false; };
        if (d === 0)
            for (const h of hs)
                out.push({ l: h.l, len: h.len });
        const f = nL / 4, p = Math.min(1, (d === 0 ? 0.97 : d === 4 ? 0.92 : d % 2 === 0 ? 0.8 : 0.5) * (0.85 + nL * 0.04)), pe = Math.min(0.8, (d === 0 ? 0.55 : d === 4 ? 0.35 : d % 2 === 0 ? 0.16 : 0.07) * f);
        const l1 = Math.floor(r(seed, s, 81) * nL);
        if (r(seed, s, 80) < p && !busy(l1))
            out.push({ l: l1, len: 0 });
        if (r(seed, s, 83) < pe) {
            const l2 = (l1 + 1 + Math.floor(r(seed, s, 84) * (nL - 1))) % nL;
            if (!busy(l2))
                out.push({ l: l2, len: 0 });
        }
        return out;
    };
    const CUMG = new Map();
    const cumGen = (tag, seed, fn, s) => {
        const seg = Math.floor(s / 192), key = `${tag}|${seed}|${seg}`;
        let a = CUMG.get(key);
        if (!a) {
            a = new Uint16Array(193);
            let c = 0;
            for (let i = 0; i < 192; i++) {
                c += fn(seg * 192 + i);
                a[i + 1] = c;
            }
            CUMG.set(key, a);
            if (CUMG.size > 32)
                CUMG.delete(CUMG.keys().next().value);
        }
        return a[Math.min(192, s - seg * 192 + 1)];
    };
    const cumAt = (seed, nL, s) => cumGen(`n${nL}`, seed, i => stepNotes(seed, nL, i).length, s);
    const judgeWord = (seed, s) => { const q = r(seed, s, 97); return q < 0.72 ? ['PERFECT', 0] : q < 0.95 ? ['GREAT', 1] : ['GOOD', 2]; };
    const lastStep = (seed, nL, s0) => { for (let s = s0; s >= s0 - 3; s--)
        if (stepNotes(seed, nL, s).length)
            return s; return -1; };
    /* what is happening at the hit line this frame: per-lane glow, and the burst list */
    const hitState = (seed, nL, t) => {
        const s0 = Math.floor(t / SD), beam = new Array(nL).fill(0), bursts = [];
        for (let s = s0; s >= s0 - 2; s--)
            for (const n of stepNotes(seed, nL, s)) {
                const age = t - s * SD;
                if (age < 0.32) {
                    beam[n.l] = Math.max(beam[n.l], 1 - age / 0.26);
                    bursts.push({ l: n.l, age, s });
                }
            }
        for (let bb = s0 - (s0 % 8); bb >= s0 - (s0 % 8) - 8; bb -= 8)
            for (const h of holdsAt(seed, nL, bb))
                if (bb * SD <= t && t <= (bb + h.len) * SD)
                    beam[h.l] = 1;
        return { s0, beam, bursts };
    };
    /* Crisp cabinet surfaces: no photographic grain, floating chips or full-screen bloom. */
    const stage = (env, ctx, accent = env.sc.accent) => {
        const { W, H, sc } = env, dk = isDark(sc.bg);
        wash(ctx, W, H, [[0, mix(sc.bg, dk ? '#0A1020' : '#F0F5FA', 0.8)], [1, mix(sc.bg, dk ? '#15243C' : '#DFEAF3', 0.8)]]);
        const g = ctx.createRadialGradient(W * 0.78, H * 0.42, 0, W * 0.78, H * 0.42, Math.min(W, H) * 0.8);
        g.addColorStop(0, rgba(accent, dk ? 0.08 : 0.06));
        g.addColorStop(1, rgba(accent, 0));
        ctx.fillStyle = g;
        ctx.fillRect(0, 0, W, H);
        ctx.strokeStyle = rgba(sc.fg, 0.055);
        ctx.lineWidth = 1;
        for (let y = H * 0.12; y < H * 0.94; y += Math.max(32, H * 0.07)) {
            ctx.beginPath();
            ctx.moveTo(W * 0.56, y);
            ctx.lineTo(W * 0.95, y);
            ctx.stroke();
        }
    };
    const hud = (env, ctx, name, count, x, y, width) => {
        const { sc } = env, U = Umin(env), ink = tx(sc), size = Math.max(10, U * 0.018);
        ctx.textAlign = 'left';
        ctx.fillStyle = ink.dim;
        ctx.font = `600 ${size}px ${MONO}`;
        ctx.fillText(name, x, y);
        ctx.textAlign = 'right';
        ctx.fillText('AUTO', x + width, y);
        ctx.fillStyle = ink.fg;
        ctx.font = `600 ${size * 1.5}px ${MONO}`;
        ctx.fillText(String(count).padStart(4, '0'), x + width, y + size * 2.1);
        ctx.textAlign = 'left';
        ctx.font = `500 ${size * 0.85}px ${MONO}`;
        ctx.fillStyle = ink.dim;
        ctx.fillText('COMBO', x, y + size * 2.1);
        ctx.strokeStyle = rgba(sc.fg, 0.18);
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, y + size * 3);
        ctx.lineTo(x + width, y + size * 3);
        ctx.stroke();
    };
    const arena = env => {
        const portrait = env.H > env.W;
        return { x: env.W * (portrait ? 0.08 : 0.58), y: env.H * (portrait ? 0.41 : 0.23),
            w: env.W * (portrait ? 0.84 : 0.37), h: env.H * (portrait ? 0.43 : 0.59) };
    };
    const registerBg = (key, name, tags, draw) => K.registerBg(key, name, tags, draw, false);
    K.rhythm = { frac, ease, easeOut, MONO, ROUND, rr, BPM, SD, tx, add, glow, star4, stepNotes, cumGen, cumAt, judgeWord, lastStep, hitState, stage, hud, arena, registerBg };
})();
