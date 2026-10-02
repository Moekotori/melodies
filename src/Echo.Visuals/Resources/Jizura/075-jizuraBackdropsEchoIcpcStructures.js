/* Fixed, real traces for lazy propagation, subset DP and prime sieving.
   Inputs are eight values, five cities and integers through 97. No growing state or caches. */
(() => {
  'use strict';
  const K = J.echo, A = K.icpcDiagram;
  const values = [3, 1, 4, 1, 5, 9, 2, 6], sum = Array(16).fill(0), lazy = Array(16).fill(0), spans = Array(16), SEGMENT = [];
  const build = (v, lo, hi) => {
    spans[v] = [lo, hi];
    if (lo === hi) { sum[v] = values[lo]; return; }
    const mid = (lo + hi) >> 1; build(v * 2, lo, mid); build(v * 2 + 1, mid + 1, hi); sum[v] = sum[v * 2] + sum[v * 2 + 1];
  };
  build(1, 0, 7);
  const save = (v, range, status) => SEGMENT.push({ v, range, status, sum: sum.slice(), lazy: lazy.slice() });
  const apply = (v, delta) => { sum[v] += (spans[v][1] - spans[v][0] + 1) * delta; lazy[v] += delta; };
  const push = (v, range) => {
    if (!lazy[v] || spans[v][0] === spans[v][1]) return;
    apply(v * 2, lazy[v]); apply(v * 2 + 1, lazy[v]); lazy[v] = 0; save(v, range, 'PUSH / PROPAGATE TO BOTH CHILDREN');
  };
  const update = (v, lo, hi, delta) => {
    const [l, r] = spans[v]; if (hi < l || r < lo) return;
    save(v, [lo, hi], `VISIT [${l}, ${r}] / RANGE ADD ${delta > 0 ? '+' : ''}${delta}`);
    if (lo <= l && r <= hi) { apply(v, delta); save(v, [lo, hi], `APPLY / SUM += LENGTH × ${delta}`); return; }
    push(v, [lo, hi]); update(v * 2, lo, hi, delta); update(v * 2 + 1, lo, hi, delta);
    sum[v] = sum[v * 2] + sum[v * 2 + 1]; save(v, [lo, hi], 'PULL / RECOMBINE CHILD SUMS');
  };
  const query = (v, lo, hi) => {
    const [l, r] = spans[v]; if (hi < l || r < lo) return 0;
    save(v, [lo, hi], `QUERY / VISIT [${l}, ${r}]`);
    if (lo <= l && r <= hi) return sum[v];
    push(v, [lo, hi]); return query(v * 2, lo, hi) + query(v * 2 + 1, lo, hi);
  };
  save(1, [0, 7], `BUILD / SUM ${sum[1]}`); update(1, 0, 3, 2);
  const first = query(1, 2, 6); save(1, [2, 6], `QUERY [2, 6] = ${first}`);
  update(1, 3, 7, -1); const total = query(1, 0, 7); save(1, [0, 7], `QUERY [0, 7] = ${total} / O(log n)`);
  K.registerBg('echoLazySegmentTree', 'Segment Tree / Lazy Propagation', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { s, phase } = A.at(env, SEGMENT, 11), T = A.frame(env, ctx, 'LAZY SEGMENT TREE', 'RANGE ADD / RANGE SUM / DELAY THE WORK', s.status), U = T.U;
    const rotation = 0.12 + Math.sin(env.t * 0.13) * 0.18;
    const pos = v => { const [lo, hi] = spans[v], level = Math.floor(Math.log2(v)); return A.project(env, ((lo + hi + 1) / 8 - 1) * 1.85, 1.15 - level * 0.65, level * 0.2 - 0.3, rotation); };
    for (let level = 0; level < 4; level++) A.ring(env, ctx, T, 0.48 + level * 0.48, 1.15 - level * 0.65, rotation, 0.15);
    for (let v = 2; v < 16; v++) {
      const active = v === s.v || (v >> 1) === s.v && s.lazy[s.v] !== 0;
      A.edge(ctx, pos(v >> 1), pos(v), active ? T.b : K.rgba(T.a, 0.3), Math.max(1, U * 0.0025), active ? phase : K.wrap(env.t * 0.3 + v * 0.13, 1));
    }
    for (let v = 1; v < 16; v++) {
      const [x, y] = pos(v), active = v === s.v;
      const level = Math.floor(Math.log2(v)), width = U * (0.08 - level * 0.013), height = U * (s.lazy[v] ? 0.045 : 0.018);
      A.tile(ctx, x, y, width, width * 0.32, height, active || s.lazy[v] ? T.b : T.a, active);
      if (active || s.lazy[v]) A.node(ctx, x, y, U * 0.006, T.b, active);
      A.text(ctx, s.sum[v], x, y - U * 0.028, U * (level === 0 ? 0.055 : 0.03), active ? T.b : T.fg, 'center');
      if (s.lazy[v]) A.text(ctx, `+(${s.lazy[v]})`, x + width * 0.7, y - U * 0.05, U * 0.016, T.b);
      const [lo, hi] = spans[v]; A.text(ctx, lo === hi ? lo : `[${lo},${hi}]`, x, y + height + width * 0.35 + U * 0.014, U * 0.012, T.dim, 'center');
    }
    A.text(ctx, `ACTIVE RANGE / [${s.range.join(', ')}]`, env.W * 0.055, env.H * 0.955, U * 0.014, T.a);
    const isQuery = s.status.startsWith('QUERY');
    A.code(env, ctx, T, isQuery ? [
      'auto query = [&](auto&& self,',
      '  int v,int l,int r,int L,int R)',
      '  -> long long {',
      '  if (R<l || r<L) return 0;',
      '  if (L<=l && r<=R)',
      '    return sum[v];',
      '  push(v, r-l+1);',
      '  int m = (l+r)/2;',
      '  return self(self,v*2,l,m,L,R)',
      '    + self(self,v*2+1,m+1,r,L,R);',
      '};',
    ] : [
      'void apply(int v,int len,int d) {',
      '  sum[v] += 1LL * len * d;',
      '  lazy[v] += d;',
      '}',
      'void push(int v,int len) {',
      '  apply(v*2, len/2, lazy[v]);',
      '  apply(v*2+1, len/2, lazy[v]);',
      '  lazy[v] = 0;',
      '}',
      'void pull(int v) {',
      '  sum[v] = sum[v*2]+sum[v*2+1];',
      '}',
    ], isQuery ? 5 : s.status.startsWith('PUSH') ? 6 : s.status.startsWith('APPLY') ? 1 : s.status.startsWith('VISIT') ? 0 : 10);
  }, false);

  const CITIES = [[0.5, 0], [0.98, 0.37], [0.79, 0.98], [0.19, 0.91], [0, 0.31]];
  const weights = CITIES.map(([x, y]) => CITIES.map(([xx, yy]) => Math.round(Math.hypot(x - xx, y - yy) * 20)));
  const dp = Array.from({ length: 32 }, () => Array(5).fill(Infinity)), prev = Array.from({ length: 32 }, () => Array(5).fill(-1)), SUBSETS = [];
  dp[1][0] = 0;
  const route = (mask, v) => { const result = []; while (v >= 0) { result.push(v); const next = prev[mask][v]; mask ^= 1 << v; v = next; } return result.reverse(); };
  SUBSETS.push({ mask: 1, v: 0, value: 0, path: [0] });
  for (let mask = 1; mask < 32; mask += 2) for (let u = 0; u < 5; u++) {
    if (!Number.isFinite(dp[mask][u])) continue;
    for (let v = 1; v < 5; v++) {
      if (mask & (1 << v)) continue;
      const next = mask | (1 << v), cost = dp[mask][u] + weights[u][v];
      if (cost >= dp[next][v]) continue;
      dp[next][v] = cost; prev[next][v] = u;
      SUBSETS.push({ mask: next, v, value: cost, path: route(next, v) });
    }
  }
  let end = 1; for (let v = 2; v < 5; v++) if (dp[31][v] + weights[v][0] < dp[31][end] + weights[end][0]) end = v;
  const optimum = dp[31][end] + weights[end][0];
  SUBSETS.push({ mask: 31, v: end, value: optimum, path: [...route(31, end), 0] });
  K.registerBg('echoSubsetTour', 'Subset DP / Traveling Salesman', ['graphic', 'emotional', 'glitch'], (env, P, ctx) => {
    const { s, index, phase, done } = A.at(env, SUBSETS, 11), T = A.frame(env, ctx, 'SUBSET DYNAMIC PROGRAMMING', 'VISITED MASK × LAST CITY / TRAVELING SALESMAN', done ? `OPTIMAL CYCLE / COST ${optimum}` : `RELAX dp[${s.mask.toString(2).padStart(5, '0')}][${String.fromCharCode(65 + s.v)}] = ${s.value}`), U = T.U;
    const p = CITIES.map(([x, y]) => [env.W * (0.73 + (x - 0.5) * 0.3), env.H * (0.66 + (y - 0.5) * 0.24)]);
    for (let u = 0; u < 5; u++) for (let v = u + 1; v < 5; v++) {
      A.edge(ctx, p[u], p[v], T.faint, 1);
      A.text(ctx, weights[u][v], (p[u][0] + p[v][0]) / 2, (p[u][1] + p[v][1]) / 2 - U * 0.008, U * 0.011, T.dim);
    }
    for (let i = 1; i < s.path.length; i++) A.edge(ctx, p[s.path[i - 1]], p[s.path[i]], i === s.path.length - 1 ? T.b : T.a, U * 0.003, i === s.path.length - 1 ? phase : -1);
    p.forEach(([x, y], i) => { A.node(ctx, x, y, U * 0.009, s.mask & (1 << i) ? T.a : T.dim, i === s.v); A.text(ctx, String.fromCharCode(65 + i), x, y + U * 0.03, U * 0.019, T.fg, 'center'); });
    // Replay sparse relaxations into a tiny transient 160-cell table; retain no frame history.
    const table = new Float64Array(160); table.fill(Infinity);
    for (let i = 0; i <= index; i++) { const event = SUBSETS[i]; if (i < SUBSETS.length - 1) table[event.mask * 5 + event.v] = event.value; }
    // The four non-source bits form a projected 4D cube. Every edge adds one visited city.
    const lattice = Array.from({ length: 16 }, (_, bits) => {
      const b = Array.from({ length: 4 }, (_, i) => (bits >> i & 1) - 0.5);
      return A.project(env, (b[0] + b[2] * 0.35 + b[3] * 0.7) * 1.8, (b[1] + b[3] * 0.6) * 1.5,
        (b[2] + b[3] * 0.65) * 1.8, env.t * 0.08, env.H > env.W ? 0.45 : 0.34, 0.53, 0.65);
    });
    for (let bits = 0; bits < 16; bits++) for (let bit = 0; bit < 4; bit++) {
      if (bits & (1 << bit)) continue;
      const next = bits | (1 << bit), mask = next * 2 + 1, ready = Number.isFinite(Math.min(...table.subarray(mask * 5, mask * 5 + 5)));
      A.edge(ctx, lattice[bits], lattice[next], mask === s.mask ? T.b : ready ? K.rgba(T.a, 0.6) : T.faint, U * 0.002,
        mask === s.mask ? phase : ready ? K.wrap(env.t * 0.23 + bit * 0.2 + bits * 0.1, 1) : -1);
    }
    lattice.forEach(([x, y, depth], bits) => {
      const mask = bits * 2 + 1, value = Math.min(...table.subarray(mask * 5, mask * 5 + 5)), active = mask === s.mask;
      A.node(ctx, x, y, U * 0.009 * depth, active ? T.b : Number.isFinite(value) ? T.a : T.dim, active);
      A.text(ctx, mask.toString(2).padStart(5, '0'), x, y + U * 0.024, U * 0.012, active ? T.b : T.dim, 'center');
      if (Number.isFinite(value)) A.text(ctx, value, x, y - U * 0.024, U * 0.02, T.fg, 'center');
    });
    A.text(ctx, s.mask.toString(2).padStart(5, '0'), env.W * 0.055, env.H * 0.22, U * 0.055, T.b);
    A.code(env, ctx, T, [
      'dp[1][0] = 0;',
      'for (int m=1; m<(1<<n); ++m)',
      '  for (int u=0; u<n; ++u) {',
      '    if (dp[m][u] == INF) continue;',
      '    for (int v=0; v<n; ++v) {',
      '      if (m & (1<<v)) continue;',
      '      int next = m | (1<<v);',
      '      dp[next][v] = min(',
      '        dp[next][v],',
      '        dp[m][u] + w[u][v]);',
      '    }',
      '  }',
    ], done ? 11 : 9);
  }, false);

  const composite = Array(98).fill(false), SIEVE = []; composite[0] = composite[1] = true;
  SIEVE.push({ p: 2, multiple: -1, composite: composite.slice() });
  for (let p = 2; p * p <= 97; p++) {
    if (composite[p]) continue;
    for (let multiple = p * p; multiple <= 97; multiple += p) {
      if (composite[multiple]) continue;
      composite[multiple] = true; SIEVE.push({ p, multiple, composite: composite.slice() });
    }
  }
  const primes = composite.reduce((n, crossed) => n + (crossed ? 0 : 1), 0);
  SIEVE.push({ p: 7, multiple: -1, composite: composite.slice() });
  const ULAM = Array(98); ULAM[1] = [0, 0];
  const directions = [[1, 0], [0, -1], [-1, 0], [0, 1]];
  let spiralX = 0, spiralZ = 0, number = 2, direction = 0;
  for (let length = 1; number <= 97; length++) for (let turn = 0; turn < 2; turn++) {
    for (let step = 0; step < length && number <= 97; step++) { spiralX += directions[direction][0]; spiralZ += directions[direction][1]; ULAM[number++] = [spiralX, spiralZ]; }
    direction = (direction + 1) % 4;
  }
  K.registerBg('echoPrimeSieve', 'Prime Sieve / Eratosthenes', ['graphic', 'calm', 'editorial'], (env, P, ctx) => {
    const { s, phase, done } = A.at(env, SIEVE, 11), T = A.frame(env, ctx, 'PRIME SIEVE', 'START AT p² / CROSS OUT EVERY NEW MULTIPLE', done ? `${primes} PRIMES / 2 THROUGH 97` : `p = ${s.p} / MARK ${s.multiple < 0 ? 'p²' : s.multiple}`), U = T.U;
    const rotation = 0.65 + Math.sin(env.t * 0.09) * 0.2;
    const pos = (n, height = -0.4) => A.project(env, ULAM[n][0] * 0.32, height, ULAM[n][1] * 0.32, rotation, 0.5, 0.58, 1.13);
    A.ring(env, ctx, T, 2.4, -0.45, rotation, 0.18, 0.5, 0.58);
    for (let n = 2; n < 97; n++) A.edge(ctx, pos(n), pos(n + 1), K.rgba(T.a, 0.08), 1);
    if (s.multiple >= 0) A.edge(ctx, pos(s.p, 0.65), pos(s.multiple), T.b, U * 0.003, phase);
    const tiles = Array.from({ length: 96 }, (_, i) => ({ n: i + 2, p: pos(i + 2) })).sort((a, b) => a.p[2] - b.p[2]);
    for (const { n, p: [x, ground, depth] } of tiles) {
      const active = n === s.multiple, prime = !s.composite[n] && (done || n <= s.p);
      const height = U * depth * (n === s.p ? 0.15 : active ? 0.015 + (1 - phase) * 0.07 : prime ? 0.065 : s.composite[n] ? 0.008 : 0.025);
      const y = ground - height, width = U * 0.023 * depth, color = active || n === s.p ? T.b : prime ? T.a : T.dim;
      A.tile(ctx, x, y, width, width * 0.48, height, color, active || n === s.p);
      A.text(ctx, n, x, y - width * 0.75, U * 0.015 * depth, s.composite[n] && !active ? K.rgba(T.fg, 0.23) : prime || n === s.p ? T.a : T.dim, 'center');
      if (prime || n === s.p) A.node(ctx, x, y, U * 0.003 * depth, n === s.p ? T.b : T.a, n === s.p);
    }
    A.text(ctx, 'ULAM / SPIRAL INTEGER LATTICE', env.W * 0.055, env.H * 0.83, U * 0.014, T.dim);
    A.code(env, ctx, T, [
      'for (int p=2; p*p<=N; ++p) {',
      '  if (composite[p]) continue;',
      '  for (int x=p*p; x<=N; x+=p)',
      '    composite[x] = true;',
      '}',
      '// Unmarked integers are prime.',
    ], done ? 5 : 3);
  }, false);
})();
