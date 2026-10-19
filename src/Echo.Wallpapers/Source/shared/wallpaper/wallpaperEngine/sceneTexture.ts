import { decodeDxt } from './dxtDecoder';
import { decompressLz4Block } from './lz4Block';

/** Reads `length` bytes at `start` within one texture entry. */
export type TextureByteReader = (start: number, length: number) => Promise<Uint8Array>;

export type SceneTextureFrame = {
  imageId: number;
  frameTime: number;
  x: number;
  y: number;
  width: number;
  height: number;
};

export type SceneTextureHeader = {
  format: number;
  flags: number;
  textureWidth: number;
  textureHeight: number;
  imageWidth: number;
  imageHeight: number;
  /** Largest mipmap of the first image; the only level the renderers upload. */
  mip: { width: number; height: number; lz4: boolean; decompressedSize: number; start: number; length: number };
  freeImageFormat: number;
  isVideo: boolean;
  /** Sprite-sheet animation frames (`flags & 4`), in mip pixels. */
  frames: SceneTextureFrame[];
  firstFrame: { x: number; y: number; width: number; height: number } | null;
};

export type SceneTextureCrop = { width: number; height: number; left: number; top: number };

/** Mip payload ready for upload: an encoded image, or pixels in a GPU-known layout. */
export type SceneTextureMip =
  | { kind: 'encoded'; bytes: Uint8Array; width: number; height: number }
  | { kind: 'rgba8' | 'dxt1' | 'dxt3' | 'dxt5' | 'r8' | 'rg88'; bytes: Uint8Array; width: number; height: number };

export type DecodedSceneTexture =
  | { kind: 'encoded'; bytes: Uint8Array; crop: SceneTextureCrop }
  | { kind: 'raw'; rgba: Uint8Array; width: number; height: number; crop: SceneTextureCrop };

const textureFormats = { rgba8888: 0, dxt5: 4, dxt3: 6, dxt1: 7, rg88: 8, r8: 9 } as const;
export const sceneTextureFlagNoInterpolation = 1;
export const sceneTextureFlagClampUv = 2;
const flagAnimated = 4;
const flagVideo = 32;
/** Upper bound for one decoded mip (8192 x 4096 RGBA). */
const maxDecodedBytes = 8192 * 4096 * 4;
const maxMipBytes = 256 * 1024 * 1024;
const maxImages = 4096;
const maxMips = 32;
const maxFrames = 4096;

const view = (bytes: Uint8Array): DataView => new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
const readMagic = (bytes: Uint8Array, offset: number): string =>
  String.fromCharCode(...bytes.subarray(offset, offset + 8));

