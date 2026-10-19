/**
 * Texture convention for the whole runtime: v = 0 is the TOP row of an image,
 * as in Direct3D (which Wallpaper Engine shaders are written for). Uploaded
 * images are not flipped, and render targets are drawn with a projection that
 * puts image-top at clip y = -1, so render targets store image-top in row 0.
 * Only the final pass to the canvas flips back.
 */

export type GlProgram = {
  program: WebGLProgram;
  uniforms: Map<string, { location: WebGLUniformLocation; type: number; size: number }>;
};

export type RenderTarget = {
  framebuffer: WebGLFramebuffer;
  texture: WebGLTexture;
  width: number;
  height: number;
  /** 3D scenes draw models into a depth-tested scene target. */
  depth: WebGLRenderbuffer | null;
};

export const positionAttribute = 0;
export const texCoordAttribute = 1;

const compileStage = (gl: WebGL2RenderingContext, type: number, source: string): WebGLShader => {
  const shader = gl.createShader(type)!;
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const log = gl.getShaderInfoLog(shader) ?? '';
    gl.deleteShader(shader);
    // Quote the first failing line; translated shaders differ from their source.
    const line = Number(/ERROR: \d+:(\d+)/u.exec(log)?.[1] ?? 0);
    const quoted = line > 0 ? `\n> ${source.split('\n')[line - 1]?.trim() ?? ''}` : '';
    throw new Error(`shader-compile: ${log.split('\n')[0]}${quoted}`);
  }
  return shader;
};

export const compileProgram = (gl: WebGL2RenderingContext, vertexSource: string, fragmentSource: string): GlProgram => {
  const vertex = compileStage(gl, gl.VERTEX_SHADER, vertexSource);
  let fragment: WebGLShader;
  try {
    fragment = compileStage(gl, gl.FRAGMENT_SHADER, fragmentSource);
  } catch (error) {
    gl.deleteShader(vertex);
    throw error;
  }
  const program = gl.createProgram()!;
  gl.attachShader(program, vertex);
  gl.attachShader(program, fragment);
  // One quad layout serves every program, Wallpaper Engine's included.
  gl.bindAttribLocation(program, positionAttribute, 'a_Position');
  gl.bindAttribLocation(program, texCoordAttribute, 'a_TexCoord');
  gl.linkProgram(program);
  gl.deleteShader(vertex);
  gl.deleteShader(fragment);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const log = gl.getProgramInfoLog(program) ?? '';
    gl.deleteProgram(program);
    throw new Error(`shader-link: ${log}`);
  }
  const uniforms: GlProgram['uniforms'] = new Map();
  const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS) as number;
  for (let index = 0; index < count; index += 1) {
    const info = gl.getActiveUniform(program, index);
    if (!info) continue;
    const name = info.name.replace(/\[0\]$/u, '');
    const location = gl.getUniformLocation(program, info.name);
    if (location) uniforms.set(name, { location, type: info.type, size: info.size });
  }
  return { program, uniforms };
};

export const createRenderTarget = (gl: WebGL2RenderingContext, width: number, height: number, withDepth = false): RenderTarget => {
  const texture = gl.createTexture()!;
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  const framebuffer = gl.createFramebuffer()!;
  gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
  gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, texture, 0);
  let depth: WebGLRenderbuffer | null = null;
  if (withDepth) {
    depth = gl.createRenderbuffer()!;
    gl.bindRenderbuffer(gl.RENDERBUFFER, depth);
    gl.renderbufferStorage(gl.RENDERBUFFER, gl.DEPTH_COMPONENT24, width, height);
    gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_ATTACHMENT, gl.RENDERBUFFER, depth);
    gl.bindRenderbuffer(gl.RENDERBUFFER, null);
  }
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  return { framebuffer, texture, width, height, depth };
};

export const deleteRenderTarget = (gl: WebGL2RenderingContext, target: RenderTarget): void => {
  gl.deleteFramebuffer(target.framebuffer);
  gl.deleteTexture(target.texture);
  if (target.depth) gl.deleteRenderbuffer(target.depth);
};

export const bindRenderTarget = (gl: WebGL2RenderingContext, target: RenderTarget | null, width?: number, height?: number): void => {
  gl.bindFramebuffer(gl.FRAMEBUFFER, target?.framebuffer ?? null);
  gl.viewport(0, 0, target?.width ?? width ?? 1, target?.height ?? height ?? 1);
};

