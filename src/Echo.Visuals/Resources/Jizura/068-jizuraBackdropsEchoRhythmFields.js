/* Original circle, orbit, matrix and cube fields; shared chart in jizuraBackdropsEchoRhythm.js. */
(() => {
    'use strict';
    const K = J.echo, TAU = K.TAU, clamp = K.clamp, mix = K.mix, rgba = K.rgba, r = J.r;
    const { U: Umin, isDark, fade, vertical, glowDisc, layer } = K;
    const { frac, ease, easeOut, MONO, ROUND, rr, BPM, SD, tx, add, glow, star4, stepNotes, cumGen, cumAt, judgeWord, hitState, stage, hud, arena, registerBg } = K.rhythm;
    /* Circle targets with approach rings and a compact slider trace. */
    const targetDisc = (ctx, x, y, R, col, ringCol, ringW) => {
        ctx.fillStyle = rgba(col, 0.16);
        ctx.beginPath();
        ctx.arc(x, y, R, 0, TAU);
        ctx.fill();
        ctx.strokeStyle = ringCol || col;
        ctx.lineWidth = Math.max(1.5, ringW || 2);
        ctx.stroke();
        ctx.strokeStyle = rgba(col, 0.5);
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(x, y, R * 0.82, 0, TAU);
        ctx.stroke();
    };
    const OP = 60 / BPM; /* one object per beat */
    const opos = (seed, i) => { const a = seed * 1.7; return [clamp(0.5 + 0.38 * Math.sin(i * 0.79 + a) * Math.cos(i * 0.23 + a * 0.5) + (r(seed, i, 1) - 0.5) * 0.12, 0.07, 0.93), clamp(0.5 + 0.36 * Math.sin(i * 1.13 + a * 1.3 + 1) + (r(seed, i, 2) - 0.5) * 0.12, 0.09, 0.91)]; };
    const isSl = i => i % 4 === 3;
    const SLD = OP * 0.66;
    const sctrl = (seed, i, ar) => { const [x, y] = opos(seed, i), ang = r(seed, i, 3) * TAU, L = 0.2, ex = clamp(x + Math.cos(ang) * L, 0.07, 0.93), ey = clamp(y + Math.sin(ang) * L * ar, 0.09, 0.91), mx = (x + ex) / 2 + Math.cos(ang + 1.57) * 0.07, my = (y + ey) / 2 + Math.sin(ang + 1.57) * 0.07 * ar; return { x, y, ex, ey, mx, my }; };
    const sPt = (c, u) => { const a = (1 - u) * (1 - u), b = 2 * u * (1 - u), d = u * u; return [a * c.x + b * c.mx + d * c.ex, a * c.y + b * c.my + d * c.ey]; };
    registerBg('echoOsuField', 'サークル', ['pop', 'graphic', 'emotional'], (env, P, ctx) => {
        const { W, H, sc } = env, t = env.t, seed = P.seed || 1, U = Umin(env), e = fade(env, 1), X = tx(sc), dk = X.dk, portrait = H > W;
        const cand = [sc.accent, sc.accent2, '#EDC882', sc.accent];
        const pw = portrait ? W * 0.84 : W * 0.37, ph = portrait ? Math.min(H * 0.48, pw * 1.2) : Math.min(H * 0.65, pw), fx = portrait ? (W - pw) / 2 : W * 0.58, fy = portrait ? H * 0.40 : (H - ph) / 2 + H * 0.04, ar = pw / ph, R = pw * 0.065;
        const P2 = (nx, ny) => [fx + nx * pw, fy + ny * ph], colOf = i => cand[Math.floor(i / 6) % 4];
        ctx.globalAlpha = e;
        stage(env, ctx);
        ctx.strokeStyle = rgba(sc.fg, 0.12);
        ctx.lineWidth = 1;
        rr(ctx, fx, fy, pw, ph, 3);
        ctx.stroke();
        hud(env, ctx, 'CIRCLE / TRACE', Math.floor(t / OP), fx, fy - U * 0.10, pw);
        const pre = 1.25, k = Math.floor(t / OP), iLast = k - 1;
        ctx.save();
        ctx.beginPath();
        ctx.rect(fx - R, fy - R, pw + R * 2, ph + R * 2);
        ctx.clip();
        for (let i = iLast; i <= iLast + 3; i++) {
            const ti = (i + 1) * OP, tn = (i + 2) * OP;
            if (t < ti - pre * 0.6 || t > tn)
                continue;
            const a = isSl(i) ? sPt(sctrl(seed, i, ar), 1) : opos(seed, i), b = opos(seed, i + 1), A = P2(a[0], a[1]), B = P2(b[0], b[1]), n = Math.max(2, Math.floor(Math.hypot(B[0] - A[0], B[1] - A[1]) / (R * 0.55)));
            const al = clamp((t - (ti - pre * 0.6)) / 0.25) * clamp((tn - t) / 0.15 + 0.2) * 0.6;
            ctx.fillStyle = rgba(dk ? '#FFFFFF' : '#7A5CC8', al * 0.7);
            for (let j = 1; j < n; j++) {
                const u = j / n;
                ctx.beginPath();
                ctx.arc(A[0] + (B[0] - A[0]) * u, A[1] + (B[1] - A[1]) * u, R * 0.06, 0, TAU);
                ctx.fill();
            }
        }
        for (let i = iLast + Math.ceil(pre / OP) + 1; i >= iLast - 1; i--) {
            if (i < 0)
                continue;
            const ti = (i + 1) * OP, sl = isSl(i), te = ti + (sl ? SLD : 0), col = colOf(i), num = i % 6 + 1;
            if (t < ti - pre || t > te + 0.34)
                continue;
            const c = sctrl(seed, i, ar), [hx, hy] = P2(c.x, c.y), aIn = clamp((t - (ti - pre)) / 0.26);
            if (sl) {
                const A = P2(c.x, c.y), M = P2(c.mx, c.my), B = P2(c.ex, c.ey), fo = t > te ? 1 - clamp((t - te) / 0.3) : 1;
                ctx.save();
                ctx.globalAlpha = aIn * fo;
                ctx.lineCap = 'round';
                ctx.lineJoin = 'round';
                ctx.beginPath();
                ctx.moveTo(A[0], A[1]);
                ctx.quadraticCurveTo(M[0], M[1], B[0], B[1]);
                ctx.strokeStyle = 'rgba(255,255,255,0.95)';
                ctx.lineWidth = R * 1.75;
                ctx.stroke();
                ctx.strokeStyle = rgba(mix(col, '#FFFFFF', dk ? 0.15 : 0.05), 0.62);
                ctx.lineWidth = R * 1.45;
                ctx.stroke();
                ctx.strokeStyle = 'rgba(255,255,255,0.35)';
                ctx.lineWidth = R * 0.4;
                ctx.stroke();
                targetDisc(ctx, B[0], B[1], R * 0.42, '#FFFFFF', null, 0);
                ctx.restore();
            }
            if (t < ti) {
                const p = clamp((ti - t) / pre), ap = R * (1 + 2.6 * p);
                ctx.save();
                ctx.globalAlpha = aIn;
                ctx.strokeStyle = dk ? 'rgba(255,255,255,0.95)' : rgba(mix(col, '#000000', 0.15), 0.95);
                ctx.lineWidth = R * 0.09;
                ctx.beginPath();
                ctx.arc(hx, hy, ap, 0, TAU);
                ctx.stroke();
                ctx.fillStyle = 'rgba(0,0,0,0.18)';
                ctx.beginPath();
                ctx.arc(hx + R * 0.05, hy + R * 0.1, R, 0, TAU);
                ctx.fill();
                targetDisc(ctx, hx, hy, R, col, '#FFFFFF', R * 0.16);
                ctx.fillStyle = '#FFFFFF';
                ctx.font = `900 ${R * 0.95}px ${ROUND}`;
                ctx.textAlign = 'center';
                ctx.textBaseline = 'middle';
                ctx.strokeStyle = rgba(mix(col, '#000000', 0.5), 0.6);
                ctx.lineWidth = R * 0.11;
                ctx.lineJoin = 'round';
                ctx.strokeText(String(num), hx, hy + R * 0.04);
                ctx.fillText(String(num), hx, hy + R * 0.04);
                ctx.textBaseline = 'alphabetic';
                ctx.restore();
            }
            else if (t - ti < 0.34) {
                const p = clamp((t - ti) / 0.34);
                ctx.save();
                add(ctx, dk);
                ctx.globalAlpha = (1 - p) * 0.95;
                ctx.strokeStyle = col;
                ctx.lineWidth = R * 0.14 * (1 - p);
                ctx.beginPath();
                ctx.arc(hx, hy, R * (1 + 1.1 * easeOut(p)), 0, TAU);
                ctx.stroke();
                glowDisc(ctx, hx, hy, R * (1.5 + p), col, (1 - p) * 0.8);
                ctx.fillStyle = dk ? '#FFFFFF' : col;
                for (let q = 0; q < 6; q++) {
                    const a = q * TAU / 6 + r(seed, i, q), d = R * (1.1 + 1.4 * easeOut(p));
                    star4(ctx, hx + Math.cos(a) * d, hy + Math.sin(a) * d, R * 0.18 * (1 - p), a);
                    ctx.fill();
                }
                ctx.restore();
            }
            if (sl && t >= ti && t <= te + 0.12) {
                const u = clamp((t - ti) / SLD), [bx, by] = P2(...sPt(c, u));
                ctx.save();
                ctx.strokeStyle = 'rgba(255,255,255,0.95)';
                ctx.lineWidth = R * 0.1;
                ctx.beginPath();
                ctx.arc(bx, by, R * (1.55 + 0.08 * Math.sin(t * 30)), 0, TAU);
                ctx.stroke();
                glow(ctx, dk, bx, by, R * 1.6, col, 0.6);
                ctx.restore();
                targetDisc(ctx, bx, by, R * 0.8, col, '#FFFFFF', R * 0.12);
            }
        }
        const cur = tt => { const kk = Math.floor(tt / OP), i = kk - 1; if (i < 0)
            return opos(seed, 0); const ti = (i + 1) * OP, sl = isSl(i); if (sl && tt < ti + SLD)
            return sPt(sctrl(seed, i, ar), clamp((tt - ti) / SLD)); const from = sl ? sPt(sctrl(seed, i, ar), 1) : opos(seed, i), te = ti + (sl ? SLD : 0), to = opos(seed, i + 1), u = ease((tt - te) / ((i + 2) * OP - te)); return [from[0] + (to[0] - from[0]) * u, from[1] + (to[1] - from[1]) * u]; };
        ctx.save();
        add(ctx, dk);
        for (let j = 10; j >= 1; j--) {
            const q = cur(t - j * 0.032), pt = P2(q[0], q[1]);
            ctx.fillStyle = rgba(cand[j % 4], (1 - j / 11) * 0.8);
            star4(ctx, pt[0], pt[1], R * 0.24 * (1 - j / 12), j);
            ctx.fill();
        }
        ctx.restore();
        const cp = P2(...cur(t));
        glow(ctx, dk, cp[0], cp[1], R * 1.3, '#FFFFFF', 0.5);
        ctx.fillStyle = '#FFFFFF';
        star4(ctx, cp[0], cp[1], R * 0.42, t * 2);
        ctx.fill();
        ctx.strokeStyle = rgba(cand[0], 0.9);
        ctx.lineWidth = 1.5;
        ctx.stroke();
        ctx.restore();
        ctx.globalAlpha = 1;
    });
    /* Eight radial targets: the approach ring and the hit rim share one clear geometry. */
    registerBg('echoRingChart', 'リング', ['calm', 'graphic'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, step = Math.floor(t / SD), cx = box.x + box.w / 2, cy = box.y + box.h / 2;
        const R = Math.min(box.w, box.h) * 0.41, hs = hitState(seed, 8, t), point = (l, r) => [cx + Math.cos(l * TAU / 8 - Math.PI / 2) * r, cy + Math.sin(l * TAU / 8 - Math.PI / 2) * r];
        stage(env, ctx);
        hud(env, ctx, '8R / ORBIT CHART', cumAt(seed, 8, step), box.x, box.y - H * 0.09, box.w);
        ctx.strokeStyle = rgba(sc.fg, 0.13);
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(cx, cy, R, 0, TAU);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(cx, cy, R * 0.35, 0, TAU);
        ctx.stroke();
        for (let l = 0; l < 8; l++) {
            const [x, y] = point(l, R), col = l % 2 ? sc.accent2 : sc.accent;
            ctx.strokeStyle = rgba(col, 0.3 + hs.beam[l] * 0.7);
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(x, y, R * 0.067, 0, TAU);
            ctx.stroke();
            ctx.fillStyle = rgba(col, hs.beam[l] * 0.45);
            ctx.fill();
        }
        for (let s = step + 8; s > step; s--)
            for (const n of stepNotes(seed, 8, s)) {
                const p = 1 - (s * SD - t) / 1.5;
                if (p < 0)
                    continue;
                const [x, y] = point(n.l, R * (0.35 + 0.65 * p));
                ctx.fillStyle = n.l % 2 ? sc.accent2 : sc.accent;
                ctx.beginPath();
                ctx.arc(x, y, R * 0.045, 0, TAU);
                ctx.fill();
                if (n.len) {
                    ctx.strokeStyle = rgba(ctx.fillStyle, 0.3);
                    ctx.lineWidth = R * 0.03;
                    ctx.beginPath();
                    ctx.moveTo(...point(n.l, R * (0.35 + 0.65 * Math.max(0, p - 0.2))));
                    ctx.lineTo(x, y);
                    ctx.stroke();
                }
            }
        ctx.textAlign = 'center';
        ctx.fillStyle = rgba(sc.fg, 0.6);
        ctx.font = `600 ${R * 0.13}px ${MONO}`;
        ctx.fillText(String(cumAt(seed, 8, step)), cx, cy + R * 0.04);
    });
    /* Sixteen neutral pads; each preview contracts inside its own cell before the hit. */
    const padAt = (seed, q) => {
        if (q < 0 || (q % 4 === 3 && r(seed, q, 61) < 0.45))
            return [];
        const first = Math.floor(r(seed, q, 62) * 16), out = [first];
        if (r(seed, q, 63) < 0.16)
            out.push((first + 1 + Math.floor(r(seed, q, 64) * 15)) % 16);
        return out;
    };
    registerBg('echoPanelGrid', 'パッド', ['pop', 'graphic', 'emotional'], (env, P, ctx) => {
        const { W, H, sc } = env, U = Umin(env), portrait = H > W, seed = P.seed || 1;
        const duration = 60 / BPM, pre = 0.9, q0 = Math.floor((env.t - 0.4) / duration);
        const gs = portrait ? Math.min(W * 0.84, H * 0.46) : Math.min(W * 0.36, H * 0.65);
        const gx = portrait ? (W - gs) / 2 : W * 0.59, gy = portrait ? H * 0.41 : (H - gs) / 2 + H * 0.05;
        const cell = gs / 4, gap = Math.max(3, cell * 0.085), w = cell - gap * 2;
        stage(env, ctx);
        for (let i = 0; i < 16; i++) {
            const x = gx + i % 4 * cell + gap, y = gy + Math.floor(i / 4) * cell + gap;
            ctx.fillStyle = rgba(sc.fg, 0.035);
            rr(ctx, x, y, w, w, 3);
            ctx.fill();
            ctx.strokeStyle = rgba(sc.fg, 0.13);
            ctx.lineWidth = 1;
            ctx.stroke();
            ctx.fillStyle = rgba(sc.fg, 0.24);
            ctx.font = `500 ${Math.max(8, cell * 0.12)}px ${MONO}`;
            ctx.textAlign = 'left';
            ctx.fillText(String(i + 1).padStart(2, '0'), x + w * 0.1, y + w * 0.2);
        }
        let latest = null;
        for (let q = Math.max(0, q0 - 1); q <= q0 + Math.ceil(pre / duration) + 1; q++) {
            const dt = q * duration + 0.4 - env.t;
            if (dt > pre || dt < -0.28)
                continue;
            for (const i of padAt(seed, q)) {
                const x = gx + (i % 4 + 0.5) * cell, y = gy + (Math.floor(i / 4) + 0.5) * cell;
                const col = i % 2 ? sc.accent2 : sc.accent;
                if (dt > 0) {
                    const progress = 1 - dt / pre, size = w * (0.28 + 0.68 * (1 - progress));
                    ctx.strokeStyle = rgba(col, 0.3 + progress * 0.7);
                    ctx.lineWidth = Math.max(1.5, cell * 0.025);
                    rr(ctx, x - size / 2, y - size / 2, size, size, 2);
                    ctx.stroke();
                    ctx.fillStyle = rgba(col, 0.3 + progress * 0.6);
                    rr(ctx, x - w * 0.08, y - w * 0.08, w * 0.16, w * 0.16, 1);
                    ctx.fill();
                }
                else {
                    const alpha = 1 + dt / 0.28;
                    ctx.fillStyle = rgba(col, alpha * 0.28);
                    rr(ctx, x - w / 2, y - w / 2, w, w, 3);
                    ctx.fill();
                    ctx.strokeStyle = rgba(col, alpha);
                    ctx.lineWidth = 2;
                    ctx.stroke();
                    ctx.fillStyle = rgba(sc.fg, alpha);
                    ctx.fillRect(x - w * 0.12, y - 1, w * 0.24, 2);
                    ctx.fillRect(x - 1, y - w * 0.12, 2, w * 0.24);
                    latest = { q, age: -dt };
                }
            }
        }
        const cum = cumGen('pd', seed, q => padAt(seed, q).length, Math.max(0, q0));
        hud(env, ctx, '16P / TOUCH MATRIX', cum, gx, gy - U * 0.10, gs);
        if (latest) {
            const [word, k] = judgeWord(seed, latest.q);
            ctx.globalAlpha = 1 - latest.age / 0.28;
            ctx.textAlign = 'center';
            ctx.font = `700 ${U * 0.024}px ${MONO}`;
            ctx.fillStyle = [sc.accent, '#EDC882', sc.accent2][k];
            ctx.fillText(word, gx + gs / 2, gy + gs + U * 0.045);
            ctx.globalAlpha = 1;
        }
    });
    /* Cubes approach a fixed gate; the slash stays inside the play field. */
    registerBg('echoSlashCubes', 'キューブ', ['graphic', 'glitch'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, step = Math.floor(t / SD), cx = box.x + box.w / 2, cy = box.y + box.h * 0.28;
        stage(env, ctx);
        hud(env, ctx, 'CUBE / SLASH CHART', cumAt(seed, 2, step), box.x, box.y - H * 0.09, box.w);
        const corners = [[box.x, box.y + box.h], [box.x + box.w, box.y + box.h], [box.x, box.y], [box.x + box.w, box.y]];
        ctx.strokeStyle = rgba(sc.fg, 0.11);
        ctx.lineWidth = 1;
        for (const [x, y] of corners) {
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.lineTo(x, y);
            ctx.stroke();
        }
        for (let i = 0; i < 6; i++) {
            const p = frac(i / 6 + t * 0.24), w = box.w * (0.1 + 0.85 * p * p), h = box.h * (0.1 + 0.85 * p * p);
            ctx.strokeStyle = rgba(sc.fg, 0.05 + p * 0.1);
            rr(ctx, cx - w / 2, cy - h * 0.3, w, h, 1);
            ctx.stroke();
        }
        ctx.save();
        ctx.beginPath();
        ctx.rect(box.x, box.y, box.w, box.h);
        ctx.clip();
        for (let s = step + 8; s >= step; s--)
            for (const n of stepNotes(seed, 2, s)) {
                const dt = s * SD - t, p = clamp(1 - dt / 1.4);
                if (p <= 0)
                    continue;
                const size = box.w * (0.025 + 0.18 * p * p), x = cx + (n.l ? 1 : -1) * box.w * (0.04 + 0.19 * p * p), y = cy + box.h * 0.46 * p * p;
                const col = n.l ? sc.accent2 : sc.accent;
                ctx.fillStyle = K.mix(sc.bg, col, 0.18 + 0.25 * p);
                rr(ctx, x - size / 2, y - size / 2, size, size, 3);
                ctx.fill();
                ctx.strokeStyle = col;
                ctx.lineWidth = 2;
                ctx.stroke();
                ctx.strokeStyle = sc.fg;
                ctx.lineWidth = Math.max(1.5, size * 0.045);
                ctx.beginPath();
                ctx.moveTo(x - size * 0.18, y - size * 0.14);
                ctx.lineTo(x, y + size * 0.13);
                ctx.lineTo(x + size * 0.18, y - size * 0.14);
                ctx.stroke();
            }
        const age = frac(t / SD) * SD;
        if (age < 0.13 && stepNotes(seed, 2, step).length) {
            const alpha = 1 - age / 0.13;
            ctx.strokeStyle = rgba(sc.fg, alpha * 0.9);
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.moveTo(cx - box.w * 0.22, cy + box.h * 0.68);
            ctx.lineTo(cx + box.w * 0.22, cy + box.h * 0.22);
            ctx.stroke();
        }
        ctx.restore();
    });
})();
