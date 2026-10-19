import type { SceneAssets } from './sceneAssets';
import { applyBlend, blendModeFor, compileProgram, positionAttribute, type BlendMode, type GlProgram, type Mat3 } from './sceneGl';
import type { Mat4, Vec3 } from './sceneMath3d';
import type { SceneTexture, SceneTextureCache } from './sceneTextures';
import { isRecord, toNumber, toVector } from './sceneValues';

const maxParticlesPerSystem = 5000;
const instanceFloats = 16;
const stepSeconds = 1 / 30;

type Json = Record<string, unknown>;
type Emitter = { kind: 'box' | 'sphere'; rate: number; instantaneous: number; duration: number; delay: number;
  min: number[]; max: number[]; distanceMin: number; distanceMax: number; directions: number[]; origin: number[]; sign: number[];
  accumulator: number; emitted: boolean };
type ChildMode = 'follow' | 'spawn' | 'death' | 'static';

const random = (min: number, max: number): number => min + (Math.random() * (max - min));
const vector = (value: unknown, fallback: number[]): number[] => toVector(value, fallback);
const scalar = (value: unknown, fallback: number): number => (Array.isArray(value) || typeof value === 'string' ? toVector(value, [fallback])[0] : toNumber(value, fallback));
const smooth = (edge0: number, edge1: number, value: number): number => {
  if (edge1 === edge0) return value >= edge1 ? 1 : 0;
  const t = Math.min(1, Math.max(0, (value - edge0) / (edge1 - edge0)));
  return t * t * (3 - (2 * t));
};

/** Cheap smooth 2D value noise for the turbulence operator. */
const hash = (x: number, y: number): number => {
  const value = Math.sin((x * 127.1) + (y * 311.7)) * 43758.5453;
  return value - Math.floor(value);
};
const valueNoise = (x: number, y: number): number => {
  const ix = Math.floor(x);
  const iy = Math.floor(y);
  const fx = x - ix;
  const fy = y - iy;
  const ux = fx * fx * (3 - (2 * fx));
  const uy = fy * fy * (3 - (2 * fy));
  const a = hash(ix, iy);
  const b = hash(ix + 1, iy);
  const c = hash(ix, iy + 1);
  const d = hash(ix + 1, iy + 1);
  return ((a + ((b - a) * ux)) + (((c + ((d - c) * ux)) - (a + ((b - a) * ux))) * uy)) * 2 - 1;
};

export const particleVertex = `#version 300 es
layout(location = 0) in vec3 a_Position;
layout(location = 2) in vec4 i_Transform;
layout(location = 3) in vec4 i_Color;
layout(location = 4) in vec4 i_UvRect;
layout(location = 5) in vec4 i_Extra;
uniform mat3 u_Matrix;
out vec2 v_Uv;
out vec4 v_Color;
void main() {
  vec2 corner = a_Position.xy * 0.5 * i_Transform.zw;
  float c = cos(i_Extra.x);
  float s = sin(i_Extra.x);
  corner = vec2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
  vec3 position = u_Matrix * vec3(i_Transform.xy + corner, 1.0);
  gl_Position = vec4(position.xy, 0.0, 1.0);
  v_Uv = i_UvRect.xy + vec2(a_Position.x * 0.5 + 0.5, 0.5 - a_Position.y * 0.5) * i_UvRect.zw;
  v_Color = i_Color;
}
`;