export const readSceneTextureHeader = async (read: TextureByteReader, entrySize: number): Promise<SceneTextureHeader> => {
  const head = await read(0, Math.min(entrySize, 128));
  const data = view(head);
  if (head.length < 64 || readMagic(head, 0) !== 'TEXV0005' || !readMagic(head, 9).startsWith('TEXI')) {
    throw new Error('scene-tex-not-texture');
  }
  const format = data.getInt32(18, true);
  const flags = data.getInt32(22, true);
  const textureWidth = data.getInt32(26, true);
  const textureHeight = data.getInt32(30, true);
  const imageWidth = data.getInt32(34, true);
  const imageHeight = data.getInt32(38, true);
  const container = /^TEXB000(\d)$/u.exec(readMagic(head, 46));
  if (!container) throw new Error('scene-tex-container-unknown');
  const containerVersion = Number(container[1]);
  let position = 55;
  const imageCount = data.getInt32(position, true); position += 4;
  const freeImageFormat = containerVersion >= 3 ? data.getInt32(position, true) : -1;
  if (containerVersion >= 3) position += 4;
  if (containerVersion >= 4) position += 4;
  if (imageCount < 1 || imageCount > maxImages) throw new Error('scene-tex-image-count-invalid');

  const mipHeaderBytes = containerVersion >= 2 ? 20 : 12;
  const readMip = async (offset: number): Promise<SceneTextureHeader['mip']> => {
    const bytes = view(await read(offset, mipHeaderBytes));
    const width = bytes.getInt32(0, true);
    const height = bytes.getInt32(4, true);
    const lz4 = containerVersion >= 2 && bytes.getInt32(8, true) === 1;
    const decompressedSize = containerVersion >= 2 ? bytes.getInt32(12, true) : 0;
    const length = bytes.getInt32(mipHeaderBytes - 4, true);
    const start = offset + mipHeaderBytes;
    if (width < 1 || height < 1 || length < 0 || start + length > entrySize) throw new Error('scene-tex-mip-invalid');
    return { width, height, lz4, decompressedSize, start, length };
  };

  let firstMip: SceneTextureHeader['mip'] | null = null;
  let cursor = position;
  for (let image = 0; image < imageCount; image += 1) {
    const mipCount = view(await read(cursor, 4)).getInt32(0, true);
    cursor += 4;
    if (mipCount < 1 || mipCount > maxMips) throw new Error('scene-tex-mip-count-invalid');
    for (let level = 0; level < mipCount; level += 1) {
      const mip = await readMip(cursor);
      firstMip ??= mip;
      cursor = mip.start + mip.length;
    }
    // Only sprite sheets need the frame table that follows every image.
    if ((flags & flagAnimated) === 0) break;
  }
  if (!firstMip) throw new Error('scene-tex-empty');

  const frames: SceneTextureFrame[] = [];
  if ((flags & flagAnimated) !== 0 && cursor + 13 <= entrySize) {
    const framesHead = await read(cursor, Math.min(entrySize - cursor, 21));
    const framesMagic = /^TEXS000(\d)$/u.exec(readMagic(framesHead, 0));
    if (framesMagic) {
      const frameCount = Math.min(maxFrames, view(framesHead).getInt32(9, true));
      const tableStart = cursor + 13 + (Number(framesMagic[1]) >= 3 ? 8 : 0);
      const tableBytes = Math.min(Math.max(0, frameCount) * 32, entrySize - tableStart);
      const table = view(await read(tableStart, tableBytes));
      for (let offset = 0; offset + 32 <= tableBytes; offset += 32) {
        // imageId, frametime, x, y, width, widthY, heightX, height
        frames.push({
          imageId: table.getInt32(offset, true),
          frameTime: table.getFloat32(offset + 4, true),
          x: table.getFloat32(offset + 8, true),
          y: table.getFloat32(offset + 12, true),
          width: table.getFloat32(offset + 16, true),
          height: table.getFloat32(offset + 28, true),
        });
      }
    }
  }
  const first = frames.find((frame) => frame.width >= 1 && frame.height >= 1);

  return {
    format,
    flags,
    textureWidth,
    textureHeight,
    imageWidth,
    imageHeight,
    mip: firstMip,
    freeImageFormat,
    isVideo: (flags & flagVideo) !== 0,
    frames,
    firstFrame: first
      ? { x: Math.round(first.x), y: Math.round(first.y), width: Math.round(first.width), height: Math.round(first.height) }
      : null,
  };
};

