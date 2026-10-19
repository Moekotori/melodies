import type { SceneAssets } from './sceneAssets';
import { SceneAudioSpectrum } from './sceneAudioSpectrum';
import { SceneBlendModes } from './sceneBlendModes';
import { SceneDrawOrder } from './sceneDrawOrder';
import { applyEffects, SceneEffectLoader, type EffectTargets, type LayerEffect } from './sceneEffects';
import { SceneMaterialCompiler, type FrameState, type MaterialPass } from './sceneMaterials';
import { composeTransform, identity4, multiply4, type Mat4 } from './sceneMath3d';
import type { SceneModel } from './sceneModels';
import { Scene3d, type LightSource } from './scene3d';
import {
  applyBlend,
  bindRenderTarget,
  blendModeFor,
  compileProgram,
  createQuad,
  createRenderTarget,
  deleteRenderTarget,
  identity3,
  multiply3,
  scale3,
  texturedQuadFragment,
  texturedQuadVertex,
  translate3,
  type BlendMode,
  type GlProgram,
  type Mat3,
  type RenderTarget,
} from './sceneGl';
import { SceneParallax } from './sceneParallax';
import { ParticleRenderer, ParticleSystem } from './sceneParticles';
import { ScenePuppetRenderer, type ScenePuppet } from './scenePuppets';
import { SceneScriptHost, type ScriptLayerTarget } from './sceneScripts';
import { SceneTextRenderer, type TextStyle } from './sceneText';
import { SceneTextureCache, type SceneTexture } from './sceneTextures';
import { SceneTransform2d } from './sceneTransform2d';
import {
  isRecord,
  readPropertySource,
  resolvePropertyValue,
  toBoolean,
  toNumber,
  toVector,
  type UserPropertyMap,
} from './sceneValues';

type NodeKind = 'image' | 'solid' | 'compose' | 'fullscreen' | 'text' | 'particle' | 'group' | 'model' | 'light' | 'backdrop';

type SceneNode = {
  id: string | null;
  name: string;
  kind: NodeKind;
  parentId: string | null;
  parent: SceneNode | null;
  origin: number[];
  scale: number[];
  angles: number[];
  visible: boolean;
  alpha: number;
  color: number[];
  brightness: number;
  size: number[];
  alignment: string;
  texture: SceneTexture | null;
  blend: BlendMode;
  /** Wallpaper Engine `colorBlendMode`; 0 is ordinary alpha blending. */
  blendMode: number;
  effects: LayerEffect[];
  targets: EffectTargets | null;
  particles: ParticleSystem | null;
  text: string;
  renderedText: string | null;
  textStyle: TextStyle | null;
  textRendering: boolean;
  animationTime: number;
  animationPlaying: boolean;
  animationFrame: number | null;
  world: Mat3;
  transform2d: SceneTransform2d | null;
  worldFrame: number;
  /** 3D scenes: object transform in world units. */
  world4: Mat4;
  model: SceneModel | null;
  /** Material of a `backdrop` layer. */
  passes: MaterialPass[] | null;
  light: LightSource | null;
  /** Puppet-warped image: drawn through its skinned mesh instead of the quad. */
  puppet: ScenePuppet | null;
  /** 3D: drawn mirrored into the planar reflection. */
  reflected: boolean;
  /** How far a root layer moves with camera parallax. */
  parallaxDepth: number[];
  api: ScriptLayerTarget;
  raw: Record<string, unknown>;
};

export type SceneRuntimeConfig = { sceneFile: string; userProperties: UserPropertyMap };

const maxTargetEdge = 4096;

const finalFragment = `#version 300 es
precision highp float;
in vec2 v_Uv;
uniform sampler2D u_Texture;
uniform sampler2D u_Bloom;
uniform float u_BloomStrength;
uniform float u_Fade;
out vec4 o_Color;
void main() {
  vec3 color = texture(u_Texture, v_Uv).rgb + texture(u_Bloom, v_Uv).rgb * u_BloomStrength;
  o_Color = vec4(color * u_Fade, 1.0);
}
`;

const brightFragment = `#version 300 es
precision highp float;
in vec2 v_Uv;
uniform sampler2D u_Texture;
uniform float u_Threshold;
out vec4 o_Color;
void main() {
  vec3 color = texture(u_Texture, v_Uv).rgb;
  float level = max(color.r, max(color.g, color.b));
  o_Color = vec4(color * smoothstep(u_Threshold, u_Threshold + 0.15, level), 1.0);
}
`;

const blurFragment = `#version 300 es
precision highp float;
in vec2 v_Uv;
uniform sampler2D u_Texture;
uniform vec2 u_Direction;
out vec4 o_Color;
void main() {
  vec3 sum = texture(u_Texture, v_Uv).rgb * 0.227;
  sum += texture(u_Texture, v_Uv + u_Direction * 1.385).rgb * 0.316;
  sum += texture(u_Texture, v_Uv - u_Direction * 1.385).rgb * 0.316;
  sum += texture(u_Texture, v_Uv + u_Direction * 3.231).rgb * 0.07;
  sum += texture(u_Texture, v_Uv - u_Direction * 3.231).rgb * 0.07;
  o_Color = vec4(sum, 1.0);
}
`;

