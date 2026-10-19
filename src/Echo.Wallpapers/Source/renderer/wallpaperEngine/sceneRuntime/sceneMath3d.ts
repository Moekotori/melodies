/** Column-major 4x4 matrices, as `uniformMatrix4fv` and the dialect's `mul(v, M)` expect. */
export type Mat4 = Float32Array;
export type Vec3 = [number, number, number];

export const identity4 = (): Mat4 => new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

export const multiply4 = (a: Mat4, b: Mat4): Mat4 => {
  const out = new Float32Array(16);
  for (let column = 0; column < 4; column += 1) {
    for (let row = 0; row < 4; row += 1) {
      out[(column * 4) + row] = (a[row] * b[column * 4]) + (a[4 + row] * b[(column * 4) + 1])
        + (a[8 + row] * b[(column * 4) + 2]) + (a[12 + row] * b[(column * 4) + 3]);
    }
  }
  return out;
};

export const perspective = (fovYRadians: number, aspect: number, near: number, far: number): Mat4 => {
  const f = 1 / Math.tan(fovYRadians / 2);
  const range = 1 / (near - far);
  return new Float32Array([f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * range, -1, 0, 0, 2 * far * near * range, 0]);
};

const subtract = (a: Vec3, b: Vec3): Vec3 => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cross = (a: Vec3, b: Vec3): Vec3 => [(a[1] * b[2]) - (a[2] * b[1]), (a[2] * b[0]) - (a[0] * b[2]), (a[0] * b[1]) - (a[1] * b[0])];
const dot = (a: Vec3, b: Vec3): number => (a[0] * b[0]) + (a[1] * b[1]) + (a[2] * b[2]);
export const normalize = (a: Vec3): Vec3 => {
  const length = Math.hypot(a[0], a[1], a[2]) || 1;
  return [a[0] / length, a[1] / length, a[2] / length];
};

/** Right-handed view matrix looking from `eye` toward `center`. */
export const lookAt = (eye: Vec3, center: Vec3, up: Vec3): Mat4 => {
  const forward = normalize(subtract(center, eye));
  const right = normalize(cross(forward, up));
  const trueUp = cross(right, forward);
  return new Float32Array([
    right[0], trueUp[0], -forward[0], 0,
    right[1], trueUp[1], -forward[1], 0,
    right[2], trueUp[2], -forward[2], 0,
    -dot(right, eye), -dot(trueUp, eye), dot(forward, eye), 1,
  ]);
};

/** Object transform: translate, then rotate Z·Y·X (radians), then scale. */
export const composeTransform = (origin: number[], angles: number[], scale: number[]): Mat4 => {
  const [cx, cy, cz] = angles.map(Math.cos);
  const [sx, sy, sz] = angles.map(Math.sin);
  // Rz * Ry * Rx
  const r00 = cz * cy; const r01 = (cz * sy * sx) - (sz * cx); const r02 = (cz * sy * cx) + (sz * sx);
  const r10 = sz * cy; const r11 = (sz * sy * sx) + (cz * cx); const r12 = (sz * sy * cx) - (cz * sx);
  const r20 = -sy; const r21 = cy * sx; const r22 = cy * cx;
  return new Float32Array([
    r00 * scale[0], r10 * scale[0], r20 * scale[0], 0,
    r01 * scale[1], r11 * scale[1], r21 * scale[1], 0,
    r02 * scale[2], r12 * scale[2], r22 * scale[2], 0,
    origin[0], origin[1], origin[2], 1,
  ]);
};

export const invert4 = (m: Mat4): Mat4 => {
  const a = m;
  const out = new Float32Array(16);
  const b00 = (a[0] * a[5]) - (a[1] * a[4]); const b01 = (a[0] * a[6]) - (a[2] * a[4]);
  const b02 = (a[0] * a[7]) - (a[3] * a[4]); const b03 = (a[1] * a[6]) - (a[2] * a[5]);
  const b04 = (a[1] * a[7]) - (a[3] * a[5]); const b05 = (a[2] * a[7]) - (a[3] * a[6]);
  const b06 = (a[8] * a[13]) - (a[9] * a[12]); const b07 = (a[8] * a[14]) - (a[10] * a[12]);
  const b08 = (a[8] * a[15]) - (a[11] * a[12]); const b09 = (a[9] * a[14]) - (a[10] * a[13]);
  const b10 = (a[9] * a[15]) - (a[11] * a[13]); const b11 = (a[10] * a[15]) - (a[11] * a[14]);
  const determinant = (b00 * b11) - (b01 * b10) + (b02 * b09) + (b03 * b08) - (b04 * b07) + (b05 * b06);
  if (!determinant) return identity4();
  const inv = 1 / determinant;
  out[0] = ((a[5] * b11) - (a[6] * b10) + (a[7] * b09)) * inv;
  out[1] = ((a[2] * b10) - (a[1] * b11) - (a[3] * b09)) * inv;
  out[2] = ((a[13] * b05) - (a[14] * b04) + (a[15] * b03)) * inv;
  out[3] = ((a[10] * b04) - (a[9] * b05) - (a[11] * b03)) * inv;
  out[4] = ((a[6] * b08) - (a[4] * b11) - (a[7] * b07)) * inv;
  out[5] = ((a[0] * b11) - (a[2] * b08) + (a[3] * b07)) * inv;
  out[6] = ((a[14] * b02) - (a[12] * b05) - (a[15] * b01)) * inv;
  out[7] = ((a[8] * b05) - (a[10] * b02) + (a[11] * b01)) * inv;
  out[8] = ((a[4] * b10) - (a[5] * b08) + (a[7] * b06)) * inv;
  out[9] = ((a[1] * b08) - (a[0] * b10) - (a[3] * b06)) * inv;
  out[10] = ((a[12] * b04) - (a[13] * b02) + (a[15] * b00)) * inv;
  out[11] = ((a[9] * b02) - (a[8] * b04) - (a[11] * b00)) * inv;
  out[12] = ((a[5] * b07) - (a[4] * b09) - (a[6] * b06)) * inv;
  out[13] = ((a[0] * b09) - (a[1] * b07) + (a[2] * b06)) * inv;
  out[14] = ((a[13] * b01) - (a[12] * b03) - (a[14] * b00)) * inv;
  out[15] = ((a[8] * b03) - (a[9] * b01) + (a[10] * b00)) * inv;
  return out;
};

/** Inverse-transpose of the upper 3x3, for transforming normals. */
export const normalMatrix = (model: Mat4): Float32Array => {
  const inverse = invert4(model);
  return new Float32Array([inverse[0], inverse[4], inverse[8], inverse[1], inverse[5], inverse[9], inverse[2], inverse[6], inverse[10]]);
};

export const transformPoint = (m: Mat4, point: number[]): Vec3 => [
  (m[0] * point[0]) + (m[4] * point[1]) + (m[8] * point[2]) + m[12],
  (m[1] * point[0]) + (m[5] * point[1]) + (m[9] * point[2]) + m[13],
  (m[2] * point[0]) + (m[6] * point[1]) + (m[10] * point[2]) + m[14],
];