/** 3D scenes: camera-facing billboards in world space; sizes are world units. */
export const particleBillboardVertex = `#version 300 es
layout(location = 0) in vec3 a_Position;
layout(location = 2) in vec4 i_Transform;
layout(location = 3) in vec4 i_Color;
layout(location = 4) in vec4 i_UvRect;
layout(location = 5) in vec4 i_Extra;
uniform mat4 u_Model;
uniform mat4 u_ViewProjection;
uniform vec3 u_Right;
uniform vec3 u_Up;
uniform float u_Scale;
out vec2 v_Uv;
out vec4 v_Color;
void main() {
  vec2 corner = a_Position.xy * 0.5 * i_Transform.zw * u_Scale;
  float c = cos(i_Extra.x);
  float s = sin(i_Extra.x);
  corner = vec2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
  vec3 center = (u_Model * vec4(i_Transform.xy, i_Extra.y, 1.0)).xyz;
  gl_Position = u_ViewProjection * vec4(center + u_Right * corner.x + u_Up * corner.y, 1.0);
  v_Uv = i_UvRect.xy + vec2(a_Position.x * 0.5 + 0.5, 0.5 - a_Position.y * 0.5) * i_UvRect.zw;
  v_Color = i_Color;
}
`;

export const particleFragment = `#version 300 es
precision highp float;
in vec2 v_Uv;
in vec4 v_Color;
uniform sampler2D u_Texture;
out vec4 o_Color;
void main() {
  o_Color = texture(u_Texture, v_Uv) * v_Color;
}
`;

/** One emitter tree from a `particles/*.json` definition, simulated in the object's local space. */
export class ParticleSystem {
  private count = 0;
  private readonly capacity: number;
  private readonly x: Float32Array; private readonly y: Float32Array; private readonly z: Float32Array; private readonly vz: Float32Array;
  private readonly vx: Float32Array; private readonly vy: Float32Array;
  private readonly size: Float32Array; private readonly baseSize: Float32Array;
  private readonly rotation: Float32Array; private readonly angular: Float32Array;
  private readonly color: Float32Array; private readonly baseAlpha: Float32Array; private readonly alpha: Float32Array;
  private readonly age: Float32Array; private readonly life: Float32Array;
  private readonly seed: Float32Array; private readonly frame: Float32Array;
  private readonly serial: Uint32Array; private readonly parent: Int32Array; private readonly parentSerial: Uint32Array;
  private nextSerial = 1;
  private elapsed = 0;
  private readonly instanceData: Float32Array;
  private readonly children: Array<{ system: ParticleSystem; mode: ChildMode; probability: number; maxCount: number; offset: number[] }> = [];
  private parentSystem: ParticleSystem | null = null;

  private constructor(
    private readonly definition: Json,
    private readonly emitters: Emitter[],
    private readonly initializers: Json[],
    private readonly operators: Json[],
    private readonly overrides: Json,
    private readonly texture: SceneTexture,
    readonly blend: BlendMode,
    private readonly trailLength: number,
  ) {
    const countScale = toNumber(overrides.count, 1);
    this.capacity = Math.max(1, Math.min(maxParticlesPerSystem, Math.round(toNumber(definition.maxcount, 100) * countScale)));
    const floats = (): Float32Array => new Float32Array(this.capacity);
    this.x = floats(); this.y = floats(); this.vx = floats(); this.vy = floats(); this.z = floats(); this.vz = floats();
    this.size = floats(); this.baseSize = floats(); this.rotation = floats(); this.angular = floats();
    this.color = new Float32Array(this.capacity * 3); this.baseAlpha = floats(); this.alpha = floats();
    this.age = floats(); this.life = floats(); this.seed = floats(); this.frame = floats();
    this.serial = new Uint32Array(this.capacity); this.parent = new Int32Array(this.capacity); this.parentSerial = new Uint32Array(this.capacity);
    this.instanceData = new Float32Array(this.capacity * instanceFloats);
  }

