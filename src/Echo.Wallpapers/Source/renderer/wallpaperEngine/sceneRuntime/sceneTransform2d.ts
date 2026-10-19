import { identity3, multiply3, type Mat3 } from './sceneGl';

/** One layer's local/world matrices, refreshed only when its inputs or parent change. */
export class SceneTransform2d {
  private readonly local = identity3();
  private x = Number.NaN;
  private y = Number.NaN;
  private angle = Number.NaN;
  private scaleX = Number.NaN;
  private scaleY = Number.NaN;
  private parent: SceneTransform2d | null = null;
  private parentRevision = -1;
  revision = 0;

  constructor(readonly world: Mat3 = identity3()) {}

  update(x: number, y: number, angle: number, scaleX: number, scaleY: number, parent: SceneTransform2d | null): void {
    const changed = x !== this.x || y !== this.y || angle !== this.angle || scaleX !== this.scaleX || scaleY !== this.scaleY;
    if (changed) {
      this.x = x; this.y = y; this.angle = angle; this.scaleX = scaleX; this.scaleY = scaleY;
      // Preserve the float32 rounding of translate * rotate * scale without
      // constructing those intermediate matrices on every update.
      const cos = Math.fround(Math.cos(angle));
      const sin = Math.fround(Math.sin(angle));
      const sx = Math.fround(scaleX); const sy = Math.fround(scaleY);
      this.local[0] = cos * sx; this.local[1] = sin * sx;
      this.local[3] = -sin * sy; this.local[4] = cos * sy;
      this.local[6] = x; this.local[7] = y;
    }
    const parentRevision = parent?.revision ?? 0;
    if (!changed && parent === this.parent && parentRevision === this.parentRevision) return;
    if (parent) multiply3(parent.world, this.local, this.world);
    else this.world.set(this.local);
    this.parent = parent;
    this.parentRevision = parentRevision;
    this.revision += 1;
  }
}
