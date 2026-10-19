import type { SceneAssets } from './sceneAssets';
import type { SceneAudioSpectrum } from './sceneAudioSpectrum';
import { blendModeFor, compileProgram, type BlendMode, type GlProgram } from './sceneGl';
import type { Mat4, Vec3 } from './sceneMath3d';
import type { SceneScriptHost, ScriptLayerTarget } from './sceneScripts';
import type { SceneTexture, SceneTextureCache } from './sceneTextures';
import { isRecord, readPropertySource, resolvePropertyValue, toVector, type UserPropertyMap } from './sceneValues';
import {
  inlineShaderIncludes,
  listShaderIncludes,
  parseShaderCombos,
  parseShaderUniforms,
  reconcileShaderStages,
  translateWeShader,
  type ShaderUniform,
} from './shaderTranslator';

export type TextureBinding =
  | { kind: 'previous' }
  | { kind: 'fbo'; name: string }
  | { kind: 'fullframe' }
  /** A 3D scene's planar reflection of its `reflected` objects. */
  | { kind: 'reflection' }
  | { kind: 'texture'; texture: SceneTexture };

/** A property that may be user-bound, scripted, or set later through `getMaterial()`. */
export type LiveValue = { read: () => unknown; write: (value: unknown) => void };

export type MaterialValue = { name: string; key: string; value: LiveValue };

/** Per-frame values every Wallpaper Engine shader may read. */
export type FrameState = {
  time: number;
  daytime: number;
  /** 64 left + 64 right bins in 0..1. */
  spectrum: SceneAudioSpectrum;
  screenWidth: number;
  screenHeight: number;
  /** Cursor in screen space, y up (`g_PointerPosition`). */
  pointer: number[];
  /** Eased camera parallax position, centre 0.5 (`g_ParallaxPosition`). */
  parallax: number[];
};

/** Up to four point lights, packed as the engine uniforms expect. */
export type SceneLights = {
  position: Float32Array;
  colorRadius: Float32Array;
  ambient: number[];
  skylight: number[];
};

/** Per-draw transforms and lighting of a 3D draw. */
export type DrawContext = {
  model: Mat4;
  normal: Float32Array;
  view: Mat4;
  projection: Mat4;
  viewProjection: Mat4;
  modelViewProjection: Mat4;
  eye: Vec3;
  up: Vec3;
  right: Vec3;
  lights: SceneLights;
};

/** A compiled material pass: program plus its textures, values and raster state. */
export type MaterialPass = {
  program: GlProgram;
  textures: Array<TextureBinding | null>;
  values: MaterialValue[];
  blend: BlendMode;
  depthTest: boolean;
  depthWrite: boolean;
  cull: 'none' | 'back';
};

const identity4 = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
const identity3 = new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]);

export const componentCount = (gl: WebGL2RenderingContext, type: number): number => {
  switch (type) {
    case gl.FLOAT_VEC2: case gl.INT_VEC2: case gl.BOOL_VEC2: return 2;
    case gl.FLOAT_VEC3: case gl.INT_VEC3: case gl.BOOL_VEC3: return 3;
    case gl.FLOAT_VEC4: case gl.INT_VEC4: case gl.BOOL_VEC4: return 4;
    case gl.FLOAT_MAT3: return 9;
    case gl.FLOAT_MAT4: return 16;
    default: return 1;
  }
};

export const setUniform = (gl: WebGL2RenderingContext, uniform: { location: WebGLUniformLocation; type: number; size: number }, values: ArrayLike<number>): void => {
  if ((uniform.type === gl.FLOAT && uniform.size === 1) || uniform.type === gl.INT || uniform.type === gl.BOOL) {
    const value = Math.fround(values[0] ?? 0);
    if (uniform.type === gl.FLOAT) gl.uniform1f(uniform.location, value);
    else gl.uniform1i(uniform.location, Math.round(value));
    return;
  }
  const data = values instanceof Float32Array ? values : Float32Array.from(values);
  switch (uniform.type) {
    case gl.FLOAT: uniform.size > 1 ? gl.uniform1fv(uniform.location, data) : gl.uniform1f(uniform.location, data[0] ?? 0); break;
    case gl.FLOAT_VEC2: gl.uniform2fv(uniform.location, data); break;
    case gl.FLOAT_VEC3: gl.uniform3fv(uniform.location, data); break;
    case gl.FLOAT_VEC4: gl.uniform4fv(uniform.location, data); break;
    case gl.INT: case gl.BOOL: gl.uniform1i(uniform.location, Math.round(data[0] ?? 0)); break;
    case gl.INT_VEC2: case gl.BOOL_VEC2: gl.uniform2iv(uniform.location, Int32Array.from(data, Math.round)); break;
    case gl.INT_VEC3: case gl.BOOL_VEC3: gl.uniform3iv(uniform.location, Int32Array.from(data, Math.round)); break;
    case gl.INT_VEC4: case gl.BOOL_VEC4: gl.uniform4iv(uniform.location, Int32Array.from(data, Math.round)); break;
    case gl.FLOAT_MAT3: gl.uniformMatrix3fv(uniform.location, false, data); break;
    case gl.FLOAT_MAT4: gl.uniformMatrix4fv(uniform.location, false, data); break;
    default: break;
  }
};