  static async load(path: string, assets: SceneAssets, textures: SceneTextureCache, overrides: Json, depth = 0): Promise<ParticleSystem | null> {
    const definition = await assets.json<Json>(path);
    if (!isRecord(definition)) return null;
    const material = typeof definition.material === 'string' ? await assets.json<Json>(definition.material) : null;
    const pass = isRecord(material) && Array.isArray(material.passes) && isRecord(material.passes[0]) ? material.passes[0] : {};
    const textureName = Array.isArray(pass.textures) && typeof pass.textures[0] === 'string' ? pass.textures[0] : null;
    const texture = (textureName ? await textures.load(textureName) : null) ?? textures.white;
    const list = (value: unknown): Json[] => (Array.isArray(value) ? value.filter(isRecord) : []);
    const emitters: Emitter[] = list(definition.emitter).map((emitter) => {
      const distanceMin = emitter.distancemin;
      const distanceMax = emitter.distancemax;
      return {
        kind: emitter.name === 'sphererandom' ? 'sphere' : 'box',
        rate: toNumber(emitter.rate, 5) * toNumber(overrides.rate, 1),
        instantaneous: toNumber(emitter.instantaneous, 0),
        duration: toNumber(emitter.duration, 0),
        delay: toNumber(emitter.delay, 0),
        min: vector(distanceMin, [0, 0, 0]),
        max: vector(distanceMax, [0, 0, 0]),
        distanceMin: scalar(distanceMin, 0),
        distanceMax: scalar(distanceMax, 0),
        directions: vector(emitter.directions, [1, 1, 0]),
        origin: vector(emitter.origin, [0, 0, 0]),
        sign: vector(emitter.sign, [0, 0, 0]),
        accumulator: 0,
        emitted: false,
      };
    });
    const renderer = list(definition.renderer)[0] ?? {};
    const system = new ParticleSystem(
      definition, emitters, list(definition.initializer), list(definition.operator), overrides, texture,
      blendModeFor(pass.blending ?? 'additive'), renderer.name === 'spritetrail' ? toNumber(renderer.length, 0.05) : 0,
    );
    if (depth < 3) {
      for (const child of list(definition.children)) {
        if (typeof child.name !== 'string') continue;
        const childSystem = await ParticleSystem.load(child.name, assets, textures, overrides, depth + 1);
        if (!childSystem) continue;
        const type = String(child.type ?? 'static');
        childSystem.parentSystem = system;
        system.children.push({
          system: childSystem,
          mode: type === 'eventfollow' ? 'follow' : type === 'eventspawn' ? 'spawn' : type === 'eventdeath' ? 'death' : 'static',
          probability: toNumber(child.probability, 1),
          maxCount: Math.max(1, toNumber(child.maxcount, 1)),
          offset: vector(child.origin, [0, 0, 0]),
        });
      }
    }
    const prewarm = Math.min(10, toNumber(definition.starttime, 0));
    for (let time = 0; time < prewarm; time += stepSeconds) system.update(stepSeconds);
    return system;
  }

  private spawn(x: number, y: number, z: number, parentIndex = -1): number {
    if (this.count >= this.capacity) return -1;
    const index = this.count;
    this.count += 1;
    this.x[index] = x; this.y[index] = y; this.vx[index] = 0; this.vy[index] = 0; this.z[index] = z; this.vz[index] = 0;
    this.baseSize[index] = 20; this.rotation[index] = 0; this.angular[index] = 0;
    this.color[index * 3] = 1; this.color[(index * 3) + 1] = 1; this.color[(index * 3) + 2] = 1;
    this.baseAlpha[index] = 1; this.age[index] = 0; this.life[index] = 1;
    this.seed[index] = Math.random(); this.frame[index] = 0;
    this.serial[index] = this.nextSerial++;
    this.parent[index] = parentIndex;
    this.parentSerial[index] = parentIndex >= 0 && this.parentSystem ? this.parentSystem.serial[parentIndex] : 0;
    for (const initializer of this.initializers) this.initialize(index, initializer);
    this.life[index] *= toNumber(this.overrides.lifetime, 1);
    this.baseSize[index] *= toNumber(this.overrides.size, 1);
    const speed = toNumber(this.overrides.speed, 1);
    this.vx[index] *= speed; this.vy[index] *= speed; this.vz[index] *= speed;
    this.baseAlpha[index] *= toNumber(this.overrides.alpha, 1);
    const tint = vector(this.overrides.color, [1, 1, 1]);
    const brightness = toNumber(this.overrides.brightness, 1);
    for (let channel = 0; channel < 3; channel += 1) this.color[(index * 3) + channel] *= tint[channel] * brightness;
    if (this.texture.frames.length > 1 && this.definition.animationmode === 'randomframe') {
      this.frame[index] = Math.floor(Math.random() * this.texture.frames.length);
    }
    for (const child of this.children) {
      if (Math.random() > child.probability) continue;
      if (child.mode === 'follow') for (let n = 0; n < child.maxCount; n += 1) child.system.spawn(child.offset[0], child.offset[1], child.offset[2], index);
      else if (child.mode === 'spawn') for (let n = 0; n < child.maxCount; n += 1) child.system.spawn(x + child.offset[0], y + child.offset[1], z + child.offset[2]);
    }
    return index;
  }