const alignmentOffset = (alignment: string, width: number, height: number): [number, number] => {
  const value = alignment.toLowerCase();
  const x = value.includes('left') ? width / 2 : value.includes('right') ? -width / 2 : 0;
  const y = value.includes('top') ? -height / 2 : value.includes('bottom') ? height / 2 : 0;
  return [x, y];
};

/** Live renderer for one Wallpaper Engine scene, drawn into the page canvas. */
export class SceneRenderer {
  private readonly nodes: SceneNode[] = [];
  private readonly quad: WebGLVertexArrayObject;
  private readonly textured: GlProgram;
  private readonly final: GlProgram;
  private readonly bright: GlProgram;
  private readonly blur: GlProgram;
  private readonly particleRenderer: ParticleRenderer;
  private readonly puppets: ScenePuppetRenderer;
  private sceneTarget: RenderTarget | null = null;
  private fullFrame: RenderTarget | null = null;
  private bloomTargets: [RenderTarget, RenderTarget] | null = null;
  private projection: Mat3 = identity3();
  private outputScale = 1;
  private readonly frame: FrameState;
  private time = 0;
  private sceneWidth = 1920;
  private sceneHeight = 1080;
  private clearColor = [0, 0, 0];
  private zoom = 1;
  private bloom: { strength: number; threshold: number } | null = null;
  /** Camera, lights and model draws; null for orthographic (2D) scenes. */
  private space: Scene3d | null = null;
  /** A material samples `_rt_Reflection` and some model is `reflected`. */
  private reflective = false;
  private drawOrder: SceneDrawOrder<SceneNode> | null = null;
  private readonly cameraDistance = (node: SceneNode): number => this.space!.distance(node.world4);
  private worldFrame = 0;
  private parallax = new SceneParallax({ enabled: false, amount: 0, delay: 0, mouseInfluence: 0 });
  /** Camera parallax, a shader or a script reads the cursor, so the host should forward it. */
  wantsPointer = false;
  private shaderUsesAudio = false;
  get wantsAudio(): boolean { return this.shaderUsesAudio || this.scripts.usesAudio; }