const upper3 = (m: Mat4): Float32Array => new Float32Array([m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10]]);

/** Values for Wallpaper Engine's engine-provided uniforms, by name and declared type. */
export const engineUniform = (
  gl: WebGL2RenderingContext,
  name: string,
  type: number,
  frame: FrameState,
  target: { width: number; height: number },
  draw: DrawContext | null,
): ArrayLike<number> | null => {
  const matrix = (value: Mat4): ArrayLike<number> => (type === gl.FLOAT_MAT3 ? upper3(value) : value);
  switch (name) {
    case 'g_Time': return [frame.time];
    case 'g_Daytime': return [frame.daytime];
    case 'g_PointerPosition':
    case 'g_PointerPositionLast': return frame.pointer;
    case 'g_ParallaxPosition': return frame.parallax;
    case 'g_TexelSize': return [1 / target.width, 1 / target.height];
    case 'g_TexelSizeHalf': return [0.5 / target.width, 0.5 / target.height];
    case 'g_Screen': return [frame.screenWidth, frame.screenHeight, frame.screenWidth / frame.screenHeight];
    case 'g_Alpha':
    case 'g_Brightness':
    case 'g_UserAlpha': return [1];
    case 'g_Color': return [1, 1, 1];
    case 'g_Color4': return [1, 1, 1, 1];
    case 'g_AudioSpectrum16Left': return frame.spectrum.uniform(16, false);
    case 'g_AudioSpectrum16Right': return frame.spectrum.uniform(16, true);
    case 'g_AudioSpectrum32Left': return frame.spectrum.uniform(32, false);
    case 'g_AudioSpectrum32Right': return frame.spectrum.uniform(32, true);
    case 'g_AudioSpectrum64Left': return frame.spectrum.uniform(64, false);
    case 'g_AudioSpectrum64Right': return frame.spectrum.uniform(64, true);
    default: break;
  }
  if (draw) {
    switch (name) {
      case 'g_ModelMatrix': return matrix(draw.model);
      case 'g_NormalModelMatrix': return draw.normal;
      case 'g_ViewMatrix': return matrix(draw.view);
      case 'g_ProjectionMatrix': return draw.projection;
      case 'g_ViewProjectionMatrix': return draw.viewProjection;
      case 'g_ModelViewProjectionMatrix': return draw.modelViewProjection;
      case 'g_EyePosition': return draw.eye;
      case 'g_ViewUp': return draw.up;
      case 'g_ViewRight': return draw.right;
      case 'g_LightsPosition': return draw.lights.position;
      case 'g_LightsColorRadius':
      case 'g_LightsColorPremultiplied': return draw.lights.colorRadius;
      case 'g_LightAmbientColor': return draw.lights.ambient;
      case 'g_LightSkylightColor': return draw.lights.skylight;
      default: break;
    }
  }
  if (/Matrix(Inverse)?$/u.test(name)) return type === gl.FLOAT_MAT3 ? identity3 : identity4;
  return null;
};

/** Fetches `#include` headers recursively from the scene, then Wallpaper Engine's assets. */
export const loadShaderHeaders = async (assets: SceneAssets, source: string, headers: Map<string, string>): Promise<void> => {
  for (const name of listShaderIncludes(source)) {
    if (headers.has(name)) continue;
    const header = await assets.text(`shaders/${name}`);
    headers.set(name, header ?? '');
    if (header) await loadShaderHeaders(assets, header, headers);
  }
};

type ShaderSources = { vertex: string; fragment: string; uniforms: ShaderUniform[]; combos: Record<string, number> };

export type BindingResolver = (name: unknown) => Promise<TextureBinding | null>;