  private initialize(index: number, initializer: Json): void {
    const min = initializer.min;
    const max = initializer.max;
    switch (initializer.name) {
      case 'lifetimerandom': this.life[index] = random(toNumber(min, 1), toNumber(max, 1)); break;
      case 'sizerandom': this.baseSize[index] = random(toNumber(min, 20), toNumber(max, 20)); break;
      case 'alpharandom': this.baseAlpha[index] = random(toNumber(min, 1), toNumber(max, 1)); break;
      case 'velocityrandom': {
        const low = vector(min, [0, 0, 0]);
        const high = vector(max, [0, 0, 0]);
        this.vx[index] = random(low[0], high[0]);
        this.vy[index] = random(low[1], high[1]);
        this.vz[index] = random(low[2], high[2]);
        break;
      }
      case 'colorrandom': {
        const low = vector(min, [255, 255, 255]);
        const high = vector(max, [255, 255, 255]);
        for (let channel = 0; channel < 3; channel += 1) this.color[(index * 3) + channel] = random(low[channel], high[channel]) / 255;
        break;
      }
      case 'rotationrandom': this.rotation[index] = random(scalarAxis(min, 0), scalarAxis(max, Math.PI * 2)); break;
      case 'angularvelocityrandom': this.angular[index] = random(scalarAxis(min, 0), scalarAxis(max, 0)); break;
      case 'turbulentvelocityrandom': {
        const speed = random(toNumber(initializer.speedmin, 0), toNumber(initializer.speedmax, 100));
        const angle = Math.random() * Math.PI * 2;
        this.vx[index] += Math.cos(angle) * speed;
        this.vy[index] += Math.sin(angle) * speed;
        break;
      }
      case 'positionoffsetrandom': {
        const distance = toNumber(initializer.distance, 0);
        const angle = Math.random() * Math.PI * 2;
        this.x[index] += Math.cos(angle) * distance * Math.random();
        this.y[index] += Math.sin(angle) * distance * Math.random();
        break;
      }
      default:
        break;
    }
  }

  private emit(emitter: Emitter): void {
    const sign = (axis: number): number => (emitter.sign[axis] > 0 ? 1 : emitter.sign[axis] < 0 ? -1 : (Math.random() < 0.5 ? -1 : 1));
    let x = emitter.origin[0];
    let y = emitter.origin[1];
    let z = emitter.origin[2];
    if (emitter.kind === 'sphere') {
      // Uniform direction on the sphere, flattened by `directions` (2D emitters zero z).
      const theta = Math.random() * Math.PI * 2;
      const cosPhi = (Math.random() * 2) - 1;
      const ring = Math.sqrt(1 - (cosPhi * cosPhi));
      const planar = emitter.directions[2] === 0;
      const distance = random(emitter.distanceMin, emitter.distanceMax);
      x += Math.cos(theta) * (planar ? 1 : ring) * distance * emitter.directions[0];
      y += Math.sin(theta) * (planar ? 1 : ring) * distance * emitter.directions[1];
      z += (planar ? 0 : cosPhi) * distance * emitter.directions[2];
    } else {
      x += sign(0) * random(emitter.min[0], emitter.max[0]);
      y += sign(1) * random(emitter.min[1], emitter.max[1]);
      z += sign(2) * random(emitter.min[2], emitter.max[2]);
    }
    this.spawn(x, y, z);
  }

