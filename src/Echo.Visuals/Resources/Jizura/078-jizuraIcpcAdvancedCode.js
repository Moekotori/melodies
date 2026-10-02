/* Original C++ core examples, shared by the four advanced algorithm studies.
   Named execution markers are stripped once and never shown as source annotations. */
(() => {
  'use strict';
  const program = source => {
    const at = {}, lines = source.trim().split('\n').map((line, index) => {
      const marker = line.match(/\s*\/\/ @([a-z]+)/);
      if (marker) at[marker[1]] = index;
      return line.replace(/\s*\/\/ @[a-z]+/, '');
    });
    return { lines, at };
  };
  J.echo.icpcPrograms = {
    tarjan: program(`
vector<int> g[N], st;
int dfn[N], low[N], comp[N];
bool on_stack[N];
int timer = 0, components = 0;

void tarjan(int u) {
  dfn[u] = low[u] = ++timer; // @discover
  st.push_back(u);
  on_stack[u] = true;
  for (int v : g[u]) {
    if (!dfn[v]) { // @descend
      tarjan(v);
      low[u] = min(low[u], low[v]); // @return
    } else if (on_stack[v]) {
      low[u] = min(low[u], dfn[v]); // @back
    }
  }
  if (dfn[u] != low[u]) return;
  // u is the root of one SCC.
  int v;
  do {
    v = st.back(); st.pop_back(); // @pop
    on_stack[v] = false;
    comp[v] = components;
  } while (v != u);
  ++components; // @component
}

void solve_scc(int n) {
  for (int u = 0; u < n; ++u)
    if (!dfn[u]) tarjan(u); // @done
}
`),
    dinic: program(`
struct Edge { int to, rev, cap; };
vector<Edge> g[N];
int level[N], cur[N];

void add_edge(int u,int v,int c) {
  int a=g[u].size(), b=g[v].size();
  g[u].push_back({v,b,c});
  g[v].push_back({u,a,0});
}

bool bfs(int s,int t) {
  fill(level, level+n, -1);
  queue<int> q; q.push(s);
  level[s] = 0; // @layer
  while (!q.empty()) {
    int u=q.front(); q.pop();
    for (auto e : g[u]) {
      if (!e.cap || level[e.to]>=0)
        continue;
      level[e.to] = level[u]+1; // @bfs
      q.push(e.to);
    }
  }
  return level[t] >= 0;
}

int dfs(int u,int t,int pushed) {
  if (u==t) return pushed;
  for (int &i=cur[u]; i<g[u].size();
       ++i) {
    auto &e = g[u][i]; // @scan
    if (!e.cap ||
        level[e.to]!=level[u]+1)
      continue;
    int f=dfs(e.to,t,
              min(pushed,e.cap));
    if (!f) continue;
    e.cap -= f; // @push
    g[e.to][e.rev].cap += f;
    return f;
  }
  return 0;
}

long long max_flow(int s,int t) {
  long long answer=0;
  while (bfs(s,t)) {
    fill(cur,cur+n,0);
    while (int f=dfs(s,t,INT_MAX))
      answer += f; // @augment
  }
  return answer; // @done
}
`),
    ac: program(`
int go[N][26], fail[N], out[N];
int nodes = 1;

void insert(string word) {
  int u=0;
  for (char ch : word) {
    int c=ch-'A';
    if (!go[u][c])
      go[u][c]=nodes++;
    u=go[u][c];
  }
  ++out[u];
}

void build() {
  queue<int> q;
  for (int c=0;c<26;++c)
    if (go[0][c]) q.push(go[0][c]);
  while (!q.empty()) {
    int u=q.front(); q.pop();
    out[u] += out[fail[u]]; // @inherit
    for (int c=0;c<26;++c) {
      int v=go[u][c];
      if (v) {
        fail[v]=go[fail[u]][c]; // @link
        q.push(v);
      } else {
        go[u][c]=go[fail[u]][c]; // @fallback
      }
    }
  }
}

long long search(string text) {
  int u=0; long long matches=0;
  for (char ch : text) {
    u=go[u][ch-'A']; // @scan
    matches += out[u]; // @match
  }
  return matches; // @done
}
`),
    ntt: program(`
const long long P=998244353, G=3;
using ll = long long;

ll power(ll a,ll k) {
  ll r=1;
  for (;k;k>>=1,a=a*a%P)
    if (k&1) r=r*a%P;
  return r;
}

void ntt(vector<ll>& a,bool inverse) {
  int n=a.size();
  for (int i=1,j=0;i<n;++i) {
    int bit=n>>1;
    for (;j&bit;bit>>=1) j^=bit;
    j^=bit;
    if (i<j) swap(a[i],a[j]); // @reverse
  }
  for (int len=2;len<=n;len<<=1) {
    ll root=power(G,(P-1)/len);
    if (inverse) root=power(root,P-2);
    for (int i=0;i<n;i+=len) {
      ll w=1;
      for (int j=0;j<len/2;++j) {
        ll u=a[i+j];
        ll v=a[i+j+len/2]*w%P;
        a[i+j]=(u+v)%P; // @butterfly
        a[i+j+len/2]=(u-v+P)%P;
        w=w*root%P;
      }
    }
  }
  if (inverse) {
    ll inv_n=power(n,P-2);
    for (ll &x:a) x=x*inv_n%P; // @normalize
  }
}

vector<ll> multiply(vector<ll> a,
                    vector<ll> b) {
  if (a.empty() || b.empty()) return {};
  int need=a.size()+b.size()-1, n=1;
  while (n<need) n<<=1;
  a.resize(n); b.resize(n);
  ntt(a,false); ntt(b,false);
  for (int i=0;i<n;++i)
    a[i]=a[i]*b[i]%P; // @product
  ntt(a,true);
  a.resize(need);
  return a; // @done
}
`),
  };
})();
