import {
  readSceneTextureHeader,
  readSceneTextureMip,
  sceneTextureFlagClampUv,
  sceneTextureFlagNoInterpolation,
  sceneTextureMipToRgba,
  type SceneTextureFrame,
  type SceneTextureHeader,
} from '../../../shared/wallpaper/wallpaperEngine/sceneTexture';
import type { SceneAssets } from './sceneAssets';

export type SceneTexture = {
  texture: WebGLTexture;
  /** Uploaded texel size (Wallpaper Engine's padded "texture" size). */
  width: number;
  height: number;
  /** Meaningful image area inside the upload, from the top-left corner. */
  imageWidth: number;
  imageHeight: number;
  /** Sprite-sheet frames in uploaded texels. */
  frames: SceneTextureFrame[];
  video: HTMLVideoElement | null;
};

/** `g_TextureNResolution`: texture size in xy, image size in zw. */
export const textureResolution = (texture: SceneTexture): [number, number, number, number] =>
  [texture.width, texture.height, texture.imageWidth, texture.imageHeight];

const headerBytes = 64 * 1024;
const maxUploadEdge = 4096;

type S3tc = { COMPRESSED_RGBA_S3TC_DXT1_EXT: number; COMPRESSED_RGBA_S3TC_DXT3_EXT: number; COMPRESSED_RGBA_S3TC_DXT5_EXT: number };

/** Loads `.tex` textures by Wallpaper Engine name (`materials/<name>.tex`) and owns their GL objects. */
export class SceneTextureCache {
  private readonly textures = new Map<string, Promise<SceneTexture | null>>();
  /** Whether the video's placeholder has been replaced with full-size storage. */
  private readonly videos = new Map<SceneTexture, boolean>();
  /** Videos drawn since the last `updateVideos`; only these keep decoding. */
  private readonly usedVideos = new Set<SceneTexture>();
  private videosPaused = false;
  private readonly s3tc: S3tc | null;
  readonly white: SceneTexture;
  readonly black: SceneTexture;

  constructor(private readonly gl: WebGL2RenderingContext, private readonly assets: SceneAssets) {
    this.s3tc = gl.getExtension('WEBGL_compressed_texture_s3tc') as S3tc | null;
    this.white = this.solid([255, 255, 255, 255]);
    this.black = this.solid([0, 0, 0, 255]);
  }

