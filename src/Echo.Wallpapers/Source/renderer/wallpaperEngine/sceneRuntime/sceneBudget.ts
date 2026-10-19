/** One scene's texture/render-target budget. Replacements debit the old allocation; disposal releases it. */
export const installSceneBudget = (gl: WebGL2RenderingContext): void => {
  const allocations = new Map<WebGLTexture, number>();
  const maxBytes = 192 * 1024 * 1024;
  let bytes = 0;
  const reserve = (size: number): void => {
    const texture = gl.getParameter(gl.TEXTURE_BINDING_2D) as WebGLTexture | null;
    if (!texture) return;
    const previous = allocations.get(texture) ?? 0;
    // Include the mip chain even when a material currently uses only its largest level.
    size = Math.ceil(size * 4 / 3);
    if (!Number.isFinite(size) || size < 0 || bytes - previous + size > maxBytes || (!allocations.has(texture) && allocations.size >= 512))
      throw new Error('scene-gpu-budget-exceeded');
    bytes += size - previous; allocations.set(texture, size);
  };
  const upload = gl.texImage2D.bind(gl) as (...args: unknown[]) => void;
  gl.texImage2D = ((...args: unknown[]): void => {
    if (args[1] === 0) {
      if (args.length >= 9) reserve(Number(args[3]) * Number(args[4]) * 4);
      else {
        const source = args[5] as { width?: number; height?: number; videoWidth?: number; videoHeight?: number };
        reserve(Number(source.videoWidth ?? source.width) * Number(source.videoHeight ?? source.height) * 4);
      }
    }
    upload(...args);
  }) as typeof gl.texImage2D;
  const compressed = gl.compressedTexImage2D.bind(gl) as (...args: unknown[]) => void;
  gl.compressedTexImage2D = ((...args: unknown[]): void => {
    if (args[1] === 0) reserve(Number(args[3]) * Number(args[4]) * 4);
    compressed(...args);
  }) as typeof gl.compressedTexImage2D;
  const remove = gl.deleteTexture.bind(gl);
  gl.deleteTexture = (texture): void => {
    if (texture) { bytes -= allocations.get(texture) ?? 0; allocations.delete(texture); }
    remove(texture);
  };
};