  private kill(index: number): void {
    const last = this.count - 1;
    for (const child of this.children) if (child.mode === 'death' && Math.random() <= child.probability) {
      for (let n = 0; n < child.maxCount; n += 1) child.system.spawn(this.x[index] + child.offset[0], this.y[index] + child.offset[1], this.z[index] + child.offset[2]);
    }
    if (index !== last) {
      this.x[index] = this.x[last]; this.y[index] = this.y[last]; this.vx[index] = this.vx[last]; this.vy[index] = this.vy[last];
      this.z[index] = this.z[last]; this.vz[index] = this.vz[last];
      this.size[index] = this.size[last]; this.baseSize[index] = this.baseSize[last];
      this.rotation[index] = this.rotation[last]; this.angular[index] = this.angular[last];
      this.color.copyWithin(index * 3, last * 3, (last * 3) + 3);
      this.baseAlpha[index] = this.baseAlpha[last]; this.alpha[index] = this.alpha[last];
      this.age[index] = this.age[last]; this.life[index] = this.life[last];
      this.seed[index] = this.seed[last]; this.frame[index] = this.frame[last];
      this.serial[index] = this.serial[last]; this.parent[index] = this.parent[last]; this.parentSerial[index] = this.parentSerial[last];
    }
    this.count = last;
  }

