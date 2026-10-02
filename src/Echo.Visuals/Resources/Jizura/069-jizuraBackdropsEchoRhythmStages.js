/* Original arcade percussion, score summary and scanning notebook scenes. */
(() => {
    'use strict';
    const K = J.echo, { clamp, rgba, mix, TAU } = K, r = J.r;
    const { frac, MONO, rr, BPM, SD, stage, hud, arena, registerBg } = K.rhythm;
    const pattern = ['d.d.k.d.', 'dd.dk.k.', 'd.kd.kdk', 'D...K...', 'dkdk.d.D', 'd.dd.k.k'];
    const drumAt = (seed, step) => step < 0 ? '.' : pattern[(Math.floor(step / 8) + seed) % pattern.length][step % 8];
    registerBg('echoTaiko', '太鼓', ['pop', 'graphic'], (env, P, ctx) => {
        const { W, H, sc, t } = env, box = arena(env), seed = P.seed || 1;
        stage(env, ctx, sc.accent2);
        const radius = Math.min(box.w * 0.067, box.h * 0.12), y = box.y + box.h * 0.48;
        const hit = box.x + box.w * 0.17, speed = box.w * 0.53, step = Math.floor(t / SD);
        hud(env, ctx, 'DRUM / DON · KA', Math.floor(t / SD * 0.7), box.x, box.y - H * 0.09, box.w);
        ctx.fillStyle = rgba(sc.fg, 0.04);
        rr(ctx, box.x, y - radius * 1.6, box.w, radius * 3.2, 3);
        ctx.fill();
        ctx.strokeStyle = rgba(sc.fg, 0.14);
        ctx.lineWidth = 1;
        for (const edge of [-1.6, 1.6]) {
            ctx.beginPath();
            ctx.moveTo(box.x, y + radius * edge);
            ctx.lineTo(box.x + box.w, y + radius * edge);
            ctx.stroke();
        }
        ctx.strokeStyle = sc.fg;
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(hit, y, radius * 1.25, 0, TAU);
        ctx.stroke();
        ctx.save();
        ctx.beginPath();
        ctx.rect(hit - radius * 1.7, y - radius * 1.7, box.w - (hit - box.x) + radius * 1.7, radius * 3.4);
        ctx.clip();
        for (let s = step; s < step + 16; s++) {
            const type = drumAt(seed, s);
            if (type === '.')
                continue;
            const x = hit + (s * SD - t) * speed, c = type.toLowerCase() === 'd' ? sc.accent2 : sc.accent;
            const R = radius * (type === type.toUpperCase() ? 1.16 : 0.88);
            ctx.fillStyle = c;
            ctx.beginPath();
            ctx.arc(x, y, R, 0, TAU);
            ctx.fill();
            ctx.strokeStyle = sc.bg;
            ctx.lineWidth = R * 0.12;
            ctx.stroke();
            ctx.fillStyle = sc.bg;
            ctx.textAlign = 'center';
            ctx.font = `700 ${R * 0.62}px ${MONO}`;
            ctx.fillText(type.toLowerCase() === 'd' ? 'D' : 'K', x, y + R * 0.2);
        }
        ctx.restore();
        const pulse = drumAt(seed, step) === '.' ? 0 : Math.pow(1 - frac(t / SD), 3);
        ctx.fillStyle = rgba(sc.accent2, pulse * 0.22);
        ctx.beginPath();
        ctx.arc(hit, y, radius * 1.8, 0, TAU);
        ctx.fill();
        ctx.fillStyle = rgba(sc.fg, 0.45);
        ctx.textAlign = 'center';
        ctx.font = `500 ${Math.max(10, H * 0.018)}px ${MONO}`;
        ctx.fillText('DON', box.x + box.w * 0.4, y + radius * 3.2);
        ctx.fillText('KA', box.x + box.w * 0.7, y + radius * 3.2);
    });
    /* A score screen uses oversized accuracy, an angular rank and a compact hit-timing plot. */
    registerBg('echoResultScreen', 'リザルト', ['graphic', 'emotional'], (env, P, ctx) => {
        const { H, sc } = env, box = arena(env), seed = P.seed || 1, dark = K.isDark(sc.bg);
        const gold = dark ? '#EDC882' : '#94621A', ink = sc.fg, muted = rgba(ink, 0.55);
        stage(env, ctx, gold);
        const x = box.x, y = box.y, w = box.w, h = box.h;
        ctx.textAlign = 'left';
        ctx.font = `700 ${Math.max(10, w * 0.032)}px ${MONO}`;
        ctx.fillStyle = muted;
        ctx.fillText('RESULT', x, y - H * 0.055);
        ctx.textAlign = 'right';
        ctx.fillStyle = gold;
        ctx.fillText('AUTO / FULL COMBO', x + w, y - H * 0.055);
        // The rank is an original angular S, not a badge or borrowed game logo.
        ctx.save();
        ctx.translate(x + w * 0.81, y + h * 0.19);
        ctx.transform(1, 0, -0.16, 1, 0, 0);
        ctx.strokeStyle = rgba(gold, 0.11);
        ctx.lineWidth = w * 0.05;
        ctx.lineJoin = 'bevel';
        const rank = () => { ctx.beginPath(); ctx.moveTo(w * 0.11, -h * 0.13); ctx.lineTo(-w * 0.055, -h * 0.13); ctx.lineTo(-w * 0.11, -h * 0.08); ctx.lineTo(-w * 0.11, -h * 0.015); ctx.lineTo(w * 0.07, h * 0.035); ctx.lineTo(w * 0.105, h * 0.075); ctx.lineTo(w * 0.105, h * 0.13); ctx.lineTo(-w * 0.11, h * 0.13); };
        rank();
        ctx.stroke();
        ctx.strokeStyle = gold;
        ctx.lineWidth = w * 0.023;
        rank();
        ctx.stroke();
        ctx.restore();
        ctx.textAlign = 'left';
        ctx.fillStyle = muted;
        ctx.font = `600 ${Math.max(9, w * 0.026)}px ${MONO}`;
        ctx.fillText('ACCURACY', x, y + h * 0.075);
        ctx.fillStyle = ink;
        ctx.font = `800 ${w * 0.137}px ${MONO}`;
        ctx.fillText('99.32', x - w * 0.008, y + h * 0.235);
        ctx.font = `600 ${w * 0.047}px ${MONO}`;
        ctx.fillText('%', x + w * 0.43, y + h * 0.235);
        ctx.fillStyle = sc.accent;
        ctx.fillRect(x, y + h * 0.32, w * 0.855, Math.max(2, h * 0.006));
        ctx.fillStyle = sc.accent2;
        ctx.fillRect(x + w * 0.855, y + h * 0.32, w * 0.125, Math.max(2, h * 0.006));
        ctx.fillStyle = gold;
        ctx.fillRect(x + w * 0.98, y + h * 0.32, w * 0.02, Math.max(2, h * 0.006));
        const labels = ['PERFECT', 'GREAT', 'GOOD'], values = ['0842', '0120', '0000'], colors = [sc.accent, sc.accent2, gold];
        for (let i = 0; i < 3; i++) {
            const X = x + w * i / 3;
            ctx.fillStyle = ink;
            ctx.font = `600 ${w * 0.062}px ${MONO}`;
            ctx.fillText(values[i], X, y + h * 0.49);
            ctx.fillStyle = colors[i];
            ctx.font = `600 ${Math.max(9, w * 0.025)}px ${MONO}`;
            ctx.fillText(labels[i], X, y + h * 0.555);
        }
        const plotY = y + h * 0.75, plotH = h * 0.17;
        ctx.fillStyle = rgba(sc.accent, 0.035);
        ctx.fillRect(x, plotY - plotH / 2, w, plotH);
        ctx.strokeStyle = rgba(ink, 0.16);
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, plotY);
        ctx.lineTo(x + w, plotY);
        ctx.stroke();
        for (let i = 0; i < 56; i++) {
            const offset = (r(seed, i, 19) - 0.5) * plotH * 0.82, X = x + w * (i + 0.5) / 56;
            ctx.fillStyle = Math.abs(offset) < plotH * 0.25 ? sc.accent : sc.accent2;
            ctx.fillRect(X, plotY + offset - 1.5, Math.max(2, w / 140), 3);
        }
        ctx.fillStyle = muted;
        ctx.font = `500 ${Math.max(9, w * 0.023)}px ${MONO}`;
        ctx.fillText('HIT TIMING', x, y + h * 0.66);
        ctx.fillText('EARLY', x, y + h * 0.92);
        ctx.textAlign = 'right';
        ctx.fillText('LATE', x + w, y + h * 0.92);
    });
    registerBg('echoScanPage', 'ノート', ['calm', 'graphic'], (env, P, ctx) => {
        const { H, sc, t } = env, box = arena(env), seed = P.seed || 1, period = 60 / BPM * 2, scan = frac(t / period);
        stage(env, ctx);
        hud(env, ctx, 'SCAN / NOTEBOOK', Math.floor(t / period) * 4, box.x, box.y - H * 0.09, box.w);
        ctx.fillStyle = rgba(sc.fg, 0.025);
        rr(ctx, box.x, box.y, box.w, box.h, 2);
        ctx.fill();
        ctx.strokeStyle = rgba(sc.fg, 0.2);
        ctx.lineWidth = 1;
        ctx.stroke();
        for (let i = 1; i < 9; i++) {
            ctx.strokeStyle = rgba(sc.fg, 0.08);
            ctx.beginPath();
            ctx.moveTo(box.x, box.y + box.h * i / 9);
            ctx.lineTo(box.x + box.w, box.y + box.h * i / 9);
            ctx.stroke();
        }
        const scanY = box.y + box.h * scan;
        for (let i = 0; i < 9; i++) {
            const x = box.x + box.w * (0.12 + 0.76 * r(seed, i, 1)), y = box.y + box.h * (0.12 + 0.76 * r(seed, i, 2)), R = Math.min(box.w, box.h) * 0.04;
            const near = Math.abs(y - scanY) / box.h, alpha = near < 0.08 ? 0.9 : 0.27;
            ctx.strokeStyle = rgba(i % 2 ? sc.accent2 : sc.accent, alpha);
            ctx.lineWidth = near < 0.08 ? 3 : 1.5;
            ctx.beginPath();
            ctx.arc(x, y, R, 0, TAU);
            ctx.stroke();
            ctx.fillStyle = rgba(sc.fg, alpha);
            ctx.textAlign = 'center';
            ctx.font = `500 ${R}px ${MONO}`;
            ctx.fillText(String(i + 1), x, y + R * 0.3);
        }
        ctx.fillStyle = rgba(sc.accent, 0.09);
        ctx.fillRect(box.x, scanY - box.h * 0.035, box.w, box.h * 0.035);
        ctx.fillStyle = sc.accent;
        ctx.fillRect(box.x, scanY, box.w, 1.5);
    });
})();