  private solid(color: number[]): SceneTexture {
    const { gl } = this;
    const texture = gl.createTexture()!;
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array(color));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    return { texture, width: 1, height: 1, imageWidth: 1, imageHeight: 1, frames: [], video: null };
  }

  load(name: string): Promise<SceneTexture | null> {
    const normalized = name.replace(/\\/gu, '/').replace(/\.tex$/iu, '');
    let cached = this.textures.get(normalized);
    if (!cached) {
      if (this.textures.size >= 256) throw new Error('scene-texture-count-budget');
      cached = this.loadTexture(normalized).catch((error: unknown) => {
        console.warn(`[scene] texture ${normalized} failed`, error);
        return null;
      });
      this.textures.set(normalized, cached);
    }
    return cached;
  }

  private async loadTexture(name: string): Promise<SceneTexture | null> {
    const path = `materials/${name}.tex`;
    const size = await this.assets.size(path);
    if (!size) return null;
    const head = await this.assets.bytes(path, { start: 0, length: Math.min(size, headerBytes) });
    if (!head) return null;
    const read = async (start: number, length: number): Promise<Uint8Array> => {
      if (start + length <= head.length) return head.subarray(start, start + length);
      const bytes = await this.assets.bytes(path, { start, length });
      if (!bytes) throw new Error('scene-texture-read-failed');
      return bytes;
    };
    const header = await readSceneTextureHeader(read, size);
    if (header.isVideo) return this.loadVideo(path, header);
    return this.upload(header, read);
  }

  private configure(header: SceneTextureHeader, mipmaps: boolean): void {
    const { gl } = this;
    const nearest = (header.flags & sceneTextureFlagNoInterpolation) !== 0;
    const wrap = (header.flags & sceneTextureFlagClampUv) !== 0 ? gl.CLAMP_TO_EDGE : gl.REPEAT;
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, nearest ? gl.NEAREST : gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, nearest ? gl.NEAREST : mipmaps ? gl.LINEAR_MIPMAP_LINEAR : gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, wrap);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, wrap);
  }

  /**
   * Every await happens before the texture is bound: other textures load
   * concurrently, and an upload after an await would land in whatever texture
   * they left bound.
   */
  private async upload(header: SceneTextureHeader, read: (start: number, length: number) => Promise<Uint8Array>): Promise<SceneTexture> {
    const { gl } = this;
    const mip = await readSceneTextureMip(read, header);
    const imageWidth = Math.min(mip.width, header.imageWidth || mip.width);
    const imageHeight = Math.min(mip.height, header.imageHeight || mip.height);

    if (mip.kind === 'encoded') {
      const blob = new Blob([mip.bytes as BlobPart]);
      const edge = Math.max(mip.width, mip.height);
      const factor = edge > maxUploadEdge ? maxUploadEdge / edge : 1;
      // Normal maps and masks are data: no colour management, no premultiplication.
      const bitmap = await createImageBitmap(blob, {
        colorSpaceConversion: 'none',
        premultiplyAlpha: 'none',
        ...(factor < 1 ? { resizeWidth: Math.round(mip.width * factor), resizeHeight: Math.round(mip.height * factor), resizeQuality: 'high' } : {}),
      });
      const texture = gl.createTexture()!;
      gl.bindTexture(gl.TEXTURE_2D, texture);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, bitmap);
      bitmap.close();
      gl.generateMipmap(gl.TEXTURE_2D);
      this.configure(header, true);
      // Encoded payloads are already cropped to the image; frames scale with the resize.
      return {
        texture,
        width: Math.round(mip.width * factor),
        height: Math.round(mip.height * factor),
        imageWidth: Math.round(imageWidth * factor),
        imageHeight: Math.round(imageHeight * factor),
        frames: header.frames.map((frame) => ({ ...frame, x: frame.x * factor, y: frame.y * factor, width: frame.width * factor, height: frame.height * factor })),
        video: null,
      };
    }

    const compressed = mip.kind === 'dxt1' || mip.kind === 'dxt3' || mip.kind === 'dxt5';
    const gpuCompressed = compressed && this.s3tc !== null && mip.width % 4 === 0 && mip.height % 4 === 0;
    const pixels = gpuCompressed ? null : await sceneTextureMipToRgba(mip);
    const texture = gl.createTexture()!;
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
    if (!pixels) {
      const s3tc = this.s3tc!;
      // DXT1 keeps its 1-bit alpha.
      const format = mip.kind === 'dxt1'
        ? s3tc.COMPRESSED_RGBA_S3TC_DXT1_EXT
        : mip.kind === 'dxt3' ? s3tc.COMPRESSED_RGBA_S3TC_DXT3_EXT : s3tc.COMPRESSED_RGBA_S3TC_DXT5_EXT;
      gl.compressedTexImage2D(gl.TEXTURE_2D, 0, format, mip.width, mip.height, 0, mip.bytes);
      this.configure(header, false);
    } else {
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, mip.width, mip.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
      gl.generateMipmap(gl.TEXTURE_2D);
      this.configure(header, true);
    }
    return { texture, width: mip.width, height: mip.height, imageWidth, imageHeight, frames: header.frames, video: null };
  }

  private loadVideo(path: string, header: SceneTextureHeader): SceneTexture {
    const { gl } = this;
    const video = document.createElement('video');
    video.src = this.assets.videoUrl(path);
    video.muted = true;
    video.loop = true;
    video.playsInline = true;
    // Playback starts once a visible layer draws it (see updateVideos).
    video.preload = 'metadata';
    const texture = gl.createTexture()!;
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([0, 0, 0, 0]));
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    const entry: SceneTexture = {
      texture,
      width: header.imageWidth,
      height: header.imageHeight,
      imageWidth: header.imageWidth,
      imageHeight: header.imageHeight,
      frames: [],
      video,
    };
    this.videos.set(entry, false);
    return entry;
  }

  /** Marks a video texture as drawn this frame. */
  useVideo(texture: SceneTexture): void {
    if (texture.video) this.usedVideos.add(texture);
  }

  /**
   * Plays and uploads the videos drawn last frame and pauses the rest, so
   * hidden variants (e.g. day/night layers) cost no decoding or upload.
   */
  updateVideos(): void {
    const { gl } = this;
    for (const [entry, uploaded] of this.videos) {
      const video = entry.video!;
      if (!this.usedVideos.has(entry) || this.videosPaused) {
        if (!video.paused) video.pause();
        continue;
      }
      if (video.paused) void video.play().catch(() => undefined);
      if (video.readyState < video.HAVE_CURRENT_DATA) continue;
      gl.bindTexture(gl.TEXTURE_2D, entry.texture);
      if (!uploaded || entry.width !== video.videoWidth || entry.height !== video.videoHeight) {
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, gl.RGBA, gl.UNSIGNED_BYTE, video);
      } else {
        gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, gl.RGBA, gl.UNSIGNED_BYTE, video);
      }
      this.videos.set(entry, true);
      entry.width = video.videoWidth;
      entry.height = video.videoHeight;
      entry.imageWidth = video.videoWidth;
      entry.imageHeight = video.videoHeight;
    }
    this.usedVideos.clear();
  }

  setVideosPaused(paused: boolean): void {
    this.videosPaused = paused;
    if (paused) for (const entry of this.videos.keys()) entry.video!.pause();
  }
}