  update(dt: number): void {
    this.elapsed += dt;
    if (!this.parentSystem) {
      for (const emitter of this.emitters) {
        if (this.elapsed < emitter.delay) continue;
        if (emitter.duration > 0 && this.elapsed > emitter.delay + emitter.duration) continue;
        if (emitter.instantaneous > 0) {
          if (!emitter.emitted) for (let n = 0; n < emitter.instantaneous; n += 1) this.emit(emitter);
          emitter.emitted = true;
          continue;
        }
        emitter.accumulator += emitter.rate * dt;
        while (emitter.accumulator >= 1) {
          emitter.accumulator -= 1;
          this.emit(emitter);
        }
      }
    }
    const moves = this.operators.some((operator) => operator.name === 'movement');
    const rotates = this.operators.some((operator) => operator.name === 'angularmovement');
    for (let index = this.count - 1; index >= 0; index -= 1) {
      this.age[index] += dt;
      const parent = this.parent[index];
      const orphaned = parent >= 0 && this.parentSystem && (parent >= this.parentSystem.count || this.parentSystem.serial[parent] !== this.parentSerial[index]);
      if (this.age[index] >= this.life[index] || orphaned) {
        this.kill(index);
        continue;
      }
      const lifeFraction = this.age[index] / this.life[index];
      let sizeScale = 1;
      let alphaScale = 1;
      for (const operator of this.operators) {
        switch (operator.name) {
          case 'movement': {
            const gravity = vector(operator.gravity, [0, 0, 0]);
            const drag = toNumber(operator.drag, 0);
            this.vx[index] += gravity[0] * dt;
            this.vy[index] += gravity[1] * dt;
            this.vz[index] += gravity[2] * dt;
            const damping = Math.max(0, 1 - (drag * dt));
            this.vx[index] *= damping;
            this.vy[index] *= damping;
            this.vz[index] *= damping;
            break;
          }
          case 'angularmovement': {
            this.angular[index] += scalarAxis(operator.force, 0) * dt;
            this.angular[index] *= Math.max(0, 1 - (toNumber(operator.drag, 0) * dt));
            break;
          }
          case 'alphafade': {
            const fadeIn = toNumber(operator.fadeintime, 0.5);
            const fadeOut = toNumber(operator.fadeouttime, 0.5);
            alphaScale *= (fadeIn > 0 ? Math.min(1, lifeFraction / fadeIn) : 1) * (fadeOut < 1 ? Math.min(1, (1 - lifeFraction) / (1 - fadeOut)) : 1);
            break;
          }
          case 'sizechange':
          case 'alphachange': {
            const t = smooth(toNumber(operator.starttime, 0), toNumber(operator.endtime, 1), lifeFraction);
            const value = toNumber(operator.startvalue, 1) + ((toNumber(operator.endvalue, operator.name === 'sizechange' ? 0 : 1) - toNumber(operator.startvalue, 1)) * t);
            if (operator.name === 'sizechange') sizeScale *= value;
            else alphaScale *= value;
            break;
          }
          case 'oscillatealpha':
          case 'oscillatesize': {
            const frequency = toNumber(operator.frequencymin, 0) + ((toNumber(operator.frequencymax, 10) - toNumber(operator.frequencymin, 0)) * this.seed[index]);
            const scale = toNumber(operator.scalemin, 0) + ((toNumber(operator.scalemax, 1) - toNumber(operator.scalemin, 0)) * this.seed[index]);
            const wave = 0.5 + (0.5 * Math.sin((this.age[index] * frequency * Math.PI * 2) + (this.seed[index] * 6.283)));
            const factor = 1 - (scale * wave);
            if (operator.name === 'oscillatealpha') alphaScale *= factor;
            else sizeScale *= factor;
            break;
          }
          case 'turbulence': {
            const scale = toNumber(operator.scale, 0.005);
            const speedMin = toNumber(operator.speedmin, 100);
            const speed = speedMin + ((toNumber(operator.speedmax, 250) - speedMin) * this.seed[index]);
            const time = this.elapsed * toNumber(operator.timescale, 1);
            const mask = vector(operator.mask, [1, 1, 0]);
            this.vx[index] += valueNoise((this.x[index] * scale) + time, this.y[index] * scale) * speed * mask[0] * dt;
            this.vy[index] += valueNoise(this.x[index] * scale, (this.y[index] * scale) + time + 17.3) * speed * mask[1] * dt;
            this.vz[index] += valueNoise((this.z[index] * scale) + time + 41.7, this.x[index] * scale) * speed * mask[2] * dt;
            break;
          }
          case 'vortex': {
            const distance = Math.hypot(this.x[index], this.y[index]) || 1;
            const inner = toNumber(operator.distanceinner, 500);
            const outer = toNumber(operator.distanceouter, 650);
            const speed = distance <= inner ? toNumber(operator.speedinner, 2500) : distance >= outer ? toNumber(operator.speedouter, 0)
              : toNumber(operator.speedinner, 2500) + ((toNumber(operator.speedouter, 0) - toNumber(operator.speedinner, 2500)) * ((distance - inner) / (outer - inner)));
            this.vx[index] += (-this.y[index] / distance) * speed * dt;
            this.vy[index] += (this.x[index] / distance) * speed * dt;
            break;
          }
          default:
            break;
        }
      }
      if (moves || this.operators.some((operator) => operator.name === 'turbulence' || operator.name === 'vortex')) {
        this.x[index] += this.vx[index] * dt;
        this.z[index] += this.vz[index] * dt;
        this.y[index] += this.vy[index] * dt;
      }
      if (rotates) this.rotation[index] += this.angular[index] * dt;
      this.size[index] = this.baseSize[index] * sizeScale;
      this.alpha[index] = this.baseAlpha[index] * alphaScale;
    }
    for (const child of this.children) child.system.update(dt);
  }

  private frameRect(index: number): [number, number, number, number] {
    const texture = this.texture;
    const frames = texture.frames;
    if (frames.length > 1) {
      const multiplier = toNumber(this.definition.sequencemultiplier, 1);
      const duration = frames.reduce((sum, frame) => sum + Math.max(0.001, frame.frameTime), 0);
      let frameIndex = this.frame[index];
      if (this.definition.animationmode !== 'randomframe') {
        const time = (this.age[index] * multiplier) % duration;
        let elapsed = 0;
        frameIndex = 0;
        for (let n = 0; n < frames.length; n += 1) {
          elapsed += Math.max(0.001, frames[n].frameTime);
          if (time < elapsed) { frameIndex = n; break; }
        }
      }
      const frame = frames[Math.min(frames.length - 1, Math.floor(frameIndex))];
      return [frame.x / texture.width, frame.y / texture.height, frame.width / texture.width, frame.height / texture.height];
    }
    return [0, 0, texture.imageWidth / texture.width, texture.imageHeight / texture.height];
  }