/** Compiles material passes for effects, models and full-screen layers; programs are shared per shader variant. */
export class SceneMaterialCompiler {
  private readonly sources = new Map<string, Promise<ShaderSources | null>>();
  private readonly programs = new Map<string, GlProgram | null>();
  /** Set once a material samples `_rt_Reflection`; the scene then renders a reflection pass. */
  usesReflection = false;
  /** Set once a compiled shader reads the cursor; the host then forwards it. */
  usesPointer = false;
  usesAudio = false;

  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly assets: SceneAssets,
    readonly textures: SceneTextureCache,
    private readonly scripts: SceneScriptHost,
    private readonly userProperties: UserPropertyMap,
  ) {}

  private shaderSources(shader: string): Promise<ShaderSources | null> {
    let cached = this.sources.get(shader);
    if (!cached) {
      cached = (async () => {
        const [vertexRaw, fragmentRaw] = await Promise.all([this.assets.text(`shaders/${shader}.vert`), this.assets.text(`shaders/${shader}.frag`)]);
        if (vertexRaw === null || fragmentRaw === null) return null;
        const headers = new Map<string, string>();
        await loadShaderHeaders(this.assets, vertexRaw, headers);
        await loadShaderHeaders(this.assets, fragmentRaw, headers);
        const vertex = inlineShaderIncludes(vertexRaw, headers);
        const fragment = inlineShaderIncludes(fragmentRaw, headers);
        const combos: Record<string, number> = {};
        for (const combo of parseShaderCombos(`${vertex}\n${fragment}`)) combos[combo.combo] = combo.defaultValue;
        const uniforms = [...parseShaderUniforms(vertex), ...parseShaderUniforms(fragment)];
        return { vertex, fragment, uniforms, combos };
      })();
      this.sources.set(shader, cached);
    }
    return cached;
  }

  private program(shader: string, sources: ShaderSources, defines: Record<string, number>): GlProgram | null {
    const key = `${shader}|${Object.entries(defines).sort(([a], [b]) => a.localeCompare(b)).map(([name, value]) => `${name}=${value}`).join(',')}`;
    if (this.programs.has(key)) return this.programs.get(key)!;
    let program: GlProgram | null = null;
    try {
      const stages = reconcileShaderStages(sources.vertex, sources.fragment);
      program = compileProgram(this.gl, translateWeShader(stages.vertex, 'vertex', defines), translateWeShader(stages.fragment, 'fragment', defines));
      if (program.uniforms.has('g_PointerPosition') || program.uniforms.has('g_PointerPositionLast')) this.usesPointer = true;
      if ([...program.uniforms.keys()].some(name => /^g_AudioSpectrum(?:16|32|64)(?:Left|Right)$/u.test(name))) this.usesAudio = true;
    } catch (error) {
      console.warn(`[scene] shader ${shader} skipped`, error);
    }
    this.programs.set(key, program);
    return program;
  }

  /** A property value that may be user-bound or animated by a script. */
  liveValue(raw: unknown, layer: ScriptLayerTarget | null, components = 0): LiveValue {
    const source = readPropertySource(raw);
    const holder = { value: resolvePropertyValue(source, this.userProperties) };
    this.scripts.attach(source, layer, this.scripts.scriptValue(holder.value, components), (value) => { holder.value = value; });
    return { read: () => holder.value, write: (value) => { holder.value = value; } };
  }

  /** Texture names of models and full-screen layers: scene textures, with render targets stubbed. */
  readonly bindTexture: BindingResolver = async (name) => {
    if (typeof name !== 'string' || !name) return null;
    if (name === '_rt_Reflection') {
      this.usesReflection = true;
      return { kind: 'reflection' };
    }
    if (name.startsWith('_rt_')) return { kind: 'texture', texture: this.textures.white };
    const texture = await this.textures.load(name);
    return texture ? { kind: 'texture', texture } : null;
  };

  /**
   * One pass of a material file, with an effect instance's per-pass
   * overrides (`textures`, `combos`, `constantshadervalues`) and binds.
   */
  async compile(
    materialPass: Record<string, unknown>,
    instancePass: Record<string, unknown>,
    resolve: BindingResolver,
    layer: ScriptLayerTarget | null,
    material: Map<string, LiveValue>,
    binds: Array<Record<string, unknown>> = [],
    /** Effects read the previous image from slot 0 unless told otherwise. */
    firstSlot: TextureBinding | null = null,
  ): Promise<MaterialPass | null> {
    if (typeof materialPass.shader !== 'string') return null;
    const sources = await this.shaderSources(materialPass.shader);
    if (!sources) return null;

    const materialTextures = Array.isArray(materialPass.textures) ? materialPass.textures : [];
    const instanceTextures = Array.isArray(instancePass.textures) ? instancePass.textures : [];
    const slotNames: unknown[] = [];
    for (let slot = 0; slot < Math.max(materialTextures.length, instanceTextures.length, 1); slot += 1) {
      slotNames[slot] = instanceTextures[slot] ?? materialTextures[slot] ?? null;
    }
    const textures: Array<TextureBinding | null> = await Promise.all(slotNames.map((name) => resolve(name)));
    for (const bind of binds) {
      const slot = Number(bind.index);
      if (Number.isInteger(slot) && slot >= 0 && slot < 16) textures[slot] = await resolve(bind.name);
    }
    if (!textures[0] && firstSlot) textures[0] = firstSlot;

    const defines: Record<string, number> = { ...sources.combos };
    for (const uniform of sources.uniforms) {
      const slot = /^g_Texture(\d+)$/u.exec(uniform.name);
      if (!slot || uniform.type !== 'sampler2D') continue;
      const slotIndex = Number(slot[1]);
      if (!textures[slotIndex] && typeof uniform.annotation?.default === 'string') {
        textures[slotIndex] = await resolve(uniform.annotation.default);
      }
      if (typeof uniform.annotation?.combo === 'string') defines[uniform.annotation.combo] = textures[slotIndex] ? 1 : 0;
    }
    for (const combos of [materialPass.combos, instancePass.combos]) {
      if (!isRecord(combos)) continue;
      // Combo names are case-insensitive in material files; shaders test the upper-case macro.
      for (const [name, value] of Object.entries(combos)) defines[name.toUpperCase()] = Number(value) || 0;
    }
    const program = this.program(materialPass.shader, sources, defines);
    if (!program) return null;

    const constants = [materialPass.constantshadervalues, instancePass.constantshadervalues].filter(isRecord);
    // `usershadervalues` maps a wallpaper user property name to the material key it drives.
    const userBindings = [materialPass.usershadervalues, instancePass.usershadervalues].filter(isRecord);
    const values: MaterialValue[] = [];
    for (const uniform of sources.uniforms) {
      const key = uniform.annotation?.material;
      if (typeof key !== 'string' || !program.uniforms.has(uniform.name)) continue;
      const constant = constants.reduce<unknown>((value, table) => (key in table ? table[key] : value), uniform.annotation?.default);
      const user = userBindings.reduce<string | null>((name, table) => Object.keys(table).find((property) => table[property] === key) ?? name, null);
      const raw = typeof user === 'string' ? { ...(isRecord(constant) && 'value' in constant ? constant : { value: constant }), user } : constant;
      const value = material.get(key) ?? this.liveValue(raw, layer, ({ vec2: 2, vec3: 3, vec4: 4 } as Record<string, number>)[uniform.type] ?? 0);
      material.set(key, value);
      values.push({ name: uniform.name, key, value });
    }
    return {
      program,
      textures,
      values,
      blend: blendModeFor(materialPass.blending),
      depthTest: materialPass.depthtest !== 'disabled',
      depthWrite: materialPass.depthwrite !== 'disabled',
      cull: materialPass.cullmode === 'nocull' ? 'none' : 'back',
    };
  }

  /** Every pass of a material file, for models and full-screen layers. */
  async loadMaterial(path: string, layer: ScriptLayerTarget | null): Promise<MaterialPass[]> {
    const json = await this.assets.json<Record<string, unknown>>(path);
    const passes = isRecord(json) && Array.isArray(json.passes) ? json.passes.filter(isRecord) : [];
    const material = new Map<string, LiveValue>();
    const compiled = await Promise.all(passes.map(async (pass) => {
      const result = await this.compile(pass, {}, this.bindTexture, layer, material);
      // For models, `normal` (and no value) writes the shader's colour without blending.
      return result && (pass.blending === undefined || pass.blending === 'normal') ? { ...result, blend: 'opaque' as const } : result;
    }));
    return compiled.filter((pass): pass is MaterialPass => pass !== null);
  }
}

