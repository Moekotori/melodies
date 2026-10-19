import type { SceneAssets } from './sceneAssets';
import { SceneCamera, type CameraFrame } from './sceneCamera';
import { applyBlend, bindRenderTarget, createRenderTarget, deleteRenderTarget, type RenderTarget } from './sceneGl';
import { bindMaterialPass, type FrameState, type MaterialPass, type SceneLights, type SceneMaterialCompiler } from './sceneMaterials';
import { identity4, multiply4, type Mat4 } from './sceneMath3d';
import { SceneModelRenderer, type SceneModel } from './sceneModels';
import type { ParticleRenderer, ParticleSystem } from './sceneParticles';
import { textureResolution, type SceneTexture } from './sceneTextures';
import { toVector } from './sceneValues';

/** Point light read from a scene object; position comes from the object's world transform. */
export type LightSource = { color: number[]; intensity: number; radius: number };

const maxLights = 4;
/** Reflections are blurred by the floor's bump anyway; half resolution keeps the extra pass cheap. */
const reflectionScale = 0.5;
const mirrorY = new Float32Array([1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

/**
 * Perspective half of the runtime: camera, lights and the draws that need a
 * view-projection (models, full-screen material backdrops, 3D particles).
 */
export class Scene3d {
  private cameraFrame: CameraFrame | null = null;
  private lights: SceneLights;
  /** Mirror image of `reflected` objects about the ground plane (y = 0); only when a material samples it. */
  private reflection: RenderTarget | null = null;

  private constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly camera: SceneCamera,
    readonly models: SceneModelRenderer,
    general: Record<string, unknown>,
  ) {
    this.lights = {
      position: new Float32Array(maxLights * 3),
      colorRadius: new Float32Array(maxLights * 4),
      ambient: toVector(general.ambientcolor, [0.3, 0.3, 0.3]),
      skylight: toVector(general.skylightcolor, [0.3, 0.3, 0.3]),
    };
  }

  static async create(
    gl: WebGL2RenderingContext,
    assets: SceneAssets,
    materials: SceneMaterialCompiler,
    scene: Record<string, unknown>,
    general: Record<string, unknown>,
  ): Promise<Scene3d> {
    return new Scene3d(gl, await SceneCamera.load(scene, general, assets), new SceneModelRenderer(gl, assets, materials), general);
  }

  /** Camera pose and light uniforms for this frame; `parallax` shifts the eye (see `SceneCamera.frame`). */
  begin(seconds: number, aspect: number, lights: Array<{ source: LightSource; world: Mat4 }>, parallax: [number, number]): CameraFrame {
    this.cameraFrame = this.camera.frame(seconds, aspect, parallax);
    this.lights.position.fill(0);
    this.lights.colorRadius.fill(0);
    lights.slice(0, maxLights).forEach(({ source, world }, index) => {
      this.lights.position.set([world[12], world[13], world[14]], index * 3);
      this.lights.colorRadius.set([
        source.color[0] * source.intensity, source.color[1] * source.intensity, source.color[2] * source.intensity, source.radius,
      ], index * 4);
    });
    return this.cameraFrame;
  }

  /** Distance from the camera to an object's origin, for back-to-front sorting. */
  distance(world: Mat4): number {
    const eye = this.cameraFrame?.eye ?? [0, 0, 0];
    return Math.hypot(world[12] - eye[0], world[13] - eye[1], world[14] - eye[2]);
  }

  get fade(): number {
    return this.cameraFrame?.fade ?? 1;
  }

  /** Sizes the reflection target to the scene target; called on resize when the scene uses reflections. */
  resizeReflection(width: number, height: number): void {
    const w = Math.max(1, Math.round(width * reflectionScale));
    const h = Math.max(1, Math.round(height * reflectionScale));
    if (this.reflection?.width === w && this.reflection.height === h) return;
    if (this.reflection) deleteRenderTarget(this.gl, this.reflection);
    this.reflection = createRenderTarget(this.gl, w, h, true);
  }

  /** Renders the mirrored `reflected` models; the floor samples it at its own screen position. */
  drawReflection(models: Array<{ model: SceneModel; world: Mat4 }>, clearColor: number[], frame: FrameState, onTexture: (texture: SceneTexture) => void): void {
    const { gl, reflection, cameraFrame } = this;
    if (!reflection || !cameraFrame) return;
    bindRenderTarget(gl, reflection);
    gl.clearColor(clearColor[0], clearColor[1], clearColor[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    for (const { model, world } of models) {
      this.models.draw(model, multiply4(mirrorY, world), cameraFrame, this.lights, frame, reflection, onTexture, { reflection: null, mirrored: true });
    }
  }

  drawModel(model: SceneModel, world: Mat4, frame: FrameState, target: { width: number; height: number }, onTexture: (texture: SceneTexture) => void): void {
    if (this.cameraFrame) this.models.draw(model, world, this.cameraFrame, this.lights, frame, target, onTexture, { reflection: this.reflection, mirrored: false });
  }

  /** A `fullscreen` model with its own material (sky domes, gradients): drawn behind everything after it. */
  drawBackdrop(passes: MaterialPass[], quad: WebGLVertexArrayObject, frame: FrameState, target: { width: number; height: number }, onTexture: (texture: SceneTexture) => void): void {
    const { gl } = this;
    gl.bindVertexArray(quad);
    gl.disable(gl.DEPTH_TEST);
    for (const pass of passes) {
      bindMaterialPass(gl, pass, frame, target, (binding) => {
        if (binding?.kind !== 'texture') return null;
        onTexture(binding.texture);
        return { texture: binding.texture.texture, resolution: textureResolution(binding.texture) };
      }, this.cameraFrame ? {
        model: identity4(),
        normal: new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]),
        view: this.cameraFrame.view,
        projection: this.cameraFrame.projection,
        viewProjection: this.cameraFrame.viewProjection,
        modelViewProjection: identity4(),
        eye: this.cameraFrame.eye,
        up: this.cameraFrame.up,
        right: this.cameraFrame.right,
        lights: this.lights,
      } : null);
      applyBlend(gl, pass.blend);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }
  }

  drawParticles(renderer: ParticleRenderer, system: ParticleSystem, world: Mat4, opacity: number): void {
    if (this.cameraFrame) renderer.draw3d(system, world, this.cameraFrame, opacity);
  }
}