  /** Fills instance data for this system and its children, in local space offsets. */
  private fill(offsetX: number, offsetY: number, offsetZ: number, out: { data: Float32Array; count: number }[]): void {
    let written = 0;
    const data = this.instanceData;
    for (let index = 0; index < this.count; index += 1) {
      let x = this.x[index] + offsetX;
      let y = this.y[index] + offsetY;
      let z = this.z[index] + offsetZ;
      const parent = this.parent[index];
      if (parent >= 0 && this.parentSystem) {
        x += this.parentSystem.x[parent];
        y += this.parentSystem.y[parent];
        z += this.parentSystem.z[parent];
      }
      for (const operator of this.operators) {
        if (operator.name !== 'oscillateposition') continue;
        const mask = vector(operator.mask, [1, 1, 0]);
        const frequency = toNumber(operator.frequencymin, 0) + ((toNumber(operator.frequencymax, 5) - toNumber(operator.frequencymin, 0)) * this.seed[index]);
        const scale = toNumber(operator.scalemin, 0) + ((toNumber(operator.scalemax, 10) - toNumber(operator.scalemin, 0)) * this.seed[index]);
        const phase = (this.age[index] * frequency * Math.PI * 2) + (this.seed[index] * 6.283);
        x += Math.sin(phase) * scale * mask[0];
        y += Math.cos(phase * 0.9) * scale * mask[1];
        z += Math.sin(phase * 1.1) * scale * mask[2];
      }
      const size = this.size[index];
      let width = size;
      let height = size;
      let rotation = this.rotation[index];
      if (this.trailLength > 0) {
        const speed = Math.hypot(this.vx[index], this.vy[index]);
        width = Math.max(size, speed * this.trailLength * 10);
        rotation = Math.atan2(this.vy[index], this.vx[index]);
      }
      const [u, v, uw, vh] = this.frameRect(index);
      const base = written * instanceFloats;
      data[base] = x; data[base + 1] = y;
      data[base + 2] = width; data[base + 3] = height;
      data[base + 4] = this.color[index * 3]; data[base + 5] = this.color[(index * 3) + 1]; data[base + 6] = this.color[(index * 3) + 2];
      data[base + 7] = Math.max(0, Math.min(1, this.alpha[index]));
      data[base + 8] = u; data[base + 9] = v; data[base + 10] = uw; data[base + 11] = vh;
      data[base + 12] = rotation;
      data[base + 13] = z;
      written += 1;
    }
    out.push({ data: data.subarray(0, written * instanceFloats), count: written });
    for (const child of this.children) {
      if (child.mode === 'static') child.system.fill(offsetX + child.offset[0], offsetY + child.offset[1], offsetZ + child.offset[2], out);
      else child.system.fill(offsetX, offsetY, offsetZ, out);
    }
  }

  collect(out: Array<{ system: ParticleSystem; data: Float32Array; count: number }>): void {
    const batches: { data: Float32Array; count: number }[] = [];
    this.fill(0, 0, 0, batches);
    const systems: ParticleSystem[] = [];
    const walk = (system: ParticleSystem): void => { systems.push(system); for (const child of system.children) walk(child.system); };
    walk(this);
    batches.forEach((batch, index) => out.push({ system: systems[index], ...batch }));
  }

  get textureHandle(): WebGLTexture {
    return this.texture.texture;
  }
}

const scalarAxis = (value: unknown, fallback: number): number => {
  if (typeof value === 'string' && /\s/u.test(value.trim())) return toVector(value, [0, 0, fallback])[2];
  return toNumber(value, fallback);
};