export type ResolvedTexture = { texture: WebGLTexture; resolution: number[] };

/** Uses a pass's program and sets its textures, engine uniforms and material values. */
export const bindMaterialPass = (
  gl: WebGL2RenderingContext,
  pass: MaterialPass,
  frame: FrameState,
  target: { width: number; height: number },
  resolve: (binding: TextureBinding | null) => ResolvedTexture | null,
  draw: DrawContext | null,
): void => {
  gl.useProgram(pass.program.program);
  for (const [slot, binding] of pass.textures.entries()) {
    const resolved = resolve(binding);
    gl.activeTexture(gl.TEXTURE0 + slot);
    gl.bindTexture(gl.TEXTURE_2D, resolved?.texture ?? null);
    const sampler = pass.program.uniforms.get(`g_Texture${slot}`);
    if (sampler) gl.uniform1i(sampler.location, slot);
    const resolution = pass.program.uniforms.get(`g_Texture${slot}Resolution`);
    if (resolution && resolved) gl.uniform4fv(resolution.location, resolved.resolution);
  }
  for (const [name, uniform] of pass.program.uniforms) {
    if (uniform.type === gl.SAMPLER_2D || /^g_Texture\d+Resolution$/u.test(name)) continue;
    const value = engineUniform(gl, name, uniform.type, frame, target, draw);
    if (value) setUniform(gl, uniform, value);
  }
  for (const value of pass.values) {
    const uniform = pass.program.uniforms.get(value.name)!;
    setUniform(gl, uniform, toVector(value.value.read(), new Array(componentCount(gl, uniform.type) * uniform.size).fill(0)));
  }
};
