import { composeTransform, invert4, multiply4, type Mat4 } from './sceneMath3d';

/** Per-frame bone channels in `MDLA`: position xyz, euler angles xyz (radians), scale xyz. */
const channels = 9;

export type PuppetBone = { parent: number; rest: Mat4 };

export type PuppetAnimation = {
  id: number;
  name: string;
  /** `loop`, `mirror` (ping-pong) or `single`. */
  mode: string;
  fps: number;
  /** Frames per cycle; tracks hold one more frame for the end pose. */
  length: number;
  /** One track per bone, in bone order, `channels` floats per frame. */
  tracks: Float32Array[];
};

export type PuppetSkeleton = { bones: PuppetBone[]; animations: PuppetAnimation[] };

/** An object's `animationlayers` entry bound to its animation. */
export type PuppetLayer = { animation: PuppetAnimation; rate: number; blend: number; additive: boolean; visible: boolean; time: number };

const findMagic = (bytes: Uint8Array, magic: string, from: number): number => {
  const codes = [...magic].map((character) => character.charCodeAt(0));
  for (let position = from; position + codes.length <= bytes.length; position += 1) {
    if (codes.every((code, index) => bytes[position + index] === code)) return position;
  }
  return -1;
};

/**
 * Reads the `MDLS` skeleton and `MDLA` animations that follow a puppet's
 * meshes. Bone matrices are local to the parent bone, in the mesh's
 * layer-pixel space (origin at the layer centre, y up).
 */
export const parsePuppet = (bytes: Uint8Array, meshEnd: number): PuppetSkeleton => {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let position = findMagic(bytes, 'MDLS', meshEnd);
  if (position < 0) throw new Error('puppet-skeleton-missing');
  const int32 = (): number => { const value = view.getInt32(position, true); position += 4; return value; };
  const uint16 = (): number => { const value = view.getUint16(position, true); position += 2; return value; };
  const float32 = (): number => { const value = view.getFloat32(position, true); position += 4; return value; };
  const cString = (): string => {
    const end = bytes.indexOf(0, position);
    const value = new TextDecoder().decode(bytes.subarray(position, end));
    position = end + 1;
    return value;
  };
  const floats = (count: number): Float32Array => {
    const values = new Float32Array(bytes.slice(position, position + (count * 4)).buffer);
    position += count * 4;
    return values;
  };

  position += 9;
  const skeletonEnd = int32();
  const boneCount = uint16();
  uint16();
  const bones: PuppetBone[] = [];
  for (let index = 0; index < boneCount; index += 1) {
    cString();
    int32();
    const parent = int32();
    const matrixBytes = int32();
    if (matrixBytes !== 64) throw new Error('puppet-bone-invalid');
    bones.push({ parent, rest: floats(16) });
    cString();
  }

  const animations: PuppetAnimation[] = [];
  position = findMagic(bytes, 'MDLA', skeletonEnd);
  if (position < 0) return { bones, animations };
  const version = Number(new TextDecoder().decode(bytes.subarray(position + 4, position + 8)));
  position += 9;
  const animationsEnd = int32();
  const animationCount = int32();
  for (let index = 0; index < animationCount && position < animationsEnd; index += 1) {
    let id = int32();
    while (id === 0 && position < animationsEnd) id = int32();
    int32();
    const name = cString();
    const mode = cString();
    const fps = float32();
    const length = int32();
    int32();
    const trackCount = int32();
    const tracks: Float32Array[] = [];
    for (let track = 0; track < trackCount; track += 1) {
      int32();
      tracks.push(floats(int32() / 4));
    }
    if (version > 1) {
      position += 1;
      const extras = int32();
      for (let extra = 0; extra < extras; extra += 1) {
        float32();
        cString();
      }
    }
    int32();
    animations.push({ id, name, mode, fps, length, tracks });
  }
  return { bones, animations };
};

/** Frame index (fractional) of an animation after `seconds`. */
export const puppetFrame = (animation: PuppetAnimation, seconds: number): number => {
  const length = Math.max(1, animation.length);
  const frame = Math.max(0, seconds * animation.fps);
  if (animation.mode === 'single') return Math.min(frame, length);
  if (animation.mode === 'mirror') {
    const cycle = frame % (2 * length);
    return cycle > length ? (2 * length) - cycle : cycle;
  }
  return frame % length;
};

const sampleTrack = (track: Float32Array, frame: number, out: Float32Array): void => {
  const last = (track.length / channels) - 1;
  const from = Math.min(last, Math.floor(frame));
  const to = Math.min(last, from + 1);
  const t = frame - Math.floor(frame);
  for (let channel = 0; channel < channels; channel += 1) {
    const a = track[(from * channels) + channel];
    out[channel] = a + ((track[(to * channels) + channel] - a) * t);
  }
};

const decompose = (matrix: Mat4): Float32Array => new Float32Array([
  matrix[12], matrix[13], matrix[14],
  0, 0, Math.atan2(matrix[1], matrix[0]),
  Math.hypot(matrix[0], matrix[1], matrix[2]), Math.hypot(matrix[4], matrix[5], matrix[6]), Math.hypot(matrix[8], matrix[9], matrix[10]),
]);

/**
 * Skinning matrices for a puppet: each bone's animated world transform times
 * the inverse of its rest world transform, packed for `uniformMatrix4fv`.
 */
export class PuppetPose {
  readonly skin: Float32Array;
  private readonly restLocals: Float32Array[];
  private readonly inverseRest: Mat4[];
  private readonly worlds: Mat4[];
  private readonly local = new Float32Array(channels);
  private readonly sample = new Float32Array(channels);
  private readonly base = new Float32Array(channels);

  constructor(private readonly skeleton: PuppetSkeleton) {
    const { bones } = skeleton;
    this.skin = new Float32Array(Math.max(1, bones.length) * 16);
    this.restLocals = bones.map((bone) => decompose(bone.rest));
    this.worlds = [];
    for (const [index, bone] of bones.entries()) {
      this.worlds[index] = bone.parent >= 0 && bone.parent < index ? multiply4(this.worlds[bone.parent], bone.rest) : bone.rest;
    }
    this.inverseRest = this.worlds.map(invert4);
    this.update([]);
  }

  update(layers: PuppetLayer[]): Float32Array {
    const { local, sample, base } = this;
    for (const [index, bone] of this.skeleton.bones.entries()) {
      local.set(this.restLocals[index]);
      for (const layer of layers) {
        const track = layer.animation.tracks[index];
        if (!layer.visible || layer.blend <= 0 || !track) continue;
        sampleTrack(track, puppetFrame(layer.animation, layer.time), sample);
        if (layer.additive) {
          sampleTrack(track, 0, base);
          for (let channel = 0; channel < channels; channel += 1) local[channel] += (sample[channel] - base[channel]) * layer.blend;
        } else {
          for (let channel = 0; channel < channels; channel += 1) local[channel] += (sample[channel] - local[channel]) * layer.blend;
        }
      }
      const matrix = composeTransform([local[0], local[1], local[2]], [local[3], local[4], local[5]], [local[6], local[7], local[8]]);
      this.worlds[index] = bone.parent >= 0 && bone.parent < index ? multiply4(this.worlds[bone.parent], matrix) : matrix;
      this.skin.set(multiply4(this.worlds[index], this.inverseRest[index]), index * 16);
    }
    return this.skin;
  }
}