/** Reads and LZ4-expands the largest mip without converting its pixel layout. */
export const readSceneTextureMip = async (read: TextureByteReader, header: SceneTextureHeader): Promise<SceneTextureMip> => {
  if (header.isVideo) throw new Error('scene-tex-is-video');
  const { mip } = header;
  if (mip.length > maxMipBytes) throw new Error('scene-tex-too-large');
  let bytes = await read(mip.start, mip.length);
  if (mip.lz4) {
    if (mip.decompressedSize < 1 || mip.decompressedSize > maxDecodedBytes) throw new Error('scene-tex-too-large');
    bytes = decompressLz4Block(bytes, mip.decompressedSize);
  }
  const size = { width: mip.width, height: mip.height };
  // FreeImage-encoded payload (PNG/JPEG/...).
  if (header.freeImageFormat !== -1) return { kind: 'encoded', bytes, ...size };
  const pixels = mip.width * mip.height;
  if (pixels * 4 > maxDecodedBytes) throw new Error('scene-tex-too-large');
  switch (header.format) {
    case textureFormats.rgba8888:
      if (bytes.length < pixels * 4) throw new Error('scene-tex-truncated');
      return { kind: 'rgba8', bytes: bytes.subarray(0, pixels * 4), ...size };
    case textureFormats.dxt5:
      return { kind: 'dxt5', bytes, ...size };
    case textureFormats.dxt3:
      return { kind: 'dxt3', bytes, ...size };
    case textureFormats.dxt1:
      return { kind: 'dxt1', bytes, ...size };
    case textureFormats.rg88:
      if (bytes.length < pixels * 2) throw new Error('scene-tex-truncated');
      return { kind: 'rg88', bytes, ...size };
    case textureFormats.r8:
      if (bytes.length < pixels) throw new Error('scene-tex-truncated');
      return { kind: 'r8', bytes, ...size };
    default:
      throw new Error('scene-tex-format-unsupported');
  }
};

/** Single-channel masks become grey RGB; two-channel maps keep G as alpha. */
export const expandSceneTextureChannels = (source: Uint8Array, pixels: number, channels: 1 | 2): Uint8Array => {
  const output = new Uint8Array(pixels * 4);
  for (let index = 0; index < pixels; index += 1) {
    const value = source[index * channels];
    output[index * 4] = value;
    output[(index * 4) + 1] = value;
    output[(index * 4) + 2] = value;
    output[(index * 4) + 3] = channels === 2 ? source[(index * 2) + 1] : 255;
  }
  return output;
};

/** Converts any non-encoded mip layout to tightly packed RGBA on the CPU. */
export const sceneTextureMipToRgba = async (mip: Exclude<SceneTextureMip, { kind: 'encoded' }>): Promise<Uint8Array> => {
  const pixels = mip.width * mip.height;
  switch (mip.kind) {
    case 'rgba8':
      return mip.bytes;
    case 'dxt1':
    case 'dxt3':
    case 'dxt5':
      return decodeDxt(mip.bytes, mip.width, mip.height, mip.kind);
    case 'rg88':
      return expandSceneTextureChannels(mip.bytes, pixels, 2);
    case 'r8':
      return expandSceneTextureChannels(mip.bytes, pixels, 1);
    default:
      throw new Error('scene-tex-format-unsupported');
  }
};

export const sceneTextureCrop = (header: SceneTextureHeader, width: number, height: number): SceneTextureCrop => {
  if (header.firstFrame) {
    const left = Math.min(Math.max(0, header.firstFrame.x), width - 1);
    const top = Math.min(Math.max(0, header.firstFrame.y), height - 1);
    return { left, top, width: Math.min(header.firstFrame.width, width - left), height: Math.min(header.firstFrame.height, height - top) };
  }
  return {
    left: 0,
    top: 0,
    width: Math.min(width, header.imageWidth > 0 ? header.imageWidth : width),
    height: Math.min(height, header.imageHeight > 0 ? header.imageHeight : height),
  };
};

/** Decodes the still image of a non-video texture. Video textures are copied, never decoded. */
export const decodeSceneTexture = async (read: TextureByteReader, header: SceneTextureHeader): Promise<DecodedSceneTexture> => {
  const mip = await readSceneTextureMip(read, header);
  const crop = sceneTextureCrop(header, mip.width, mip.height);
  if (mip.kind === 'encoded') return { kind: 'encoded', bytes: mip.bytes, crop };
  return { kind: 'raw', rgba: await sceneTextureMipToRgba(mip), width: mip.width, height: mip.height, crop };
};
