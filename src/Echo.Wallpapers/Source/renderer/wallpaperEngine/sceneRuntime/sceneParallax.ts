/** Scene `general` camera parallax settings. */
export type ParallaxSettings = { enabled: boolean; amount: number; delay: number; mouseInfluence: number };

/**
 * Layer travel at full displacement, as a fraction of the scene width per unit
 * of `parallaxDepth`, and the 3D camera's travel relative to its look distance.
 */
const layerTravel = 0.05;
const cameraTravel = 0.1;

/**
 * Cursor position forwarded by the host page (canvas-normalised, y down) and
 * Wallpaper Engine's camera parallax derived from it, eased over `delay` seconds.
 */
export class SceneParallax {
  /** Latest cursor, y down; `null` until the host sends one. */
  private cursor: [number, number] | null = null;
  private eased: [number, number] = [0.5, 0.5];

  constructor(readonly settings: ParallaxSettings) {}

  setCursor(x: number, y: number): void {
    this.cursor = [x, y];
  }

  /** `g_PointerPosition`: screen space with y up, as Wallpaper Engine's shaders expect. */
  get pointer(): [number, number] {
    return this.cursor ? [this.cursor[0], 1 - this.cursor[1]] : [0.5, 0.5];
  }

  update(deltaSeconds: number): void {
    const target = this.cursor ?? [0.5, 0.5];
    const delay = this.settings.delay;
    const step = delay > 0 ? 1 - Math.exp(-deltaSeconds / delay) : 1;
    this.eased = [this.eased[0] + ((target[0] - this.eased[0]) * step), this.eased[1] + ((target[1] - this.eased[1]) * step)];
  }

  /** Eased cursor offset from the centre in [-1, 1], y up, scaled by amount and mouse influence. */
  get displacement(): [number, number] {
    const { enabled, amount, mouseInfluence } = this.settings;
    if (!enabled) return [0, 0];
    const scale = amount * mouseInfluence;
    return [((this.eased[0] * 2) - 1) * scale, (1 - (this.eased[1] * 2)) * scale];
  }

  /** 2D: a root layer's offset in scene units; layers shift against the cursor, deeper ones more. */
  layerOffset(depth: number[], sceneWidth: number): [number, number] {
    const [x, y] = this.displacement;
    return [-x * depth[0] * layerTravel * sceneWidth, -y * depth[1] * layerTravel * sceneWidth];
  }

  /** 3D: camera shift along its right and up axes, as a fraction of the eye–target distance. */
  get cameraOffset(): [number, number] {
    const [x, y] = this.displacement;
    return [x * cameraTravel, y * cameraTravel];
  }
}
