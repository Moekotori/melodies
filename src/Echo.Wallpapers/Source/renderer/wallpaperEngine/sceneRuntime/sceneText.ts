import type { SceneAssets } from './sceneAssets';
import type { SceneTexture } from './sceneTextures';

export type TextStyle = {
  font: string | null;
  pointSize: number;
  color: number[];
  horizontalAlign: string;
  verticalAlign: string;
  padding: number;
  width: number;
  height: number;
  background: number[] | null;
};

/** Points to scene pixels at 300 dpi: saved text boxes match glyph sizes at that density. */
const pointsToPixels = 300 / 72;
const maxTextureEdge = 2048;

/** Renders text objects into textures; fonts come from the scene package. */
export class SceneTextRenderer {
  private readonly families = new Map<string, Promise<string>>();
  private familyCounter = 0;

  constructor(private readonly gl: WebGL2RenderingContext, private readonly assets: SceneAssets) {}

  private family(path: string | null): Promise<string> {
    if (!path || path.startsWith('systemfont')) return Promise.resolve('sans-serif');
    let cached = this.families.get(path);
    if (!cached) {
      cached = (async () => {
        const bytes = await this.assets.bytes(path);
        if (!bytes) return 'sans-serif';
        const name = `scene-font-${this.familyCounter++}`;
        const face = new FontFace(name, bytes);
        await face.load();
        document.fonts.add(face);
        return `"${name}", sans-serif`;
      })().catch(() => 'sans-serif');
      this.families.set(path, cached);
    }
    return cached;
  }

  /** Draws `text` at `pixelScale` texels per scene unit into `target`, replacing its contents. */
  async render(text: string, style: TextStyle, pixelScale: number, target: SceneTexture | null): Promise<SceneTexture> {
    const { gl } = this;
    const family = await this.family(style.font);
    const scale = Math.min(pixelScale, maxTextureEdge / Math.max(1, style.width, style.height));
    const width = Math.max(1, Math.round(style.width * scale));
    const height = Math.max(1, Math.round(style.height * scale));
    const canvas = new OffscreenCanvas(width, height);
    const context = canvas.getContext('2d')!;
    if (style.background) {
      context.fillStyle = `rgba(${style.background.map((value) => Math.round(value * 255)).join(',')}, 1)`;
      context.fillRect(0, 0, width, height);
    }
    const fontSize = style.pointSize * pointsToPixels * scale;
    context.font = `${fontSize}px ${family}`;
    context.fillStyle = `rgb(${style.color.slice(0, 3).map((value) => Math.round(Math.min(1, Math.max(0, value)) * 255)).join(',')})`;
    context.textBaseline = 'alphabetic';
    const padding = style.padding * scale;
    const lines = text.split(/\r?\n/u);
    const lineHeight = fontSize * 1.2;
    const blockHeight = lineHeight * lines.length;
    let y = style.verticalAlign === 'top' ? padding
      : style.verticalAlign === 'bottom' ? height - padding - blockHeight
        : (height - blockHeight) / 2;
    for (const line of lines) {
      const metrics = context.measureText(line);
      const x = style.horizontalAlign === 'left' ? padding
        : style.horizontalAlign === 'right' ? width - padding - metrics.width
          : (width - metrics.width) / 2;
      context.fillText(line, x, y + (fontSize * 0.95));
      y += lineHeight;
    }

    const texture = target?.texture ?? gl.createTexture()!;
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, canvas);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return { texture, width, height, imageWidth: width, imageHeight: height, frames: [], video: null };
  }
}
