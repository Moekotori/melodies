export type DxtFormat = 'dxt1' | 'dxt3' | 'dxt5';

// setTimeout works in both the main process and the scene runtime page.
const yieldToEventLoop = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** Block rows decoded between event-loop yields; keeps a 4K texture from stalling the thread. */
const blockRowsPerSlice = 32;

const expand565 = (value: number, target: Uint8Array): void => {
  const r = (value >>> 11) & 31;
  const g = (value >>> 5) & 63;
  const b = value & 31;
  target[0] = (r << 3) | (r >>> 2);
  target[1] = (g << 2) | (g >>> 4);
  target[2] = (b << 3) | (b >>> 2);
};

const decodeColorBlock = (
  source: Uint8Array,
  offset: number,
  palette: Uint8Array,
  allowPunchThrough: boolean,
): number => {
  const c0 = source[offset] | (source[offset + 1] << 8);
  const c1 = source[offset + 2] | (source[offset + 3] << 8);
  const a = palette.subarray(0, 4);
  const b = palette.subarray(4, 8);
  expand565(c0, a);
  expand565(c1, b);
  a[3] = 255;
  b[3] = 255;
  const c2 = palette.subarray(8, 12);
  const c3 = palette.subarray(12, 16);
  if (c0 > c1 || !allowPunchThrough) {
    for (let channel = 0; channel < 3; channel += 1) {
      c2[channel] = ((2 * a[channel]) + b[channel] + 1) / 3;
      c3[channel] = (a[channel] + (2 * b[channel]) + 1) / 3;
    }
    c2[3] = 255;
    c3[3] = 255;
  } else {
    for (let channel = 0; channel < 3; channel += 1) c2[channel] = (a[channel] + b[channel]) >>> 1;
    c2[3] = 255;
    c3[0] = 0; c3[1] = 0; c3[2] = 0; c3[3] = 0;
  }
  return (source[offset + 4] | (source[offset + 5] << 8) | (source[offset + 6] << 16) | (source[offset + 7] << 24)) >>> 0;
};

const decodeDxt5Alpha = (source: Uint8Array, offset: number, alphas: Uint8Array): void => {
  const a0 = source[offset];
  const a1 = source[offset + 1];
  alphas[0] = a0;
  alphas[1] = a1;
  if (a0 > a1) {
    for (let index = 1; index < 7; index += 1) alphas[index + 1] = (((7 - index) * a0) + (index * a1) + 3) / 7;
  } else {
    for (let index = 1; index < 5; index += 1) alphas[index + 1] = (((5 - index) * a0) + (index * a1) + 2) / 5;
    alphas[6] = 0;
    alphas[7] = 255;
  }
};

/** Decodes BC1/BC2/BC3 blocks into tightly packed RGBA of `width` x `height`. */
export const decodeDxt = async (
  source: Uint8Array,
  width: number,
  height: number,
  format: DxtFormat,
): Promise<Uint8Array> => {
  const blockBytes = format === 'dxt1' ? 8 : 16;
  const blocksWide = Math.ceil(width / 4);
  const blocksHigh = Math.ceil(height / 4);
  if (source.length < blocksWide * blocksHigh * blockBytes) throw new Error('dxt-truncated');
  const output = new Uint8Array(width * height * 4);
  const palette = new Uint8Array(16);
  const alphas = new Uint8Array(8);

  for (let blockY = 0; blockY < blocksHigh; blockY += 1) {
    if (blockY > 0 && blockY % blockRowsPerSlice === 0) await yieldToEventLoop();
    for (let blockX = 0; blockX < blocksWide; blockX += 1) {
      const offset = ((blockY * blocksWide) + blockX) * blockBytes;
      const colorOffset = format === 'dxt1' ? offset : offset + 8;
      const indices = decodeColorBlock(source, colorOffset, palette, format === 'dxt1');
      if (format === 'dxt5') decodeDxt5Alpha(source, offset, alphas);
      // DXT5 alpha indices are 48 bits; read them as two 24-bit halves.
      const alphaLow = format === 'dxt5' ? source[offset + 2] | (source[offset + 3] << 8) | (source[offset + 4] << 16) : 0;
      const alphaHigh = format === 'dxt5' ? source[offset + 5] | (source[offset + 6] << 8) | (source[offset + 7] << 16) : 0;

      for (let pixel = 0; pixel < 16; pixel += 1) {
        const x = (blockX * 4) + (pixel & 3);
        const y = (blockY * 4) + (pixel >>> 2);
        if (x >= width || y >= height) continue;
        const colorIndex = (indices >>> (pixel * 2)) & 3;
        const target = ((y * width) + x) * 4;
        output[target] = palette[colorIndex * 4];
        output[target + 1] = palette[(colorIndex * 4) + 1];
        output[target + 2] = palette[(colorIndex * 4) + 2];
        if (format === 'dxt1') {
          output[target + 3] = palette[(colorIndex * 4) + 3];
        } else if (format === 'dxt3') {
          const nibble = (source[offset + (pixel >>> 1)] >>> ((pixel & 1) * 4)) & 15;
          output[target + 3] = nibble * 17;
        } else {
          const alphaIndex = pixel < 8 ? (alphaLow >>> (pixel * 3)) & 7 : (alphaHigh >>> ((pixel - 8) * 3)) & 7;
          output[target + 3] = alphas[alphaIndex];
        }
      }
    }
  }
  return output;
};
