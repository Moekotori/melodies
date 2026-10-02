/* Original arrow, scratch, guitar and piano cabinets. */
(() => {
    'use strict';
    const K = J.echo, { clamp, rgba, TAU, isDark, U: Umin } = K;
    const { frac, MONO, rr, SD, stepNotes, cumAt, judgeWord, lastStep, hitState, stage, hud, arena, registerBg } = K.rhythm;
    /* A perspective highway: short trapezoid heads, continuous holds, a bright fixed hit gate. */
    registerBg('echoManiaLanes', 'レーン', ['glitch', 'graphic', 'calm'], (env, P, ctx) => {
        const { W, H, sc, t } = env, U = Umin(env), portrait = H > W, seed = P.seed || 1;
        const nL = 4, cols = [sc.accent, sc.accent2, sc.accent2, sc.accent];
        const width = portrait ? W * 0.88 : W * 0.39, cx = W * (portrait ? 0.5 : 0.765);
        const top = H * (portrait ? 0.39 : 0.16), hit = H * 0.84, bottom = H * 0.93;
        const point = (lane, p) => [cx + (lane / nL - 0.5) * width * (0.36 + 0.64 * p), top + (hit - top) * p];
        const quad = (left, right, p0, p1) => {
            ctx.beginPath();
            ctx.moveTo(...point(left, p0));
            ctx.lineTo(...point(right, p0));
            ctx.lineTo(...point(right, p1));
            ctx.lineTo(...point(left, p1));
            ctx.closePath();
        };
        const progress = dt => Math.pow(clamp(1 - dt / 1.75), 1.6);
        const hs = hitState(seed, nL, t);
        stage(env, ctx);
        hud(env, ctx, '4K / FALLING NOTES', cumAt(seed, nL, hs.s0), cx - width / 2, H * (portrait ? 0.30 : 0.065), width);
        const base = ctx.createLinearGradient(0, top, 0, bottom);
        base.addColorStop(0, rgba(sc.bg, 0.18));
        base.addColorStop(1, rgba(sc.bg, 0.9));
        ctx.fillStyle = base;
        quad(0, nL, 0, 1);
        ctx.fill();
        for (let l = 0; l < nL; l++) {
            ctx.fillStyle = rgba(cols[l], l % 2 ? 0.04 : 0.025);
            quad(l, l + 1, 0, 1);
            ctx.fill();
            if (hs.beam[l] > 0) {
                const g = ctx.createLinearGradient(0, hit - (hit - top) * 0.35, 0, hit);
                g.addColorStop(0, rgba(cols[l], 0));
                g.addColorStop(1, rgba(cols[l], hs.beam[l] * 0.23));
                ctx.fillStyle = g;
                quad(l + 0.04, l + 0.96, 0.6, 1);
                ctx.fill();
            }
        }
        ctx.lineWidth = 1;
        for (let l = 0; l <= nL; l++) {
            ctx.strokeStyle = rgba(sc.fg, l === 0 || l === nL ? 0.3 : 0.12);
            ctx.beginPath();
            ctx.moveTo(...point(l, 0));
            ctx.lineTo(...point(l, 1));
            ctx.stroke();
        }
        // Scroll lines give depth without adding a second moving background.
        for (let i = 0; i < 8; i++) {
            const p = Math.pow(frac(i / 8 + t / 3.5), 1.6);
            ctx.strokeStyle = rgba(sc.fg, 0.035 + p * 0.045);
            ctx.beginPath();
            ctx.moveTo(...point(0, p));
            ctx.lineTo(...point(nL, p));
            ctx.stroke();
        }
        for (let s = hs.s0 + 10; s >= hs.s0 - 8; s--)
            for (const n of stepNotes(seed, nL, s)) {
                if ((s + n.len) * SD <= t)
                    continue;
                const dt = s * SD - t;
                if (dt > 1.75)
                    continue;
                const p = progress(Math.max(0, dt)), col = cols[n.l];
                if (n.len) {
                    const tail = progress((s + n.len) * SD - t);
                    ctx.fillStyle = rgba(col, dt < 0 ? 0.38 : 0.23);
                    quad(n.l + 0.24, n.l + 0.76, tail, p);
                    ctx.fill();
                    ctx.fillStyle = rgba(col, 0.65);
                    quad(n.l + 0.46, n.l + 0.54, tail, p);
                    ctx.fill();
                }
                if (dt >= 0 || n.len) {
                    const thickness = 0.012 + 0.015 * p;
                    ctx.fillStyle = col;
                    quad(n.l + 0.07, n.l + 0.93, Math.max(0, p - thickness), p);
                    ctx.fill();
                    ctx.fillStyle = rgba(sc.fg, 0.72);
                    quad(n.l + 0.07, n.l + 0.93, Math.max(0, p - thickness), Math.max(0, p - thickness + 0.004));
                    ctx.fill();
                }
            }
        const gate = ctx.createLinearGradient(cx - width / 2, 0, cx + width / 2, 0);
        gate.addColorStop(0, sc.accent);
        gate.addColorStop(0.5, sc.fg);
        gate.addColorStop(1, sc.accent);
        ctx.fillStyle = gate;
        ctx.fillRect(cx - width / 2, hit - 1.5, width, 3);
        const kw = width / nL, capH = bottom - hit;
        for (let l = 0; l < nL; l++) {
            const x = cx - width / 2 + l * kw, col = cols[l], beam = hs.beam[l];
            ctx.fillStyle = rgba(col, 0.055 + beam * 0.16);
            ctx.fillRect(x + 2, hit + 7, kw - 4, capH - 7);
            ctx.fillStyle = rgba(col, 0.28 + beam * 0.7);
            ctx.fillRect(x + kw * 0.16, hit + 11, kw * 0.68, 3);
            ctx.fillStyle = rgba(sc.fg, 0.48);
            ctx.textAlign = 'center';
            ctx.font = `500 ${Math.max(10, U * 0.019)}px ${MONO}`;
            ctx.fillText(['D', 'F', 'J', 'K'][l], x + kw / 2, bottom - capH * 0.2);
        }
        const last = lastStep(seed, nL, hs.s0), age = t - last * SD;
        if (last >= 0 && age < 0.35) {
            ctx.globalAlpha = 1 - age / 0.35;
            ctx.fillStyle = sc.fg;
            ctx.textAlign = 'center';
            ctx.font = `700 italic ${U * 0.031}px ${MONO}`;
            ctx.fillText(judgeWord(seed, last)[0], cx, hit - U * 0.085);
            ctx.globalAlpha = 1;
        }
    });
    const arrowPath = (ctx, cx, cy, size, dir) => {
        const points = [[-1, -0.3], [0.05, -0.3], [0.05, -0.75], [1, 0], [0.05, 0.75], [0.05, 0.3], [-1, 0.3]], angle = [Math.PI, Math.PI / 2, -Math.PI / 2, 0][dir];
        ctx.beginPath();
        points.forEach(([x, y], i) => { const X = cx + (x * Math.cos(angle) - y * Math.sin(angle)) * size, Y = cy + (x * Math.sin(angle) + y * Math.cos(angle)) * size; i ? ctx.lineTo(X, Y) : ctx.moveTo(X, Y); });
        ctx.closePath();
    };
    registerBg('echoStepArrows', '矢印', ['pop', 'graphic'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, lw = box.w / 4, hit = box.y + box.h * 0.12, speed = box.h / 1.65;
        const hs = hitState(seed, 4, t), cols = [sc.accent2, sc.accent, sc.accent, sc.accent2];
        stage(env, ctx);
        hud(env, ctx, '4A / STEP CHART', cumAt(seed, 4, hs.s0), box.x, box.y - H * 0.09, box.w);
        ctx.save();
        ctx.beginPath();
        ctx.rect(box.x, box.y, box.w, box.h);
        ctx.clip();
        for (let i = 0; i < 4; i++) {
            const x = box.x + (i + 0.5) * lw;
            ctx.fillStyle = rgba(sc.fg, i % 2 ? 0.02 : 0.045);
            ctx.fillRect(box.x + i * lw, box.y, lw, box.h);
            ctx.strokeStyle = rgba(cols[i], 0.4 + 0.6 * hs.beam[i]);
            ctx.lineWidth = 2;
            arrowPath(ctx, x, hit, lw * 0.32, i);
            ctx.stroke();
            ctx.fillStyle = rgba(cols[i], hs.beam[i] * 0.25);
            ctx.fill();
        }
        for (let s = hs.s0 + 10; s >= hs.s0 - 8; s--)
            for (const n of stepNotes(seed, 4, s)) {
                if ((s + n.len) * SD <= t)
                    continue;
                const x = box.x + (n.l + 0.5) * lw, y = hit + (s * SD - t) * speed, col = cols[n.l];
                if (n.len) {
                    const tail = hit + ((s + n.len) * SD - t) * speed;
                    ctx.fillStyle = rgba(col, 0.22);
                    ctx.fillRect(x - lw * 0.12, Math.max(hit, y), lw * 0.24, tail - Math.max(hit, y));
                }
                if (s * SD > t) {
                    ctx.fillStyle = col;
                    arrowPath(ctx, x, y, lw * 0.28, n.l);
                    ctx.fill();
                }
            }
        ctx.restore();
    });
    registerBg('echoTurntableChart', 'DJ機材', ['graphic', 'glitch'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, step = Math.floor(t / SD), lx = box.x + box.w * 0.38, lw = box.w * 0.62 / 6, hit = box.y + box.h * 0.86;
        stage(env, ctx);
        hud(env, ctx, 'SCRATCH / 6K', cumAt(seed, 6, step), box.x, box.y - H * 0.09, box.w);
        const cx = box.x + box.w * 0.17, cy = box.y + box.h * 0.64, R = box.w * 0.145;
        ctx.fillStyle = rgba(sc.fg, 0.055);
        ctx.beginPath();
        ctx.arc(cx, cy, R, 0, TAU);
        ctx.fill();
        for (let i = 0; i < 5; i++) {
            ctx.strokeStyle = rgba(sc.fg, 0.12);
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.arc(cx, cy, R * (0.4 + i * 0.14), 0, TAU);
            ctx.stroke();
        }
        ctx.strokeStyle = sc.accent2;
        ctx.lineWidth = 3;
        ctx.beginPath();
        ctx.arc(cx, cy, R * 0.9, t * 0.7, t * 0.7 + TAU * 0.28);
        ctx.stroke();
        ctx.fillStyle = sc.accent2;
        ctx.beginPath();
        ctx.arc(cx, cy, R * 0.16, 0, TAU);
        ctx.fill();
        ctx.save();
        ctx.beginPath();
        ctx.rect(lx, box.y, box.w * 0.62, box.h);
        ctx.clip();
        for (let l = 0; l < 6; l++) {
            ctx.fillStyle = rgba(sc.fg, l % 2 ? 0.04 : 0.02);
            ctx.fillRect(lx + l * lw, box.y, lw - 1, box.h);
        }
        for (let s = step + 10; s > step; s--)
            for (const n of stepNotes(seed, 6, s)) {
                const y = hit - (s * SD - t) * box.h / 1.65, col = n.l % 2 ? sc.accent2 : sc.accent;
                if (n.len) {
                    ctx.fillStyle = rgba(col, 0.2);
                    ctx.fillRect(lx + (n.l + 0.33) * lw, y - n.len * SD * box.h / 1.65, lw * 0.34, n.len * SD * box.h / 1.65);
                }
                ctx.fillStyle = col;
                rr(ctx, lx + n.l * lw + 2, y, lw - 4, Math.max(4, H * 0.008), 1);
                ctx.fill();
            }
        ctx.fillStyle = sc.fg;
        ctx.fillRect(lx, hit, box.w * 0.62, 2);
        ctx.restore();
        const beams = hitState(seed, 6, t).beam;
        for (let l = 0; l < 6; l++) {
            ctx.fillStyle = rgba(l % 2 ? sc.accent2 : sc.accent, 0.12 + beams[l] * 0.65);
            ctx.fillRect(lx + l * lw + 2, hit + 8, lw - 4, box.h * 0.07);
        }
    });
    registerBg('echoFretboard', '指板', ['graphic', 'pop'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, step = Math.floor(t / SD), cx = box.x + box.w / 2, top = box.y, hit = box.y + box.h * 0.88;
        const pos = (lane, p) => [cx + (lane / 5 - 0.5) * box.w * (0.2 + 0.8 * p), top + p * (hit - top)], cols = [sc.accent, sc.accent2, '#EDC882', sc.accent2, sc.accent];
        stage(env, ctx);
        hud(env, ctx, '5F / FRET CHART', cumAt(seed, 5, step), box.x, box.y - H * 0.09, box.w);
        ctx.fillStyle = rgba(sc.fg, 0.035);
        ctx.beginPath();
        for (const [i, p] of [[0, 0], [5, 0], [5, 1], [0, 1]]) {
            const [x, y] = pos(i, p);
            ctx.lineTo(x, y);
        }
        ctx.closePath();
        ctx.fill();
        ctx.strokeStyle = rgba(sc.fg, 0.18);
        ctx.lineWidth = 1;
        for (let i = 0; i <= 5; i++) {
            ctx.beginPath();
            ctx.moveTo(...pos(i, 0));
            ctx.lineTo(...pos(i, 1));
            ctx.stroke();
        }
        for (let i = 0; i < 10; i++) {
            const p = frac(i / 10 + t * 0.28);
            ctx.strokeStyle = rgba(sc.fg, 0.08 + p * 0.07);
            ctx.beginPath();
            ctx.moveTo(...pos(0, p));
            ctx.lineTo(...pos(5, p));
            ctx.stroke();
        }
        for (let s = step + 8; s > step; s--)
            for (const n of stepNotes(seed, 5, s)) {
                const p = 1 - (s * SD - t) / 1.5;
                if (p < 0 || p > 1)
                    continue;
                const [x, y] = pos(n.l + 0.5, p), w = box.w * (0.2 + 0.8 * p) / 5 * 0.7;
                ctx.fillStyle = cols[n.l];
                ctx.beginPath();
                ctx.ellipse(x, y, w / 2, Math.max(2, w * 0.14), 0, 0, TAU);
                ctx.fill();
            }
        const beams = hitState(seed, 5, t).beam;
        for (let i = 0; i < 5; i++) {
            const [x, y] = pos(i + 0.5, 1);
            ctx.strokeStyle = cols[i];
            ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.ellipse(x, y, box.w * 0.065, box.w * 0.018, 0, 0, TAU);
            ctx.stroke();
            ctx.fillStyle = rgba(cols[i], beams[i] * 0.4);
            ctx.fill();
        }
    });
    registerBg('echoPianoRain', 'ピアノ', ['calm', 'graphic'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, step = Math.floor(t / SD), keys = 10, kw = box.w / keys, hit = box.y + box.h * 0.83;
        stage(env, ctx);
        hud(env, ctx, 'PIANO / NOTE RAIN', cumAt(seed, keys, step), box.x, box.y - H * 0.09, box.w);
        ctx.save();
        ctx.beginPath();
        ctx.rect(box.x, box.y, box.w, box.h);
        ctx.clip();
        for (let s = step + 10; s > step; s--)
            for (const n of stepNotes(seed, keys, s)) {
                const y = hit - (s * SD - t) * box.h / 1.8, col = n.l % 2 ? sc.accent2 : sc.accent;
                ctx.fillStyle = rgba(col, 0.5);
                rr(ctx, box.x + (n.l + 0.31) * kw, y, kw * 0.38, box.h * 0.09, 2);
                ctx.fill();
                ctx.fillStyle = col;
                ctx.fillRect(box.x + (n.l + 0.31) * kw, y + box.h * 0.08, kw * 0.38, box.h * 0.01);
            }
        const beams = hitState(seed, keys, t).beam;
        for (let k = 0; k < keys; k++) {
            const x = box.x + k * kw;
            ctx.fillStyle = K.mix(sc.fg, sc.accent, beams[k] * 0.65);
            rr(ctx, x + 1, hit, kw - 2, box.h * 0.16, 1);
            ctx.fill();
            if ([0, 1, 3, 4, 5, 7, 8].includes(k)) {
                ctx.fillStyle = sc.bg;
                rr(ctx, x + kw * 0.68, hit, kw * 0.6, box.h * 0.095, 1);
                ctx.fill();
            }
        }
        ctx.restore();
    });
})();
