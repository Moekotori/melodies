const encodePath = (path: string): string =>
  path.replace(/\\/gu, '/').replace(/^\/+/u, '').split('/').map(encodeURIComponent).join('/');

/**
 * Scene files resolved by the host: the scene's own package or folder first,
 * then Wallpaper Engine's installed assets. Everything stays under the page root.
 */
export class SceneAssets {
  private readonly jsonCache = new Map<string, Promise<unknown | null>>();
  private readonly textCache = new Map<string, Promise<string | null>>();
  private textBytes = 0;

  constructor(readonly root: string) {}

  fileUrl(path: string): string {
    return `${this.root}file/${encodePath(path)}`;
  }

  videoUrl(path: string): string {
    return `${this.root}video/${encodePath(path)}`;
  }

  async bytes(path: string, range?: { start: number; length: number }): Promise<Uint8Array<ArrayBuffer> | null> {
    const response = await fetch(this.fileUrl(path), range
      ? { headers: { Range: `bytes=${range.start}-${range.start + range.length - 1}` } }
      : undefined);
    if (!response.ok) return null;
    return new Uint8Array(await response.arrayBuffer());
  }

  /** Total size of a file, read from a one-byte range request. */
  async size(path: string): Promise<number | null> {
    const response = await fetch(this.fileUrl(path), { headers: { Range: 'bytes=0-0' } });
    if (!response.ok) return null;
    await response.arrayBuffer();
    const total = /\/(\d+)$/u.exec(response.headers.get('Content-Range') ?? '');
    return total ? Number(total[1]) : Number(response.headers.get('Content-Length') ?? 0);
  }

  text(path: string): Promise<string | null> {
    let cached = this.textCache.get(path);
    if (!cached) {
      if (this.textCache.size >= 1024) throw new Error('scene-asset-count-budget');
      cached = fetch(this.fileUrl(path)).then((response) => {
        if (!response.ok) return null;
        const bytes = Number(response.headers.get('Content-Length'));
        if (!Number.isFinite(bytes) || bytes < 0 || bytes > 4 * 1024 * 1024 || this.textBytes + bytes > 16 * 1024 * 1024)
          throw new Error('scene-text-budget');
        this.textBytes += bytes;
        return response.text();
      });
      this.textCache.set(path, cached);
    }
    return cached;
  }

  json<T = unknown>(path: string): Promise<T | null> {
    let cached = this.jsonCache.get(path);
    if (!cached) {
      // Wallpaper Engine accepts `//` comment lines in its JSON files; strings are kept intact.
      cached = this.text(path).then((text) => (text === null ? null : JSON.parse(text
        .replace(/^\s*[^\s{[]*(?=[{[])/u, '')
        .replace(/("(?:[^"\\\n]|\\.)*")|\/\/[^\n]*/gu, (_match, quoted: string | undefined) => quoted ?? ''))));
      this.jsonCache.set(path, cached);
    }
    return cached as Promise<T | null>;
  }
}
