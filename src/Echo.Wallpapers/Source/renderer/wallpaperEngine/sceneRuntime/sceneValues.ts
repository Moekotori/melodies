/** Saved project properties (`project.json` `general.properties`). */
export type UserPropertyMap = Record<string, { type?: string; value?: unknown } | undefined>;

type Keyframe = { frame: number; value: number };

/** Editor keyframe track: per-channel keys (`c0`..`c3`) played at `fps`. */
export type PropertyAnimation = {
  fps: number;
  length: number;
  mode: 'single' | 'loop' | 'mirror';
  channels: Keyframe[][];
};

/**
 * Scene JSON property: a plain value, or `{ value, user?, script?, scriptproperties?, animation? }`
 * where `user` binds a project property and `script` / `animation` drive it per frame.
 */
export type PropertySource = {
  value: unknown;
  user: string | { name: string; condition?: unknown } | null;
  script: string | null;
  scriptProperties: Record<string, unknown> | null;
  animation: PropertyAnimation | null;
};

const readAnimation = (raw: unknown): PropertyAnimation | null => {
  if (!raw || typeof raw !== 'object') return null;
  const record = raw as Record<string, unknown>;
  const options = (record.options && typeof record.options === 'object' ? record.options : {}) as Record<string, unknown>;
  const channels: Keyframe[][] = [];
  for (let index = 0; index < 4; index += 1) {
    const keys = record[`c${index}`];
    if (!Array.isArray(keys)) break;
    channels.push(keys
      .filter((key): key is Record<string, unknown> => Boolean(key) && typeof key === 'object')
      .map((key) => ({ frame: Number(key.frame) || 0, value: Number(key.value) || 0 }))
      .sort((a, b) => a.frame - b.frame));
  }
  if (channels.length === 0 || channels.every((keys) => keys.length === 0)) return null;
  const mode = options.mode === 'loop' || options.mode === 'mirror' ? options.mode : 'single';
  return { fps: Number(options.fps) || 30, length: Math.max(1, Number(options.length) || 1), mode, channels };
};

/**
 * Value of each animated channel at `seconds`. Keys ease in and out; the
 * editor's Bézier handles are not reproduced.
 */
export const evaluateAnimation = (animation: PropertyAnimation, seconds: number): number[] => {
  let frame = seconds * animation.fps;
  if (animation.mode === 'loop') frame %= animation.length;
  else if (animation.mode === 'mirror') {
    const cycle = frame % (animation.length * 2);
    frame = cycle > animation.length ? (animation.length * 2) - cycle : cycle;
  } else frame = Math.min(frame, animation.length);
  return animation.channels.map((keys) => {
    if (keys.length === 0) return 0;
    if (frame <= keys[0].frame) return keys[0].value;
    for (let index = 1; index < keys.length; index += 1) {
      const next = keys[index];
      if (frame > next.frame) continue;
      const previous = keys[index - 1];
      const t = next.frame === previous.frame ? 1 : (frame - previous.frame) / (next.frame - previous.frame);
      return previous.value + ((next.value - previous.value) * t * t * (3 - (2 * t)));
    }
    return keys[keys.length - 1].value;
  });
};

export const readPropertySource = (raw: unknown): PropertySource => {
  if (raw && typeof raw === 'object' && !Array.isArray(raw) && ('value' in raw || 'script' in raw || 'user' in raw || 'animation' in raw)) {
    const record = raw as Record<string, unknown>;
    const user = typeof record.user === 'string'
      ? record.user
      : record.user && typeof record.user === 'object' && typeof (record.user as { name?: unknown }).name === 'string'
        ? record.user as { name: string; condition?: unknown }
        : null;
    return {
      value: record.value,
      user,
      script: typeof record.script === 'string' ? record.script : null,
      scriptProperties: record.scriptproperties && typeof record.scriptproperties === 'object'
        ? record.scriptproperties as Record<string, unknown>
        : null,
      animation: readAnimation(record.animation),
    };
  }
  return { value: raw, user: null, script: null, scriptProperties: null, animation: null };
};

/** Static value of a property after applying its user-property binding. */
export const resolvePropertyValue = (source: PropertySource, userProperties: UserPropertyMap): unknown => {
  if (!source.user) return source.value;
  if (typeof source.user === 'string') {
    const bound = userProperties[source.user];
    return bound && 'value' in bound ? bound.value : source.value;
  }
  const bound = userProperties[source.user.name];
  if (!bound || !('value' in bound)) return source.value;
  // Conditional bindings show the object while a combo matches.
  return source.user.condition === undefined ? bound.value : String(bound.value) === String(source.user.condition);
};

export const toNumber = (value: unknown, fallback: number): number => {
  const number = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : typeof value === 'boolean' ? Number(value) : Number.NaN;
  return Number.isFinite(number) ? number : fallback;
};

/** Parses `"x y z"`, numbers, `{x,y,z}` objects (script Vec3) and arrays. */
export const toVector = (value: unknown, fallback: number[]): number[] => {
  if (typeof value === 'number' && Number.isFinite(value)) return fallback.map(() => value);
  let parts: unknown[] | null = null;
  if (typeof value === 'string') parts = value.trim().split(/\s+/u);
  else if (Array.isArray(value)) parts = value;
  else if (value && typeof value === 'object') {
    const vector = value as Record<string, unknown>;
    parts = [vector.x, vector.y, vector.z, vector.w];
  }
  if (!parts) return fallback;
  return fallback.map((fallbackValue, index) => toNumber(parts![index], fallbackValue));
};

export const toBoolean = (value: unknown, fallback: boolean): boolean => {
  if (typeof value === 'boolean') return value;
  if (typeof value === 'number') return value !== 0;
  if (typeof value === 'string') return value !== '0' && value.toLowerCase() !== 'false';
  return fallback;
};

export const isRecord = (value: unknown): value is Record<string, unknown> =>
  Boolean(value) && typeof value === 'object' && !Array.isArray(value);