/** Instanced sprite drawing shared by every particle system of a scene. */
export class ParticleRenderer {
  private readonly program: GlProgram;
  private readonly billboard: GlProgram;
  private readonly vao: WebGLVertexArrayObject;
  private readonly buffer: WebGLBuffer;
  private capacity = 0;

  constructor(private readonly gl: WebGL2RenderingContext) {
    this.program = compileProgram(gl, particleVertex, particleFragment);
    this.billboard = compileProgram(gl, particleBillboardVertex, particleFragment);
    this.vao = gl.createVertexArray()!;
    gl.bindVertexArray(this.vao);
    const corners = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, corners);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 0, 1, -1, 0, -1, 1, 0, 1, 1, 0]), gl.STATIC_DRAW);
    gl.enableVertexAttribArray(positionAttribute);
    gl.vertexAttribPointer(positionAttribute, 3, gl.FLOAT, false, 0, 0);
    this.buffer = gl.createBuffer()!;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffer);
    const stride = instanceFloats * 4;
    for (const [location, offset] of [[2, 0], [3, 16], [4, 32], [5, 48]] as const) {
      gl.enableVertexAttribArray(location);
      gl.vertexAttribPointer(location, 4, gl.FLOAT, false, stride, offset);
      gl.vertexAttribDivisor(location, 1);
    }
    gl.bindVertexArray(null);
  }

  /** 2D scenes: sprites in the object's plane through a scene-to-clip matrix. */
  draw(system: ParticleSystem, matrix: Mat3, opacity: number): void {
    const { gl } = this;
    gl.useProgram(this.program.program);
    gl.uniformMatrix3fv(this.program.uniforms.get('u_Matrix')!.location, false, matrix);
    gl.uniform1i(this.program.uniforms.get('u_Texture')!.location, 0);
    this.drawBatches(system, opacity);
  }

  /** 3D scenes: billboards facing the camera; depth-tested, never depth-written. */
  draw3d(system: ParticleSystem, model: Mat4, camera: { viewProjection: Mat4; right: Vec3; up: Vec3 }, opacity: number): void {
    const { gl } = this;
    const uniforms = this.billboard.uniforms;
    gl.useProgram(this.billboard.program);
    gl.uniformMatrix4fv(uniforms.get('u_Model')!.location, false, model);
    gl.uniformMatrix4fv(uniforms.get('u_ViewProjection')!.location, false, camera.viewProjection);
    gl.uniform3fv(uniforms.get('u_Right')!.location, camera.right);
    gl.uniform3fv(uniforms.get('u_Up')!.location, camera.up);
    gl.uniform1f(uniforms.get('u_Scale')!.location, Math.hypot(model[0], model[1], model[2]));
    gl.uniform1i(uniforms.get('u_Texture')!.location, 0);
    gl.enable(gl.DEPTH_TEST);
    gl.depthMask(false);
    this.drawBatches(system, opacity);
    gl.depthMask(true);
    gl.disable(gl.DEPTH_TEST);
  }

  private drawBatches(system: ParticleSystem, opacity: number): void {
    const { gl } = this;
    const batches: Array<{ system: ParticleSystem; data: Float32Array; count: number }> = [];
    system.collect(batches);
    gl.bindVertexArray(this.vao);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.buffer);
    for (const batch of batches) {
      if (batch.count === 0) continue;
      if (opacity < 1) for (let index = 0; index < batch.count; index += 1) batch.data[(index * instanceFloats) + 7] *= opacity;
      const bytes = batch.data.byteLength;
      if (bytes > this.capacity) {
        this.capacity = bytes;
        gl.bufferData(gl.ARRAY_BUFFER, this.capacity, gl.DYNAMIC_DRAW);
      }
      gl.bufferSubData(gl.ARRAY_BUFFER, 0, batch.data);
      applyBlend(gl, batch.system.blend);
      gl.bindTexture(gl.TEXTURE_2D, batch.system.textureHandle);
      gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, batch.count);
    }
    gl.bindVertexArray(null);
  }
}
