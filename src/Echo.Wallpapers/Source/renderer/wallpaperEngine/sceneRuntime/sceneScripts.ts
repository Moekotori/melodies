import type { SceneAssets } from './sceneAssets';
import { evaluateAnimation, toVector, type PropertyAnimation, type PropertySource, type UserPropertyMap } from './sceneValues';

type ScriptExports = Record<string, unknown> & {
  init?: (value: unknown) => unknown;
  update?: (value: unknown) => unknown;
  scriptProperties?: Record<string, unknown>;
};

type ScriptBinding = {
  exports: ScriptExports;
  value: unknown;
  apply: (value: unknown) => void;
  failed: boolean;
};

/** Layer surface a script sees as `thisLayer` / `thisScene.getLayer()`. */
export type ScriptLayerTarget = Record<string, unknown>;

const moduleFiles: Array<[string, string]> = [
  ['WEMath', 'scripts/jsmodules/wemath.js'],
  ['WEColor', 'scripts/jsmodules/wecolor.js'],
  ['WEVector', 'scripts/jsmodules/wevector.js'],
];

/** Rewrites ES module syntax into a function body that returns live export getters. */
export const transformSceneScript = (source: string): string => {
  const exported = new Set<string>();
  const specifier = (value: string): string => JSON.stringify(value);
  let body = source
    .replace(/\bimport\s+\*\s+as\s+(\w+)\s+from\s+(['"])([^'"]+)\2\s*;?/gu, (_m, name: string, _q, module: string) => `const ${name} = __modules[${specifier(module)}];`)
    .replace(/\bimport\s*\{([^}]*)\}\s*from\s+(['"])([^'"]+)\2\s*;?/gu, (_m, names: string, _q, module: string) =>
      `const { ${names.split(',').map((name) => name.trim().replace(/\s+as\s+/u, ': ')).filter(Boolean).join(', ')} } = __modules[${specifier(module)}];`)
    .replace(/\bimport\s+(\w+)\s+from\s+(['"])([^'"]+)\2\s*;?/gu, (_m, name: string, _q, module: string) => `const ${name} = __modules[${specifier(module)}].default;`)
    .replace(/\bexport\s+(async\s+)?function\s*(\*?)\s*(\w+)/gu, (_m, isAsync: string | undefined, star: string, name: string) => {
      exported.add(name);
      return `${isAsync ?? ''}function${star} ${name}`;
    })
    .replace(/\bexport\s+(let|var|const)\s+(\w+)/gu, (_m, kind: string, name: string) => {
      exported.add(name);
      return `${kind} ${name}`;
    })
    .replace(/\bexport\s+class\s+(\w+)/gu, (_m, name: string) => {
      exported.add(name);
      return `class ${name}`;
    })
    .replace(/\bexport\s+default\s+/gu, () => {
      exported.add('default');
      return 'const __default = ';
    })
    .replace(/\bexport\s*\{([^}]*)\}\s*;?/gu, (_m, names: string) => {
      for (const name of names.split(',')) {
        const [local, alias] = name.trim().split(/\s+as\s+/u);
        if (local) exported.add(alias ? `${alias}=${local}` : local);
      }
      return '';
    });
  const getters = [...exported].map((entry) => {
    const [name, local] = entry.includes('=') ? entry.split('=') : [entry, entry === 'default' ? '__default' : entry];
    return `get ${JSON.stringify(name)}() { return ${local}; }`;
  });
  body += `\nreturn { ${getters.join(', ')} };`;
  return body;
};

const scriptParameters = ['__modules', 'engine', 'shared', 'input', 'thisLayer', 'thisObject', 'thisScene', 'console', 'localStorage'];

/** Constructors from Wallpaper Engine's `baseclasses.js`, loaded by the page before the runtime. */
const globalClass = (name: string): (new (...args: unknown[]) => Record<string, number>) | null =>
  new Function(`return typeof ${name} === 'undefined' ? null : ${name};`)() as (new (...args: unknown[]) => Record<string, number>) | null;

export type AudioBuffers = { left: Float32Array; right: Float32Array; average: Float32Array; resolution: number };

/**
 * SceneScript subset: property scripts (`init`/`update`), `engine`, `shared`,
 * `thisLayer`/`thisScene`, script properties and the WE math modules. Scripts
 * run only inside the sandboxed scene page; a failing script freezes its
 * property at the last value instead of stopping the scene.
 */
export class SceneScriptHost {
  private readonly bindings: ScriptBinding[] = [];
  private readonly animations: Array<{ animation: PropertyAnimation; apply: (value: unknown) => void; scalar: boolean }> = [];
  private readonly modules: Record<string, unknown> = {};
  private readonly audioBuffers = new Map<number, AudioBuffers>();
  get usesAudio(): boolean { return this.audioBuffers.size > 0; }
  private readonly storage = new Map<string, unknown>();
  /** Cursor for scripts' `input`, in scene units and canvas pixels; set from the host's pointer. */
  private cursorWorld = [0, 0, 0];
  private cursorScreen = [0, 0];
  /** A script reads `input`; the host then forwards the cursor. */
  usesCursor = false;
  private readonly shared: Record<string, unknown> = {};
  private readonly startedAt = performance.now();
  readonly vec2 = globalClass('Vec2');
  readonly vec3 = globalClass('Vec3');
  readonly vec4 = globalClass('Vec4');
  readonly engine: Record<string, unknown>;
  private sceneApi: Record<string, unknown> = {};
  /** Scripts attached while the scene is still loading run `init` once every layer exists. */
  private started = false;

  constructor(private readonly assets: SceneAssets, userProperties: UserPropertyMap, screen: { width: number; height: number }) {
    const converted: Record<string, unknown> = {};
    for (const [key, property] of Object.entries(userProperties)) {
      if (!property) continue;
      converted[key] = property.type === 'color' && this.vec3 ? new this.vec3(property.value) : property.value;
    }
    this.engine = {
      runtime: 0,
      frametime: 0,
      timeOfDay: 0,
      userProperties: converted,
      screenResolution: this.makeVec2(screen.width, screen.height),
      canvasSize: this.makeVec2(screen.width, screen.height),
      AUDIO_RESOLUTION_16: 16,
      AUDIO_RESOLUTION_32: 32,
      AUDIO_RESOLUTION_64: 64,
      isDesktopDevice: () => true,
      isMobileDevice: () => false,
      isTabletDevice: () => false,
      isWallpaper: () => true,
      isScreensaver: () => false,
      isRunningInEditor: () => false,
      registerAudioBuffers: (resolution: unknown) => this.registerAudioBuffers(Number(resolution) || 64),
      // Asset handles feed layer creation, which is not supported; the path stands in for the handle.
      registerAsset: (path: unknown) => String(path ?? ''),
      setTimeout: (callback: () => void, delay: number) => window.setTimeout(callback, delay),
      setInterval: (callback: () => void, delay: number) => window.setInterval(callback, delay),
      clearTimeout: (id: number) => window.clearTimeout(id),
      clearInterval: (id: number) => window.clearInterval(id),
    };
  }

  makeVec2(x: number, y: number): unknown {
    return this.vec2 ? new this.vec2(x, y) : { x, y };
  }

  makeVec3(x: number, y: number, z: number): unknown {
    return this.vec3 ? new this.vec3(x, y, z) : { x, y, z };
  }

  /** Vector-typed properties reach scripts as Wallpaper Engine's Vec2/Vec3/Vec4. */
  scriptValue(value: unknown, components: number): unknown {
    if (components < 2) return value;
    const [x, y, z, w] = toVector(value, new Array(components).fill(0));
    if (components === 2) return this.makeVec2(x, y);
    if (components === 3) return this.makeVec3(x, y, z);
    return this.vec4 ? new this.vec4(x, y, z, w) : { x, y, z, w };
  }

  setSceneApi(api: Record<string, unknown>): void {
    this.sceneApi = api;
  }

  async loadModules(): Promise<void> {
    for (const [name, path] of moduleFiles) {
      const source = await this.assets.text(path);
      if (source === null) continue;
      try {
        this.modules[name] = this.instantiate(source, null);
      } catch (error) {
        console.warn(`[scene] script module ${name} failed`, error);
      }
    }
  }

  private registerAudioBuffers(resolution: number): AudioBuffers {
    const size = resolution <= 16 ? 16 : resolution <= 32 ? 32 : 64;
    let buffers = this.audioBuffers.get(size);
    if (!buffers) {
      buffers = { left: new Float32Array(size), right: new Float32Array(size), average: new Float32Array(size), resolution: size };
      this.audioBuffers.set(size, buffers);
    }
    return buffers;
  }

  setCursor(world: number[], screen: number[]): void {
    this.cursorWorld = world;
    this.cursorScreen = screen;
  }

  private instantiate(source: string, layer: ScriptLayerTarget | null): ScriptExports {
    const factory = new Function(...scriptParameters, `"use strict";\n${transformSceneScript(source)}`) as (...args: unknown[]) => ScriptExports;
    // SceneScript's storage API (`get/set/delete/clear` with a location), kept in memory per scene.
    const storage = this.storage;
    const localStorageShim = {
      LOCATION_GLOBAL: 0,
      LOCATION_SCREEN: 1,
      get: (key: unknown) => storage.get(String(key)),
      set: (key: unknown, value: unknown) => { storage.set(String(key), value); },
      delete: (key: unknown) => { storage.delete(String(key)); },
      clear: () => { storage.clear(); },
    };
    if (/\binput\./u.test(source)) this.usesCursor = true;
    const input = Object.defineProperties({}, {
      cursorWorldPosition: { get: () => this.makeVec3(this.cursorWorld[0], this.cursorWorld[1], this.cursorWorld[2]) },
      cursorScreenPosition: { get: () => this.makeVec2(this.cursorScreen[0], this.cursorScreen[1]) },
    });
    return factory(this.modules, this.engine, this.shared, input, layer, layer, this.sceneApi, console, localStorageShim);
  }

  /** Instantiates a property script and/or keyframe track; `apply` receives every value they produce. */
  attach(source: PropertySource, layer: ScriptLayerTarget | null, initial: unknown, apply: (value: unknown) => void): void {
    if (source.animation) {
      this.animations.push({ animation: source.animation, apply, scalar: source.animation.channels.length === 1 && typeof initial !== 'object' });
    }
    if (!source.script) return;
    let exports: ScriptExports;
    try {
      exports = this.instantiate(source.script, layer);
      if (source.scriptProperties && exports.scriptProperties && typeof exports.scriptProperties === 'object') {
        for (const [key, value] of Object.entries(source.scriptProperties)) {
          if (!(key in exports.scriptProperties)) continue;
          const current = exports.scriptProperties[key];
          exports.scriptProperties[key] = this.vec3 && current instanceof this.vec3 ? new this.vec3(value) : value;
        }
      }
    } catch (error) {
      console.warn('[scene] script failed to load', error);
      return;
    }
    const binding: ScriptBinding = { exports, value: initial, apply, failed: false };
    if (this.started && typeof exports.init === 'function') this.run(binding, exports.init);
    this.bindings.push(binding);
  }

  /** Delivers a now-playing event to every script exporting the handler (`mediaPropertiesChanged`, ...). */
  dispatch(handler: string, event: Record<string, unknown>): void {
    for (const binding of this.bindings) {
      const method = binding.exports[handler];
      if (binding.failed || typeof method !== 'function') continue;
      try {
        method.call(binding.exports, event);
      } catch (error) {
        binding.failed = true;
        console.warn('[scene] script stopped', error);
      }
    }
  }

  /** Runs every pending `init`; call once the whole scene is built. */
  start(): void {
    if (this.started) return;
    this.started = true;
    for (const binding of this.bindings) {
      if (typeof binding.exports.init === 'function') this.run(binding, binding.exports.init);
    }
  }

  private run(binding: ScriptBinding, method: (value: unknown) => unknown): void {
    try {
      const result = method.call(binding.exports, binding.value);
      if (result !== undefined) {
        binding.value = result;
        binding.apply(result);
      }
    } catch (error) {
      binding.failed = true;
      console.warn('[scene] script stopped', error);
    }
  }

  /** Advances engine time and audio, then runs every live `update`. */
  update(deltaSeconds: number, spectrum: Float32Array | null): void {
    const now = new Date();
    this.engine.frametime = deltaSeconds;
    this.engine.runtime = (performance.now() - this.startedAt) / 1000;
    this.engine.timeOfDay = ((now.getHours() * 3600) + (now.getMinutes() * 60) + now.getSeconds()) / 86400;
    if (spectrum) {
      for (const buffers of this.audioBuffers.values()) {
        const step = 64 / buffers.resolution;
        for (let index = 0; index < buffers.resolution; index += 1) {
          let left = 0;
          let right = 0;
          for (let offset = 0; offset < step; offset += 1) {
            left += spectrum[(index * step) + offset];
            right += spectrum[64 + (index * step) + offset];
          }
          buffers.left[index] = left / step;
          buffers.right[index] = right / step;
          buffers.average[index] = (left + right) / (2 * step);
        }
      }
    }
    // Keyframes first; a script on the same property sees and may override them.
    const runtime = this.engine.runtime as number;
    for (const track of this.animations) {
      const values = evaluateAnimation(track.animation, runtime);
      track.apply(track.scalar ? values[0] : values);
    }
    for (const binding of this.bindings) {
      if (!binding.failed && typeof binding.exports.update === 'function') this.run(binding, binding.exports.update);
    }
  }
}
