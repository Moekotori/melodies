import type { SceneAssets } from './sceneAssets';
import type { CameraFrame } from './sceneCamera';
import { applyBlend, type GlProgram, type RenderTarget } from './sceneGl';
import {
  bindMaterialPass,
  type DrawContext,
  type FrameState,
  type MaterialPass,
  type SceneLights,
  type SceneMaterialCompiler,
} from './sceneMaterials';
import { multiply4, normalMatrix, type Mat4 } from './sceneMath3d';
import { parseModel, type ModelAttribute } from './sceneModel';
import type { ScriptLayerTarget } from './sceneScripts';
import { textureResolution, type SceneTexture } from './sceneTextures';

/** `mirrored` draws are the reflection pass; `reflection` is what `_rt_Reflection` samples. */
export type ModelDrawOptions = { reflection: RenderTarget | null; mirrored: boolean };

type GpuMesh = {
  vertexBuffer: WebGLBuffer;
  indexBuffer: WebGLBuffer;
  indexCount: number;
  stride: number;
  attributes: ModelAttribute[];
  passes: MaterialPass[];
  vaos: Map<GlProgram, WebGLVertexArrayObject>;
};

/** `translucent` models are drawn after opaque ones, farthest first. */
export type SceneModel = { meshes: GpuMesh[]; translucent: boolean };

/** Loads `.mdl` meshes with their materials and draws them in a 3D scene. */
export class SceneModelRenderer {
  constructor(
    private readonly gl: WebGL2RenderingContext,
    private readonly assets: SceneAssets,
    private readonly materials: SceneMaterialCompiler,
  ) {}

  async load(path: string, layer: ScriptLayerTarget | null): Promise<SceneModel> {
    const { gl } = this;
    const bytes = await this.assets.bytes(path);
    if (!bytes) throw new Error('model-missing');
    const meshes: GpuMesh[] = [];
    for (const mesh of parseModel(bytes).meshes) {
      const passes = (await Promise.all(mesh.materials.map((material) => this.materials.loadMaterial(material, layer)))).flat();
      if (passes.length === 0) continue;
      const vertexBuffer = gl.createBuffer()!;
      gl.bindBuffer(gl.ARRAY_BUFFER, vertexBuffer);
      gl.bufferData(gl.ARRAY_BUFFER, mesh.vertices, gl.STATIC_DRAW);
      const indexBuffer = gl.createBuffer()!;
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, indexBuffer);
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, mesh.indices, gl.STATIC_DRAW);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, null);
      meshes.push({ vertexBuffer, indexBuffer, indexCount: mesh.indices.length, stride: mesh.stride, attributes: mesh.attributes, passes, vaos: new Map() });
    }
    return { meshes, translucent: meshes.some((mesh) => mesh.passes.some((pass) => pass.blend !== 'opaque')) };
  }

  /** Vertex layout per program: attributes are matched by the names the shader declares. */
  private vao(mesh: GpuMesh, program: GlProgram): WebGLVertexArrayObject {
    const { gl } = this;
    let vao = mesh.vaos.get(program);
    if (vao) return vao;
    vao = gl.createVertexArray()!;
    gl.bindVertexArray(vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, mesh.vertexBuffer);
    for (const attribute of mesh.attributes) {
      const location = gl.getAttribLocation(program.program, attribute.name);
      if (location < 0) continue;
      gl.enableVertexAttribArray(location);
      if (attribute.integer) gl.vertexAttribIPointer(location, attribute.size, gl.UNSIGNED_INT, mesh.stride, attribute.offset);
      else gl.vertexAttribPointer(location, attribute.size, gl.FLOAT, false, mesh.stride, attribute.offset);
    }
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, mesh.indexBuffer);
    gl.bindVertexArray(null);
    mesh.vaos.set(program, vao);
    return vao;
  }

  draw(
    model: SceneModel,
    world: Mat4,
    camera: CameraFrame,
    lights: SceneLights,
    frame: FrameState,
    target: { width: number; height: number },
    onTexture: (texture: SceneTexture) => void,
    options: ModelDrawOptions,
  ): void {
    const { gl } = this;
    const { reflection } = options;
    const draw: DrawContext = {
      model: world,
      normal: normalMatrix(world),
      view: camera.view,
      projection: camera.projection,
      viewProjection: camera.viewProjection,
      modelViewProjection: multiply4(camera.viewProjection, world),
      eye: camera.eye,
      up: camera.up,
      right: camera.right,
      lights,
    };
    // The image-top-first projection mirrors Y, so front faces wind clockwise on screen;
    // a mirrored model flips them back.
    gl.frontFace(options.mirrored ? gl.CCW : gl.CW);
    for (const mesh of model.meshes) {
      for (const pass of mesh.passes) {
        bindMaterialPass(gl, pass, frame, target, (binding) => {
          if (binding?.kind === 'reflection') {
            const source = reflection ?? { texture: this.materials.textures.black.texture, width: 1, height: 1 };
            return { texture: source.texture, resolution: [source.width, source.height, source.width, source.height] };
          }
          if (binding?.kind !== 'texture') return null;
          onTexture(binding.texture);
          return { texture: binding.texture.texture, resolution: textureResolution(binding.texture) };
        }, draw);
        applyBlend(gl, pass.blend);
        if (pass.depthTest) gl.enable(gl.DEPTH_TEST);
        else gl.disable(gl.DEPTH_TEST);
        gl.depthMask(pass.depthWrite);
        if (pass.cull === 'back') gl.enable(gl.CULL_FACE);
        else gl.disable(gl.CULL_FACE);
        gl.bindVertexArray(this.vao(mesh, pass.program));
        gl.drawElements(gl.TRIANGLES, mesh.indexCount, gl.UNSIGNED_SHORT, 0);
      }
    }
    gl.bindVertexArray(null);
    gl.disable(gl.CULL_FACE);
    gl.disable(gl.DEPTH_TEST);
    gl.depthMask(true);
    gl.frontFace(gl.CCW);
  }
}
