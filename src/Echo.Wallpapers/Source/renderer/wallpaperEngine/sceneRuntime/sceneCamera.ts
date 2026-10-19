import type { SceneAssets } from './sceneAssets';
import { lookAt, multiply4, normalize, perspective, type Mat4, type Vec3 } from './sceneMath3d';
import { isRecord, toNumber, toVector } from './sceneValues';

type Pose = { eye: Vec3; center: Vec3; up: Vec3 };
type PathKey = Pose & { time: number };
type CameraPath = { duration: number; keys: PathKey[] };

export type CameraFrame = {
  view: Mat4;
  projection: Mat4;
  viewProjection: Mat4;
  eye: Vec3;
  up: Vec3;
  right: Vec3;
  /** Brightness multiplier while crossing between camera paths. */
  fade: number;
};

/** Wallpaper Engine's perspective default when a scene does not set `fov`. */
const defaultFovDegrees = 50;
const fadeSeconds = 0.6;

const readPose = (raw: Record<string, unknown>): Pose => ({
  eye: toVector(raw.eye, [0, 0, 0]) as Vec3,
  center: toVector(raw.center, [0, 0, -1]) as Vec3,
  up: toVector(raw.up, [0, 1, 0]) as Vec3,
});

const mix = (a: Vec3, b: Vec3, t: number): Vec3 => [a[0] + ((b[0] - a[0]) * t), a[1] + ((b[1] - a[1]) * t), a[2] + ((b[2] - a[2]) * t)];

/**
 * Perspective camera of a 3D scene: the saved pose, or the camera paths the
 * editor recorded, played back to back and looped.
 */
export class SceneCamera {
  private constructor(
    private readonly pose: Pose,
    private readonly paths: CameraPath[],
    private readonly fovRadians: number,
    private readonly near: number,
    private readonly far: number,
    private readonly fadeBetweenPaths: boolean,
  ) {}

  static async load(scene: Record<string, unknown>, general: Record<string, unknown>, assets: SceneAssets): Promise<SceneCamera> {
    const camera = isRecord(scene.camera) ? scene.camera : {};
    const paths: CameraPath[] = [];
    for (const file of Array.isArray(camera.paths) ? camera.paths : []) {
      if (typeof file !== 'string') continue;
      const document = await assets.json<Record<string, unknown>>(file);
      for (const path of Array.isArray(document?.paths) ? document.paths.filter(isRecord) : []) {
        const keys = (Array.isArray(path.transforms) ? path.transforms.filter(isRecord) : [])
          .map((key) => ({ ...readPose(key), time: toNumber(key.timestamp, 0) }))
          .sort((a, b) => a.time - b.time);
        const duration = toNumber(path.duration, keys[keys.length - 1]?.time ?? 0);
        if (keys.length > 0 && duration > 0) paths.push({ duration, keys });
      }
    }
    return new SceneCamera(
      readPose(camera),
      paths,
      (toNumber(general.fov, defaultFovDegrees) * Math.PI) / 180,
      toNumber(general.nearz, 0.01),
      toNumber(general.farz, 10000),
      general.camerafade === true && paths.length > 1,
    );
  }

  private poseAt(seconds: number): { pose: Pose; fade: number } {
    if (this.paths.length === 0) return { pose: this.pose, fade: 1 };
    const total = this.paths.reduce((sum, path) => sum + path.duration, 0);
    let time = seconds % total;
    let path = this.paths[0];
    for (const candidate of this.paths) {
      path = candidate;
      if (time < candidate.duration) break;
      time -= candidate.duration;
    }
    const keys = path.keys;
    let pose: Pose = keys[keys.length - 1];
    if (time <= keys[0].time) pose = keys[0];
    else {
      for (let index = 1; index < keys.length; index += 1) {
        if (time > keys[index].time) continue;
        const previous = keys[index - 1];
        const next = keys[index];
        const t = next.time === previous.time ? 1 : (time - previous.time) / (next.time - previous.time);
        pose = { eye: mix(previous.eye, next.eye, t), center: mix(previous.center, next.center, t), up: mix(previous.up, next.up, t) };
        break;
      }
    }
    const fade = this.fadeBetweenPaths ? Math.min(1, time / fadeSeconds, (path.duration - time) / fadeSeconds) : 1;
    return { pose, fade: Math.max(0, fade) };
  }

  /**
   * `flipY` maps world-up to image-top-first render targets (the runtime's
   * texture convention), which also reverses triangle winding. `parallax`
   * moves the eye along the camera's right and up axes, in look distances.
   */
  frame(seconds: number, aspect: number, parallax: [number, number]): CameraFrame {
    const { pose, fade } = this.poseAt(seconds);
    let eye = pose.eye;
    if (parallax[0] !== 0 || parallax[1] !== 0) {
      const base = lookAt(pose.eye, pose.center, pose.up);
      const distance = Math.hypot(pose.center[0] - pose.eye[0], pose.center[1] - pose.eye[1], pose.center[2] - pose.eye[2]);
      const [x, y] = [parallax[0] * distance, parallax[1] * distance];
      eye = [eye[0] + (base[0] * x) + (base[1] * y), eye[1] + (base[4] * x) + (base[5] * y), eye[2] + (base[8] * x) + (base[9] * y)];
    }
    const view = lookAt(eye, pose.center, pose.up);
    const flipY = new Float32Array([1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
    const projection = multiply4(flipY, perspective(this.fovRadians, aspect, this.near, this.far));
    return {
      view,
      projection,
      viewProjection: multiply4(projection, view),
      eye,
      right: normalize([view[0], view[4], view[8]]),
      up: normalize([view[1], view[5], view[9]]),
      fade,
    };
  }
}