  private constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly canvas: HTMLCanvasElement,
    private readonly textures: SceneTextureCache,
    private readonly scripts: SceneScriptHost,
    private readonly textRenderer: SceneTextRenderer,
    private readonly blendModes: SceneBlendModes,
    assets: SceneAssets,
  ) {
    this.quad = createQuad(gl);
    this.textured = compileProgram(gl, texturedQuadVertex, texturedQuadFragment);
    this.final = compileProgram(gl, texturedQuadVertex, finalFragment);
    this.bright = compileProgram(gl, texturedQuadVertex, brightFragment);
    this.blur = compileProgram(gl, texturedQuadVertex, blurFragment);
    this.particleRenderer = new ParticleRenderer(gl);
    this.puppets = new ScenePuppetRenderer(gl, assets);
    this.frame = {
      time: 0, daytime: 0, spectrum: new SceneAudioSpectrum(), screenWidth: canvas.width, screenHeight: canvas.height, pointer: [0.5, 0.5], parallax: [0.5, 0.5],
    };
  }

  static async load(gl: WebGL2RenderingContext, canvas: HTMLCanvasElement, assets: SceneAssets, config: SceneRuntimeConfig): Promise<SceneRenderer> {
    const scene = await assets.json<Record<string, unknown>>(config.sceneFile);
    if (!isRecord(scene)) throw new Error('scene-json-missing');
    const textures = new SceneTextureCache(gl, assets);
    const scripts = new SceneScriptHost(assets, config.userProperties, { width: canvas.width, height: canvas.height });
    await scripts.loadModules();
    const renderer = new SceneRenderer(gl, canvas, textures, scripts, new SceneTextRenderer(gl, assets), await SceneBlendModes.load(gl, assets), assets);
    await renderer.build(scene, assets, config.userProperties);
    scripts.start();
    renderer.resize();
    return renderer;
  }

  private async build(scene: Record<string, unknown>, assets: SceneAssets, userProperties: UserPropertyMap): Promise<void> {
    const general = isRecord(scene.general) ? scene.general : {};
    const projection = isRecord(general.orthogonalprojection) ? general.orthogonalprojection : null;
    if (projection) {
      this.sceneWidth = toNumber(projection.width, 0);
      this.sceneHeight = toNumber(projection.height, 0);
      if (this.sceneWidth <= 0 || this.sceneHeight <= 0) throw new Error('scene-projection-unsupported');
    }
    this.clearColor = toVector(resolvePropertyValue(readPropertySource(general.clearcolor), userProperties), [0, 0, 0]);
    this.zoom = toNumber(resolvePropertyValue(readPropertySource(general.zoom), userProperties), 1) || 1;
    const setting = (raw: unknown): unknown => resolvePropertyValue(readPropertySource(raw), userProperties);
    this.parallax = new SceneParallax({
      enabled: toBoolean(setting(general.cameraparallax), false),
      amount: toNumber(setting(general.cameraparallaxamount), 0.5),
      delay: toNumber(setting(general.cameraparallaxdelay), 0.1),
      mouseInfluence: toNumber(setting(general.cameraparallaxmouseinfluence), 0.5),
    });
    if (toBoolean(resolvePropertyValue(readPropertySource(general.bloom), userProperties), false)) {
      this.bloom = {
        strength: toNumber(resolvePropertyValue(readPropertySource(general.bloomstrength), userProperties), 2),
        threshold: toNumber(resolvePropertyValue(readPropertySource(general.bloomthreshold), userProperties), 0.65),
      };
    }

    const objects = (Array.isArray(scene.objects) ? scene.objects : []).filter(isRecord);
    for (const object of objects) this.nodes.push(this.createNode(object));
    const byId = new Map(this.nodes.filter((node) => node.id !== null).map((node) => [node.id!, node]));
    for (const node of this.nodes) node.parent = node.parentId !== null && node.parentId !== node.id ? byId.get(node.parentId) ?? null : null;

    this.scripts.setSceneApi({
      getLayer: (name: unknown) => this.nodes.find((node) => node.name === name)?.api ?? null,
      getLayerCount: () => this.nodes.length,
      getLayerByIndex: (index: number) => this.nodes[index]?.api ?? null,
      enumerateLayers: () => this.nodes.map((node) => node.api),
      getLayerIndex: (layer: unknown) => this.nodes.findIndex((node) => node.api === layer),
    });

    const materials = new SceneMaterialCompiler(this.gl, assets, this.textures, this.scripts, userProperties);
    // Scenes without an orthographic projection are perspective (3D) scenes.
    if (!projection) this.space = await Scene3d.create(this.gl, assets, materials, scene, general);
    const effectLoader = new SceneEffectLoader(assets, materials);
    // One unsupported layer (a broken file, an unsupported shader) only drops itself.
    await Promise.all(this.nodes.map((node) => this.loadNode(node, assets, materials, effectLoader, userProperties).catch((error: unknown) => {
      console.warn(`[scene] layer ${node.name} skipped`, error);
      node.kind = 'group';
    })));
    this.reflective = materials.usesReflection && this.nodes.some((node) => node.reflected && node.kind === 'model');
    this.wantsPointer = this.parallax.settings.enabled || materials.usesPointer || this.scripts.usesCursor;
    this.shaderUsesAudio = materials.usesAudio;
    if (this.space) this.drawOrder = new SceneDrawOrder(this.nodes);
  }

  private createNode(object: Record<string, unknown>): SceneNode {
    const node: SceneNode = {
      id: object.id === undefined ? null : String(object.id),
      name: typeof object.name === 'string' ? object.name : '',
      kind: 'group',
      parentId: object.parent === undefined || object.parent === null ? null : String(object.parent),
      parent: null,
      origin: [0, 0, 0],
      scale: [1, 1, 1],
      angles: [0, 0, 0],
      visible: true,
      alpha: 1,
      color: [1, 1, 1],
      brightness: 1,
      size: [0, 0],
      alignment: typeof object.alignment === 'string' ? object.alignment : 'center',
      texture: null,
      blend: 'translucent',
      blendMode: toNumber(object.colorBlendMode, 0),
      effects: [],
      targets: null,
      particles: null,
      text: '',
      renderedText: null,
      textStyle: null,
      textRendering: false,
      animationTime: 0,
      animationPlaying: true,
      animationFrame: null,
      world: identity3(),
      transform2d: null,
      worldFrame: 0,
      world4: identity4(),
      model: null,
      passes: null,
      light: null,
      puppet: null,
      reflected: object.reflected === true,
      parallaxDepth: [1, 1],
      api: {},
      raw: object,
    };
    node.api = this.layerApi(node);
    return node;
  }

  private layerApi(node: SceneNode): ScriptLayerTarget {
    const vec3 = (values: number[]): unknown => this.scripts.makeVec3(values[0], values[1], values[2]);
    const vec2 = (values: number[]): unknown => this.scripts.makeVec2(values[0], values[1]);
    return {
      get name() { return node.name; },
      get origin() { return vec3(node.origin); },
      set origin(value: unknown) { node.origin = toVector(value, node.origin); },
      get scale() { return vec3(node.scale); },
      set scale(value: unknown) { node.scale = toVector(value, node.scale); },
      get angles() { return vec3(node.angles); },
      set angles(value: unknown) { node.angles = toVector(value, node.angles); },
      get alpha() { return node.alpha; },
      set alpha(value: unknown) { node.alpha = toNumber(value, node.alpha); },
      get visible() { return node.visible; },
      set visible(value: unknown) { node.visible = toBoolean(value, node.visible); },
      get color() { return vec3(node.color); },
      set color(value: unknown) { node.color = toVector(value, node.color); },
      get brightness() { return node.brightness; },
      set brightness(value: unknown) { node.brightness = toNumber(value, node.brightness); },
      get text() { return node.text; },
      set text(value: unknown) { node.text = String(value ?? ''); },
      get size() { return vec2(node.size); },
      getEffect: (key: unknown) => {
        const effect = typeof key === 'number' ? node.effects[key] : node.effects.find((candidate) => candidate.name === key);
        return effect ? {
          get name() { return effect.name; },
          get visible() { return effect.isVisible(); },
          set visible(value: unknown) { effect.setVisible(toBoolean(value, true)); },
          // Material values by key, e.g. `getMaterial().angle = 90`.
          getMaterial: () => new Proxy({}, {
            get: (_target, key) => (typeof key === 'string' ? effect.material.get(key)?.read() : undefined),
            set: (_target, key, value) => {
              if (typeof key === 'string') effect.material.get(key)?.write(value);
              return true;
            },
          }),
        } : null;
      },
      getEffectCount: () => node.effects.length,
      getTextureAnimation: () => ({
        get frameCount() { return node.texture?.frames.length ?? 0; },
        getFrame: () => node.animationFrame ?? 0,
        setFrame: (frame: number) => { node.animationFrame = Math.max(0, Math.floor(frame)); },
        play: () => { node.animationPlaying = true; node.animationFrame = null; },
        pause: () => { node.animationPlaying = false; },
        stop: () => { node.animationPlaying = false; node.animationFrame = 0; },
        isPlaying: () => node.animationPlaying,
      }),
    };
  }

  /** Binds a property: user value first, then its script (if any) keeps updating it. */
  private bind(node: SceneNode, raw: unknown, userProperties: UserPropertyMap, apply: (value: unknown) => void, components = 0): void {
    if (raw === undefined) return;
    const source = readPropertySource(raw);
    const value = resolvePropertyValue(source, userProperties);
    if (value !== undefined) apply(value);
    this.scripts.attach(source, node.api, this.scripts.scriptValue(value, components), apply);
  }

  private async loadNode(
    node: SceneNode,
    assets: SceneAssets,
    materials: SceneMaterialCompiler,
    effectLoader: SceneEffectLoader,
    userProperties: UserPropertyMap,
  ): Promise<void> {
    const object = node.raw;
    this.bind(node, object.origin, userProperties, (value) => { node.origin = toVector(value, node.origin); }, 3);
    this.bind(node, object.scale, userProperties, (value) => { node.scale = toVector(value, node.scale); }, 3);
    this.bind(node, object.angles, userProperties, (value) => { node.angles = toVector(value, node.angles); }, 3);
    this.bind(node, object.visible, userProperties, (value) => { node.visible = toBoolean(value, node.visible); });
    this.bind(node, object.alpha, userProperties, (value) => { node.alpha = toNumber(value, node.alpha); });
    this.bind(node, object.color, userProperties, (value) => { node.color = toVector(value, node.color); }, 3);
    this.bind(node, object.brightness, userProperties, (value) => { node.brightness = toNumber(value, node.brightness); });
    this.bind(node, object.parallaxDepth, userProperties, (value) => { node.parallaxDepth = toVector(value, node.parallaxDepth); }, 2);
    const size = toVector(object.size, [0, 0]);

    if (this.space && typeof object.model === 'string') {
      node.kind = 'model';
      node.model = await this.space.models.load(object.model, node.api);
      return;
    }
    if (this.space && typeof object.light === 'string') {
      const light: LightSource = { color: [1, 1, 1], intensity: 1, radius: 1 };
      node.light = light;
      node.kind = 'light';
      this.bind(node, object.color, userProperties, (value) => { light.color = toVector(value, light.color); }, 3);
      this.bind(node, object.intensity, userProperties, (value) => { light.intensity = toNumber(value, light.intensity); });
      this.bind(node, object.radius, userProperties, (value) => { light.radius = toNumber(value, light.radius); });
      return;
    }

    if (typeof object.image === 'string') {
      const model = await assets.json<Record<string, unknown>>(object.image);
      if (model?.fullscreen && !model.passthrough && this.space && typeof model.material === 'string') {
        // A full-screen layer with its own material (sky, gradient) paints behind what follows it.
        node.kind = 'backdrop';
        node.passes = await materials.loadMaterial(model.material, node.api);
        return;
      }
      if (model?.fullscreen) node.kind = 'fullscreen';
      else if (model?.passthrough) node.kind = 'compose';
      // An object's own `solid` flag only marks it opaque; colour layers come from the model.
      else if (model?.solidlayer) node.kind = 'solid';
      else {
        const material = typeof model?.material === 'string' ? await assets.json<Record<string, unknown>>(model.material) : null;
        const pass = isRecord(material) && Array.isArray(material.passes) && isRecord(material.passes[0]) ? material.passes[0] : {};
        const textureName = Array.isArray(pass.textures) && typeof pass.textures[0] === 'string' ? pass.textures[0] : null;
        node.texture = textureName ? await this.textures.load(textureName) : null;
        node.kind = node.texture ? 'image' : 'solid';
        node.blend = blendModeFor(pass.blending);
        if (node.texture && typeof model?.puppet === 'string') {
          // An unreadable puppet leaves the image unwarped.
          node.puppet = await this.puppets.load(model.puppet, Array.isArray(object.animationlayers) ? object.animationlayers : [])
            .catch((error: unknown) => {
              console.warn(`[scene] puppet of ${node.name} skipped`, error);
              return null;
            });
        }
      }
      node.size = size[0] > 0 && size[1] > 0 ? size
        : Number(model?.width) > 0 ? [Number(model!.width), Number(model!.height)]
          : node.texture ? [node.texture.imageWidth, node.texture.imageHeight] : [0, 0];
    } else if (typeof object.particle === 'string') {
      node.kind = 'particle';
      node.particles = await ParticleSystem.load(object.particle, assets, this.textures, isRecord(object.instanceoverride) ? object.instanceoverride : {});
    } else if (object.text !== undefined) {
      node.kind = 'text';
      node.size = size;
      this.bind(node, object.text, userProperties, (value) => { node.text = String(value ?? ''); });
      node.textStyle = {
        font: typeof object.font === 'string' ? object.font : null,
        pointSize: toNumber(object.pointsize, 32),
        color: node.color,
        horizontalAlign: typeof object.horizontalalign === 'string' ? object.horizontalalign : 'center',
        verticalAlign: typeof object.verticalalign === 'string' ? object.verticalalign : 'center',
        padding: toNumber(object.padding, 0),
        width: size[0] || 512,
        height: size[1] || 128,
        background: toBoolean(object.opaquebackground, false) ? toVector(object.backgroundcolor, [0, 0, 0]) : null,
      };
      // Text colour is baked into the texture; the draw only applies alpha.
      node.color = [1, 1, 1];
      node.size = [node.textStyle.width, node.textStyle.height];
    }
    // 3D scenes keep their full-screen passes; flat layers have no place in the perspective view.
    if (this.space && node.kind !== 'particle' && node.kind !== 'fullscreen') node.kind = 'group';

    if (node.kind !== 'particle' && node.kind !== 'group' && Array.isArray(object.effects)) {
      const loaded = await Promise.all(object.effects.filter(isRecord).map((instance) => effectLoader.load(instance, node.api)));
      node.effects = loaded.filter((effect): effect is LayerEffect => effect !== null);
    }
  }

  resize(): void {
    const { gl, canvas } = this;
    const scale = Math.min(window.devicePixelRatio || 1, 1);
    canvas.width = Math.max(1, Math.round(canvas.clientWidth * scale));
    canvas.height = Math.max(1, Math.round(canvas.clientHeight * scale));
    if (this.space) {
      // A perspective view is rendered at the canvas aspect, so nothing is cropped.
      this.sceneWidth = canvas.width;
      this.sceneHeight = canvas.height;
    }
    // Cover: the scene fills the canvas and the overflow is cropped at the final blit.
    let cover = Math.max(canvas.width / this.sceneWidth, canvas.height / this.sceneHeight);
    cover = Math.min(cover, maxTargetEdge / Math.max(this.sceneWidth, this.sceneHeight));
    this.outputScale = cover;
    const width = Math.max(1, Math.round(this.sceneWidth * cover));
    const height = Math.max(1, Math.round(this.sceneHeight * cover));
    if (!this.sceneTarget || this.sceneTarget.width !== width || this.sceneTarget.height !== height) {
      if (this.sceneTarget) deleteRenderTarget(gl, this.sceneTarget);
      if (this.fullFrame) deleteRenderTarget(gl, this.fullFrame);
      if (this.bloomTargets) this.bloomTargets.forEach((target) => deleteRenderTarget(gl, target));
      this.sceneTarget = createRenderTarget(gl, width, height, this.space !== null);
      if (this.space && this.reflective) this.space.resizeReflection(width, height);
      this.fullFrame = null;
      this.bloomTargets = this.bloom
        ? [createRenderTarget(gl, Math.max(1, width >> 2), Math.max(1, height >> 2)), createRenderTarget(gl, Math.max(1, width >> 2), Math.max(1, height >> 2))]
        : null;
    }
    const w = this.sceneWidth;
    const h = this.sceneHeight;
    const toClip = new Float32Array([2 / w, 0, 0, 0, -2 / h, 0, -1, 1, 1]);
    this.projection = this.zoom === 1 ? toClip
      : multiply3(toClip, multiply3(translate3(w / 2, h / 2), multiply3(scale3(this.zoom, this.zoom), translate3(-w / 2, -h / 2))));
    this.frame.screenWidth = canvas.width;
    this.frame.screenHeight = canvas.height;
  }

  /** Cursor over the wallpaper from the host, canvas-normalised with y down. */
  setPointer(x: number, y: number): void {
    this.parallax.setCursor(x, y);
    const { canvas } = this;
    const screen = [x * canvas.width, y * canvas.height];
    if (this.space) {
      this.scripts.setCursor([0, 0, 0], screen);
      return;
    }
    // The scene covers the canvas, cropped evenly; scene y grows upwards.
    const visibleWidth = canvas.width / this.outputScale;
    const visibleHeight = canvas.height / this.outputScale;
    const sceneX = ((this.sceneWidth - visibleWidth) / 2) + (x * visibleWidth);
    const sceneY = this.sceneHeight - (((this.sceneHeight - visibleHeight) / 2) + (y * visibleHeight));
    this.scripts.setCursor([sceneX, sceneY, 0], screen);
  }

  setSpectrum(samples: ArrayLike<number>): void {
    this.frame.spectrum.update(samples);
  }

  /** Now-playing facts from ECHO, in Wallpaper Engine's media event shapes. */
  dispatchMedia(type: string, data: Record<string, unknown>): void {
    if (type === 'echo-we:media' && data.properties && typeof data.properties === 'object') {
      this.scripts.dispatch('mediaPropertiesChanged', data.properties as Record<string, unknown>);
    } else if (type === 'echo-we:playback') {
      this.scripts.dispatch('mediaPlaybackChanged', { state: Number(data.state) || 0 });
    } else if (type === 'echo-we:timeline') {
      this.scripts.dispatch('mediaTimelineChanged', { position: Number(data.position) || 0, duration: Number(data.duration) || 0 });
    }
  }

  setPaused(paused: boolean): void {
    this.textures.setVideosPaused(paused);
  }

  private updateWorld(node: SceneNode): void {
    if (node.worldFrame === this.worldFrame) return;
    node.worldFrame = this.worldFrame;
    if (node.parent) this.updateWorld(node.parent);
    if (this.space) {
      const local = composeTransform(node.origin, node.angles, node.scale);
      node.world4 = node.parent ? multiply4(node.parent.world4, local) : local;
      return;
    }
    let shiftX = 0; let shiftY = 0;
    if (!node.parent && this.parallax.settings.enabled) {
      const offset = this.parallax.layerOffset(node.parallaxDepth, this.sceneWidth);
      shiftX = offset[0]; shiftY = offset[1];
    }
    node.transform2d ??= new SceneTransform2d(node.world);
    node.transform2d.update(node.origin[0] + shiftX, node.origin[1] + shiftY, node.angles[2], node.scale[0], node.scale[1], node.parent?.transform2d ?? null);
  }

  private isVisible(node: SceneNode): boolean {
    for (let current: SceneNode | null = node, depth = 0; current && depth < 32; current = current.parent, depth += 1) {
      if (!current.visible) return false;
    }
    return node.alpha > 0;
  }

  private uvRect(node: SceneNode, texture: SceneTexture, deltaSeconds: number): [number, number, number, number] {
    const frames = texture.frames;
    if (frames.length > 1) {
      if (node.animationPlaying && node.animationFrame === null) node.animationTime += deltaSeconds;
      let index = node.animationFrame ?? 0;
      if (node.animationFrame === null) {
        const duration = frames.reduce((sum, frame) => sum + Math.max(0.001, frame.frameTime), 0);
        let time = node.animationTime % duration;
        for (index = 0; index < frames.length - 1 && time >= Math.max(0.001, frames[index].frameTime); index += 1) {
          time -= Math.max(0.001, frames[index].frameTime);
        }
      }
      const frame = frames[Math.min(index, frames.length - 1)];
      return [frame.x / texture.width, frame.y / texture.height, frame.width / texture.width, frame.height / texture.height];
    }
    return [0, 0, texture.imageWidth / texture.width, texture.imageHeight / texture.height];
  }

  private drawTexture(texture: WebGLTexture, matrix: Mat3, uv: number[], color: number[], blend: BlendMode): void {
    const { gl } = this;
    const uniforms = this.textured.uniforms;
    gl.useProgram(this.textured.program);
    gl.bindVertexArray(this.quad);
    applyBlend(gl, blend);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.uniform1i(uniforms.get('u_Texture')!.location, 0);
    gl.uniformMatrix3fv(uniforms.get('u_Matrix')!.location, false, matrix);
    gl.uniform4fv(uniforms.get('u_UvRect')!.location, uv);
    gl.uniform4fv(uniforms.get('u_Color')!.location, color);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  }

  /** Final draw of a layer image into the scene, honouring its colour blend mode. */
  private composite(node: SceneNode, texture: WebGLTexture, matrix: Mat3, uv: number[], color: number[], blend: BlendMode): void {
    const { gl } = this;
    const scene = this.sceneTarget!;
    const program = node.blendMode !== 0 ? this.blendModes.program(node.blendMode) : null;
    if (!program) {
      bindRenderTarget(gl, scene);
      this.drawTexture(texture, matrix, uv, color, blend);
      return;
    }
    const background = this.snapshotFullFrame();
    bindRenderTarget(gl, scene);
    gl.useProgram(program.program);
    gl.bindVertexArray(this.quad);
    gl.disable(gl.BLEND);
    const uniforms = program.uniforms;
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, background.texture);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    if (uniforms.has('g_Texture0')) gl.uniform1i(uniforms.get('g_Texture0')!.location, 0);
    if (uniforms.has('g_Texture1')) gl.uniform1i(uniforms.get('g_Texture1')!.location, 1);
    gl.uniformMatrix3fv(uniforms.get('u_Matrix')!.location, false, matrix);
    gl.uniform4fv(uniforms.get('u_UvRect')!.location, uv);
    if (uniforms.has('u_Color')) gl.uniform4fv(uniforms.get('u_Color')!.location, color);
    if (uniforms.has('u_TargetSize')) gl.uniform2f(uniforms.get('u_TargetSize')!.location, scene.width, scene.height);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  }

  private ensureTargets(node: SceneNode, width: number, height: number): EffectTargets {
    const { gl } = this;
    if (node.targets && node.targets.ping.width === width && node.targets.ping.height === height) return node.targets;
    if (node.targets) {
      deleteRenderTarget(gl, node.targets.ping);
      deleteRenderTarget(gl, node.targets.pong);
      node.targets.named.forEach((target) => deleteRenderTarget(gl, target));
    }
    node.targets = { ping: createRenderTarget(gl, width, height), pong: createRenderTarget(gl, width, height), named: new Map() };
    return node.targets;
  }

  private snapshotFullFrame(): RenderTarget {
    const scene = this.sceneTarget!;
    if (!this.fullFrame) this.fullFrame = createRenderTarget(this.gl, scene.width, scene.height);
    bindRenderTarget(this.gl, this.fullFrame);
    this.drawTexture(scene.texture, identity3(), [0, 0, 1, 1], [1, 1, 1, 1], 'opaque');
    return this.fullFrame;
  }

  private drawLayer(node: SceneNode, deltaSeconds: number): void {
    const { gl } = this;
    const scene = this.sceneTarget!;
    const [width, height] = node.size;
    if (width <= 0 || height <= 0) return;
    const [offsetX, offsetY] = alignmentOffset(node.alignment, width, height);
    const quadMatrix = multiply3(this.projection, multiply3(node.world, multiply3(translate3(offsetX, offsetY), scale3(width / 2, -height / 2))));
    const tint = [node.color[0] * node.brightness, node.color[1] * node.brightness, node.color[2] * node.brightness, Math.min(1, node.alpha)];
    const texture = node.kind === 'solid' ? this.textures.white : node.texture;
    if (texture) this.textures.useVideo(texture);
    const compose = node.kind === 'compose' || node.kind === 'fullscreen';
    const effects = node.effects.filter((effect) => effect.isVisible());
    // Effects work in the image's own space; a puppet warps only the final draw.
    const place = (source: WebGLTexture, uv: number[], color: number[], blend: BlendMode): void => {
      if (!node.puppet) {
        this.composite(node, source, node.kind === 'fullscreen' ? identity3() : quadMatrix, uv, color, blend);
        return;
      }
      bindRenderTarget(gl, scene);
      this.puppets.draw(node.puppet, source, multiply3(this.projection, multiply3(node.world, translate3(offsetX, offsetY))), uv, color, blend);
    };
    if (!texture && !compose) return;

    if (effects.length === 0) {
      if (compose) return;
      place(texture!.texture, this.uvRect(node, texture!, deltaSeconds), tint, node.blend);
      return;
    }

    const scaleX = Math.hypot(node.world[0], node.world[1]);
    const scaleY = Math.hypot(node.world[3], node.world[4]);
    const resolution = Math.min(1, this.outputScale * Math.max(scaleX, scaleY));
    const targetWidth = node.kind === 'fullscreen' ? scene.width : Math.max(1, Math.min(maxTargetEdge, Math.round(width * resolution)));
    const targetHeight = node.kind === 'fullscreen' ? scene.height : Math.max(1, Math.min(maxTargetEdge, Math.round(height * resolution)));
    const targets = this.ensureTargets(node, targetWidth, targetHeight);
    const fullFrame = effects.some((effect) => effect.usesFullFrame) ? this.snapshotFullFrame() : null;

    bindRenderTarget(gl, targets.ping);
    gl.clearColor(0, 0, 0, 0);
    gl.clear(gl.COLOR_BUFFER_BIT);
    if (compose) {
      // Adjustment layers start from what is already drawn beneath them.
      const uv = node.kind === 'fullscreen' ? [0, 0, 1, 1] : (() => {
        const centerX = node.world[6] + offsetX;
        const centerY = node.world[7] + offsetY;
        const w = width * scaleX;
        const h = height * scaleY;
        return [(centerX - (w / 2)) / this.sceneWidth, 1 - ((centerY + (h / 2)) / this.sceneHeight), w / this.sceneWidth, h / this.sceneHeight];
      })();
      this.drawTexture(scene.texture, identity3(), uv, [1, 1, 1, 1], 'opaque');
    } else {
      const solidColor = node.kind === 'solid' ? [tint[0], tint[1], tint[2], 1] : [1, 1, 1, 1];
      this.drawTexture(texture!.texture, identity3(), this.uvRect(node, texture!, deltaSeconds), solidColor, 'opaque');
    }

    const result = applyEffects(gl, this.quad, effects, targets, this.frame, fullFrame, this.textured, (used) => this.textures.useVideo(used));
    const finalTint = node.kind === 'solid' || compose ? [1, 1, 1, tint[3]] : tint;
    place(result.texture, [0, 0, 1, 1], finalTint, compose ? 'translucent' : node.blend);
  }

  private refreshText(node: SceneNode): void {
    if (node.textRendering || node.renderedText === node.text || !node.textStyle) return;
    node.textRendering = true;
    const text = node.text;
    const pixelScale = Math.max(1, this.outputScale * Math.hypot(node.world[0], node.world[1]) * 1.5);
    void this.textRenderer.render(text, node.textStyle, pixelScale, node.texture).then((texture) => {
      node.texture = texture;
      node.renderedText = text;
    }).finally(() => { node.textRendering = false; });
  }

  private drawBloom(): void {
    const { gl } = this;
    const [a, b] = this.bloomTargets!;
    bindRenderTarget(gl, a);
    gl.useProgram(this.bright.program);
    gl.bindVertexArray(this.quad);
    gl.disable(gl.BLEND);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, this.sceneTarget!.texture);
    gl.uniform1i(this.bright.uniforms.get('u_Texture')!.location, 0);
    gl.uniformMatrix3fv(this.bright.uniforms.get('u_Matrix')!.location, false, identity3());
    gl.uniform4f(this.bright.uniforms.get('u_UvRect')!.location, 0, 0, 1, 1);
    gl.uniform1f(this.bright.uniforms.get('u_Threshold')!.location, this.bloom!.threshold);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    gl.useProgram(this.blur.program);
    gl.uniformMatrix3fv(this.blur.uniforms.get('u_Matrix')!.location, false, identity3());
    gl.uniform4f(this.blur.uniforms.get('u_UvRect')!.location, 0, 0, 1, 1);
    gl.uniform1i(this.blur.uniforms.get('u_Texture')!.location, 0);
    for (let iteration = 0; iteration < 2; iteration += 1) {
      bindRenderTarget(gl, b);
      gl.bindTexture(gl.TEXTURE_2D, a.texture);
      gl.uniform2f(this.blur.uniforms.get('u_Direction')!.location, 1 / a.width, 0);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      bindRenderTarget(gl, a);
      gl.bindTexture(gl.TEXTURE_2D, b.texture);
      gl.uniform2f(this.blur.uniforms.get('u_Direction')!.location, 0, 1 / a.height);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    }
  }

  render(deltaSeconds: number): void {
    const { gl } = this;
    this.time += deltaSeconds;
    this.parallax.update(deltaSeconds);
    const [parallaxX, parallaxY] = this.parallax.displacement;
    this.frame.pointer = this.parallax.pointer;
    this.frame.parallax = [0.5 + (parallaxX / 2), 0.5 + (parallaxY / 2)];
    const now = new Date();
    this.frame.time = this.time;
    this.frame.daytime = ((now.getHours() * 3600) + (now.getMinutes() * 60) + now.getSeconds()) / 86400;
    this.scripts.update(deltaSeconds, this.frame.spectrum.samples);
    this.textures.updateVideos();

    this.worldFrame += 1;
    for (const node of this.nodes) this.updateWorld(node);
    for (const node of this.nodes) {
      if (node.particles && this.isVisible(node)) node.particles.update(Math.min(deltaSeconds, 0.1));
      if (node.puppet && this.isVisible(node)) this.puppets.advance(node.puppet, deltaSeconds);
      if (node.kind === 'text') this.refreshText(node);
    }

    const scene = this.sceneTarget!;
    const space = this.space;
    bindRenderTarget(gl, scene);
    gl.clearColor(this.clearColor[0], this.clearColor[1], this.clearColor[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    if (space) {
      const lights = this.nodes.filter((node) => node.light && this.isVisible(node)).map((node) => ({ source: node.light!, world: node.world4 }));
      space.begin(this.time, scene.width / scene.height, lights, this.parallax.cameraOffset);
    }
    const useTexture = (texture: SceneTexture): void => this.textures.useVideo(texture);
    if (space && this.reflective) {
      const reflected = this.nodes.filter((node) => node.reflected && node.model && this.isVisible(node)).map((node) => ({ model: node.model!, world: node.world4 }));
      space.drawReflection(reflected, this.clearColor, this.frame, useTexture);
    }
    for (const node of space ? this.drawOrder!.update(this.cameraDistance) : this.nodes) {
      if (node.kind === 'group' || node.kind === 'light' || !this.isVisible(node)) continue;
      if (node.kind === 'particle') {
        if (!node.particles) continue;
        bindRenderTarget(gl, scene);
        if (space) space.drawParticles(this.particleRenderer, node.particles, node.world4, Math.min(1, node.alpha));
        else this.particleRenderer.draw(node.particles, multiply3(this.projection, node.world), Math.min(1, node.alpha));
        continue;
      }
      if (space && node.kind === 'model') {
        bindRenderTarget(gl, scene);
        space.drawModel(node.model!, node.world4, this.frame, scene, useTexture);
        continue;
      }
      if (space && node.kind === 'backdrop') {
        bindRenderTarget(gl, scene);
        space.drawBackdrop(node.passes!, this.quad, this.frame, scene, useTexture);
        continue;
      }
      this.drawLayer(node, deltaSeconds);
    }
    if (this.bloomTargets) this.drawBloom();

    bindRenderTarget(gl, null, this.canvas.width, this.canvas.height);
    const cropX = this.canvas.width / scene.width;
    const cropY = this.canvas.height / scene.height;
    const left = (1 - cropX) / 2;
    const top = (1 - cropY) / 2;
    gl.useProgram(this.final.program);
    gl.bindVertexArray(this.quad);
    gl.disable(gl.BLEND);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, scene.texture);
    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.bloomTargets?.[0].texture ?? this.textures.black.texture);
    const uniforms = this.final.uniforms;
    gl.uniform1i(uniforms.get('u_Texture')!.location, 0);
    gl.uniform1i(uniforms.get('u_Bloom')!.location, 1);
    gl.uniform1f(uniforms.get('u_BloomStrength')!.location, this.bloom ? this.bloom.strength * 0.5 : 0);
    gl.uniform1f(uniforms.get('u_Fade')!.location, this.space?.fade ?? 1);
    gl.uniformMatrix3fv(uniforms.get('u_Matrix')!.location, false, identity3());
    // Canvas row 0 is the bottom; flip back to image-top-first sampling.
    gl.uniform4f(uniforms.get('u_UvRect')!.location, left, top + cropY, cropX, -cropY);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
  }
}
