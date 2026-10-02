/* Original range-query sculptures. Immutable 23-node persistent pool and 16-cell Fenwick tree.
   All operation traces are fixed at load time and disappear with the JIZURA frame. */
(() => {
  'use strict';
  const K = J.echo, A = K.icpcDiagram;
  const input = [2, 7, 1, 8, 2, 8, 1, 8], pool = [], versions = [], PERSIST = [];
  const append = (lo, hi, left, right, value, version) => {
    const id = pool.length; pool.push({ lo, hi, left, right, value, version }); return id;
  };
  const build = (lo, hi) => {
    if (lo === hi) return append(lo, hi, -1, -1, input[lo], 0);
    const mid = (lo + hi) >> 1, left = build(lo, mid), right = build(mid + 1, hi);
    return append(lo, hi, left, right, pool[left].value + pool[right].value, 0);
  };
  const snapshot = (active, version, status) => PERSIST.push({ active, version, count: pool.length, status });
  versions.push(build(0, 7)); snapshot(versions[0], 0, 'VERSION 0 / SUM 37');
  const add = (previous, position, delta, version) => {
    const old = pool[previous]; let left = old.left, right = old.right;
    if (old.lo !== old.hi) {
      const mid = (old.lo + old.hi) >> 1;
      if (position <= mid) left = add(left, position, delta, version); else right = add(right, position, delta, version);
    }
    const id = append(old.lo, old.hi, left, right, old.value + delta, version);
    snapshot(id, version, `PATH COPY / [${old.lo}, ${old.hi}] / SHARE UNCHANGED NODES`); return id;
  };
  versions.push(add(versions[0], 3, 5, 1)); snapshot(versions[1], 1, 'VERSION 1 / SUM 42 / VERSION 0 STILL 37');
  versions.push(add(versions[1], 6, -1, 2)); snapshot(versions[2], 2, 'VERSIONS PRESERVED / 37 → 42 → 41');
  K.registerBg('echoPersistentTree', 'Persistent Segment Tree / Version Forest', ['graphic', 'calm', 'glitch'], (env, P, ctx) => {
    const { s, phase } = A.at(env, PERSIST, 11), T = A.frame(env, ctx, 'PERSISTENT SEGMENT TREE', 'COPY ONE ROOT-TO-LEAF PATH / SHARE THE REST', s.status), U = T.U;
    const rotation = 0.32 + Math.sin(env.t * 0.1) * 0.18;
    const pos = id => { const node = pool[id], level = 3 - Math.log2(node.hi - node.lo + 1);
      return A.project(env, ((node.lo + node.hi + 1) / 8 - 1) * 1.75 + node.version * 0.15, 0.82 - level * 0.5,
        (node.version - 1) * 0.75, rotation, env.H > env.W ? 0.5 : 0.43, 0.56, 0.9); };
    for (let version = 0; version <= s.version; version++) A.ring(env, ctx, T, 1.8, -0.85 + version * 0.05, rotation, 0.12);
    for (let id = 0; id < s.count; id++) {
      const node = pool[id], current = node.version === s.version;
      for (const child of [node.left, node.right]) if (child >= 0) {
        const shared = pool[child].version !== node.version;
        A.edge(ctx, pos(id), pos(child), current ? shared ? T.b : T.a : T.faint, U * (current ? 0.0025 : 0.001),
          current ? K.wrap(env.t * 0.25 + id * 0.11, 1) : -1);
      }
    }
    const nodes = Array.from({ length: s.count }, (_, id) => ({ id, p: pos(id) })).sort((a, b) => a.p[2] - b.p[2]);
    for (const { id, p: [x, y, depth] } of nodes) {
      const n = pool[id], active = id === s.active, current = n.version === s.version, w = U * 0.034 * depth;
      A.tile(ctx, x, y, w, w * 0.38, U * 0.022, active ? T.b : current ? T.a : T.dim, active);
      if (active) A.node(ctx, x, y, U * 0.007, T.b, true);
      A.text(ctx, n.value, x, y - U * 0.023, U * 0.022, active ? T.b : current ? T.fg : T.dim, 'center');
      if (n.lo === 0 && n.hi === 7) A.text(ctx, `ROOT v${n.version}`, x, y - U * 0.065, U * 0.013, current ? T.a : T.dim, 'center');
    }
    A.text(ctx, `${s.count} NODES / SHARED STORAGE`, env.W * 0.055, env.H * 0.83, U * 0.014, T.a);
    A.code(env, ctx, T, [
      'int add(int old,int l,int r,',
      '        int pos,int delta) {',
      '  int v = clone(old);',
      '  sum[v] += delta;',
      '  if (l == r) return v;',
      '  int m = (l+r)/2;',
      '  if (pos <= m)',
      '    lc[v]=add(lc[old],l,m,pos,delta);',
      '  else',
      '    rc[v]=add(rc[old],m+1,r,pos,delta);',
      '  return v;',
      '}',
    ], pool[s.active].lo === pool[s.active].hi ? 4 : 10);
  }, false);

  const data = [0, 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5, 8, 9, 7, 9, 3], bit = Array(17).fill(0), FENWICK = [];
  for (let i = 1; i <= 16; i++) for (let v = i; v <= 16; v += v & -v) bit[v] += data[i];
  const save = (i, next, answer, mode, status) => FENWICK.push({ i, next, answer, mode, status, bit: bit.slice() });
  save(5, 6, 0, 'update', 'ADD(5, +4) / FOLLOW i += lowbit(i)'); data[5] += 4;
  for (let i = 5; i <= 16; i += i & -i) { bit[i] += 4; save(i, i + (i & -i), 0, 'update', `UPDATE NODE ${i} / COVER ${i & -i} VALUES`); }
  let answer = 0;
  for (let i = 13; i > 0; i -= i & -i) { answer += bit[i]; save(i, i - (i & -i), answer, 'query', `PREFIX(13) / ACCUMULATE ${answer}`); }
  save(0, 0, answer, 'query', `PREFIX(13) = ${answer} / O(log n)`);
  K.registerBg('echoFenwickTower', 'Fenwick Tree / Lowbit Towers', ['graphic', 'calm', 'editorial'], (env, P, ctx) => {
    const { s, phase } = A.at(env, FENWICK, 10), T = A.frame(env, ctx, 'FENWICK TREE', 'BINARY INTERVALS / JUMP BY THE LOWEST SET BIT', s.status), U = T.U;
    const rotation = 0.14 + Math.sin(env.t * 0.08) * 0.12;
    const pos = i => { const width = i & -i, level = Math.log2(width);
      return A.project(env, ((i - width / 2) / 16 - 0.5) * 3.5, -1 + level * 0.55, level * 0.1, rotation, env.H > env.W ? 0.5 : 0.44); };
    for (let level = 0; level < 5; level++) A.ring(env, ctx, T, 1.7 - level * 0.22, -1 + level * 0.55, rotation, 0.12);
    for (let i = 1; i < 16; i++) A.edge(ctx, pos(i), pos(i + (i & -i)), T.faint, 1);
    if (s.i > 0 && s.next > 0 && s.next <= 16) A.edge(ctx, pos(s.i), pos(s.next), T.b, U * 0.0035, phase);
    for (let i = 16; i >= 1; i--) {
      const [x, y, depth] = pos(i), width = i & -i, active = s.i === i, w = U * 0.024 * Math.pow(width, 0.6) * depth;
      A.tile(ctx, x, y, w, w * 0.23, U * 0.025, active ? T.b : T.a, active);
      A.text(ctx, s.bit[i], x, y - U * 0.027, U * (width > 4 ? 0.04 : 0.025), active ? T.b : T.fg, 'center');
      A.text(ctx, `[${i - width + 1},${i}]`, x, y + U * 0.045, U * 0.012, T.dim, 'center');
      if (active) A.node(ctx, x, y, U * 0.007, T.b, true);
    }
    A.text(ctx, s.i.toString(2).padStart(5, '0'), env.W * 0.055, env.H * (env.H > env.W ? 0.44 : 0.23), U * 0.045, T.b);
    A.text(ctx, `lowbit = ${s.i & -s.i}`, env.W * 0.055, env.H * (env.H > env.W ? 0.48 : 0.28), U * 0.018, T.dim);
    A.code(env, ctx, T, [
      'void add(int i,int delta) {',
      '  for (; i<=n; i+=i&-i)',
      '    bit[i] += delta;',
      '}',
      'int prefix(int i) {',
      '  int result = 0;',
      '  for (; i>0; i-=i&-i)',
      '    result += bit[i];',
      '  return result;',
      '}',
    ], s.mode === 'update' ? 2 : s.i ? 7 : 8);
  }, false);
})();
