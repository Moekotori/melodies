import type { SceneAssets } from './sceneAssets';
import { bindRenderTarget, createRenderTarget, type GlProgram, type RenderTarget } from './sceneGl';
import {
  bindMaterialPass,
  type FrameState,
  type LiveValue,
  type MaterialPass,
  type ResolvedTexture,
  type SceneMaterialCompiler,
  type TextureBinding,
} from './sceneMaterials';
import type { ScriptLayerTarget } from './sceneScripts';
import { textureResolution, type SceneTexture } from './sceneTextures';
import { isRecord, toBoolean } from './sceneValues';

type DrawPass = { kind: 'draw'; pass: MaterialPass; target: string | null };
type CopyPass = { kind: 'copy'; source: TextureBinding; target: string };

export type LayerEffect = {
  name: string;
  /** Material values by key, for scripts' `getEffect(name).getMaterial()`. */
  material: Map<string, LiveValue>;
  isVisible: () => boolean;
  setVisible: (visible: boolean) => void;
  passes: Array<DrawPass | CopyPass>;
  fbos: Array<{ name: string; scale: number }>;
  usesFullFrame: boolean;
};

export type EffectTargets = { ping: RenderTarget; pong: RenderTarget; named: Map<string, RenderTarget> };

const identity3 = new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]);

/** Loads `effects/*\/effect.json` chains: material passes, named FBOs, binds and copies. */
export class SceneEffectLoader {
  constructor(private readonly assets: SceneAssets, private readonly materials: SceneMaterialCompiler) {}

  async load(instance: Record<string, unknown>, layer: ScriptLayerTarget | null): Promise<LayerEffect | null> {
    if (typeof instance.file !== 'string') return null;
    const effect = await this.assets.json<Record<string, unknown>>(instance.file);
    if (!effect || !Array.isArray(effect.passes)) return null;
    const instancePasses = Array.isArray(instance.passes) ? instance.passes.filter(isRecord) : [];
    const fbos = (Array.isArray(effect.fbos) ? effect.fbos.filter(isRecord) : [])
      .filter((fbo) => typeof fbo.name === 'string')
      .map((fbo) => ({ name: fbo.name as string, scale: Math.max(1, Number(fbo.scale) || 1) }));
    const fboNames = new Set(fbos.map((fbo) => fbo.name));
    const passes: LayerEffect['passes'] = [];
    const material = new Map<string, LiveValue>();
    let usesFullFrame = false;

    const bindingFor = async (name: unknown): Promise<TextureBinding | null> => {
      if (name === 'previous') return { kind: 'previous' };
      if (name === '_rt_FullFrameBuffer') { usesFullFrame = true; return { kind: 'fullframe' }; }
      if (typeof name === 'string' && fboNames.has(name)) return { kind: 'fbo', name };
      return this.materials.bindTexture(name);
    };

    for (const [index, pass] of effect.passes.entries()) {
      if (!isRecord(pass)) continue;
      if (pass.command === 'copy' && typeof pass.target === 'string') {
        const source = await bindingFor(pass.source ?? 'previous');
        if (source) passes.push({ kind: 'copy', source, target: pass.target });
        continue;
      }
      if (typeof pass.material !== 'string') continue;
      const materialJson = await this.assets.json<Record<string, unknown>>(pass.material);
      const materialPass = isRecord(materialJson) && Array.isArray(materialJson.passes) && isRecord(materialJson.passes[0]) ? materialJson.passes[0] : null;
      if (!materialPass) continue;
      const compiled = await this.materials.compile(
        materialPass,
        instancePasses[index] ?? {},
        bindingFor,
        layer,
        material,
        Array.isArray(pass.bind) ? pass.bind.filter(isRecord) : [],
        { kind: 'previous' },
      );
      if (compiled) passes.push({ kind: 'draw', pass: compiled, target: typeof pass.target === 'string' ? pass.target : null });
    }
    if (passes.length === 0) return null;

    const visible = this.materials.liveValue(instance.visible ?? true, layer);
    let override: boolean | null = null;
    return {
      name: typeof instance.name === 'string' && instance.name ? instance.name : String(effect.name ?? instance.file),
      material,
      isVisible: () => override ?? toBoolean(visible.read(), true),
      setVisible: (value) => { override = value; },
      passes,
      fbos,
      usesFullFrame,
    };
  }
}

/** Runs a layer's effect chain; returns the target holding the final image. */
export const applyEffects = (
  gl: WebGL2RenderingContext,
  quad: WebGLVertexArrayObject,
  effects: LayerEffect[],
  targets: EffectTargets,
  frame: FrameState,
  fullFrame: RenderTarget | null,
  copyProgram: GlProgram,
  /** Called for every texture a visible pass samples (keeps video textures playing). */
  onTexture: (texture: SceneTexture) => void,
): RenderTarget => {
  let current = targets.ping;
  const namedTarget = (name: string, scale: number): RenderTarget => {
    let target = targets.named.get(name);
    const width = Math.max(1, Math.round(targets.ping.width / scale));
    const height = Math.max(1, Math.round(targets.ping.height / scale));
    if (!target || target.width !== width || target.height !== height) {
      target = createRenderTarget(gl, width, height);
      targets.named.set(name, target);
    }
    return target;
  };
  gl.bindVertexArray(quad);
  gl.disable(gl.BLEND);

  for (const effect of effects) {
    if (!effect.isVisible()) continue;
    const scales = new Map(effect.fbos.map((fbo) => [fbo.name, fbo.scale]));
    const resolve = (binding: TextureBinding | null, previous: RenderTarget): ResolvedTexture | null => {
      if (!binding) return null;
      switch (binding.kind) {
        case 'previous': return { texture: previous.texture, resolution: [previous.width, previous.height, previous.width, previous.height] };
        case 'fullframe': return fullFrame ? { texture: fullFrame.texture, resolution: [fullFrame.width, fullFrame.height, fullFrame.width, fullFrame.height] } : null;
        case 'fbo': {
          const target = namedTarget(binding.name, scales.get(binding.name) ?? 1);
          return { texture: target.texture, resolution: [target.width, target.height, target.width, target.height] };
        }
        case 'texture':
          onTexture(binding.texture);
          return { texture: binding.texture.texture, resolution: textureResolution(binding.texture) };
        default: return null;
      }
    };

    for (const pass of effect.passes) {
      if (pass.kind === 'copy') {
        const source = resolve(pass.source, current);
        const target = namedTarget(pass.target, scales.get(pass.target) ?? 1);
        if (!source) continue;
        bindRenderTarget(gl, target);
        gl.useProgram(copyProgram.program);
        gl.activeTexture(gl.TEXTURE0);
        gl.bindTexture(gl.TEXTURE_2D, source.texture);
        const uniforms = copyProgram.uniforms;
        gl.uniformMatrix3fv(uniforms.get('u_Matrix')!.location, false, identity3);
        gl.uniform4f(uniforms.get('u_UvRect')!.location, 0, 0, 1, 1);
        gl.uniform4f(uniforms.get('u_Color')!.location, 1, 1, 1, 1);
        gl.uniform1i(uniforms.get('u_Texture')!.location, 0);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
        continue;
      }
      const output = pass.target
        ? namedTarget(pass.target, scales.get(pass.target) ?? 1)
        : current === targets.ping ? targets.pong : targets.ping;
      bindRenderTarget(gl, output);
      const input = current;
      bindMaterialPass(gl, pass.pass, frame, output, (binding) => resolve(binding, input), null);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      if (!pass.target) current = output;
    }
  }
  return current;
};