/** Unit quad: clip-space corners with texcoord (0,0) at clip (-1,-1). */
export const createQuad = (gl: WebGL2RenderingContext): WebGLVertexArrayObject => {
  const vao = gl.createVertexArray()!;
  gl.bindVertexArray(vao);
  const positions = gl.createBuffer()!;
  gl.bindBuffer(gl.ARRAY_BUFFER, positions);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 0, 1, -1, 0, -1, 1, 0, 1, 1, 0]), gl.STATIC_DRAW);
  gl.enableVertexAttribArray(positionAttribute);
  gl.vertexAttribPointer(positionAttribute, 3, gl.FLOAT, false, 0, 0);
  const texCoords = gl.createBuffer()!;
  gl.bindBuffer(gl.ARRAY_BUFFER, texCoords);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 1, 1]), gl.STATIC_DRAW);
  gl.enableVertexAttribArray(texCoordAttribute);
  gl.vertexAttribPointer(texCoordAttribute, 2, gl.FLOAT, false, 0, 0);
  gl.bindVertexArray(null);
  return vao;
};

export type BlendMode = 'normal' | 'translucent' | 'additive' | 'opaque';

export const blendModeFor = (value: unknown): BlendMode => {
  switch (value) {
    case 'additive':
      return 'additive';
    case 'disabled':
      return 'opaque';
    default:
      return 'translucent';
  }
};

export const applyBlend = (gl: WebGL2RenderingContext, mode: BlendMode): void => {
  if (mode === 'opaque') {
    gl.disable(gl.BLEND);
    return;
  }
  gl.enable(gl.BLEND);
  gl.blendEquation(gl.FUNC_ADD);
  if (mode === 'additive') gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE, gl.ZERO, gl.ONE);
  else gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
};

/** Draws a texture through a 2D transform into the bound target. */
export const texturedQuadVertex = `#version 300 es
in vec3 a_Position;
in vec2 a_TexCoord;
uniform mat3 u_Matrix;
uniform vec4 u_UvRect;
out vec2 v_Uv;
void main() {
  vec3 position = u_Matrix * vec3(a_Position.xy, 1.0);
  gl_Position = vec4(position.xy, 0.0, 1.0);
  v_Uv = u_UvRect.xy + a_TexCoord * u_UvRect.zw;
}
`;

export const texturedQuadFragment = `#version 300 es
precision highp float;
in vec2 v_Uv;
uniform sampler2D u_Texture;
uniform vec4 u_Color;
out vec4 o_Color;
void main() {
  o_Color = texture(u_Texture, v_Uv) * u_Color;
}
`;

/** Matrix mapping the unit quad to clip space; column-major for `uniformMatrix3fv`. */
export type Mat3 = Float32Array;

export const identity3 = (): Mat3 => new Float32Array([1, 0, 0, 0, 1, 0, 0, 0, 1]);

/** Optional output may alias either input, so hot paths can reuse their matrices. */
export const multiply3 = (a: Mat3, b: Mat3, out: Mat3 = new Float32Array(9)): Mat3 => {
  const a0 = a[0]; const a1 = a[1]; const a2 = a[2];
  const a3 = a[3]; const a4 = a[4]; const a5 = a[5];
  const a6 = a[6]; const a7 = a[7]; const a8 = a[8];
  for (let column = 0; column < 3; column += 1) {
    const offset = column * 3;
    const b0 = b[offset]; const b1 = b[offset + 1]; const b2 = b[offset + 2];
    out[offset] = (a0 * b0) + (a3 * b1) + (a6 * b2);
    out[offset + 1] = (a1 * b0) + (a4 * b1) + (a7 * b2);
    out[offset + 2] = (a2 * b0) + (a5 * b1) + (a8 * b2);
  }
  return out;
};

export const translate3 = (x: number, y: number): Mat3 => new Float32Array([1, 0, 0, 0, 1, 0, x, y, 1]);
export const scale3 = (x: number, y: number): Mat3 => new Float32Array([x, 0, 0, 0, y, 0, 0, 0, 1]);
export const rotate3 = (radians: number): Mat3 => {
  const cos = Math.cos(radians);
  const sin = Math.sin(radians);
  return new Float32Array([cos, sin, 0, -sin, cos, 0, 0, 0, 1]);
};
