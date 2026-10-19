import type { SceneAssets } from './sceneAssets';
import { applyBlend, compileProgram, texturedQuadFragment, type BlendMode, type GlProgram, type Mat3 } from './sceneGl';
import { parseModel } from './sceneModel';
import { parsePuppet, PuppetPose, type PuppetLayer } from './scenePuppetModel';
import { isRecord, toBoolean, toNumber } from './sceneValues';

type PuppetMesh = { vao: WebGLVertexArrayObject; indexCount: number };

export type ScenePuppet = { meshes: PuppetMesh[]; program: GlProgram; pose: PuppetPose; layers: PuppetLayer[] };

/** Skinned version of the layer quad: the warped mesh samples the layer image (or its effect result). */
const puppetVertex = (bones: number): string => `#version 300 es
in vec3 a_Position;
in uvec4 a_BlendIndices;
in vec4 a_BlendWeights;
in vec2 a_TexCoord;
uniform mat4 u_Bones[${bones}];
uniform mat3 u_Matrix;
uniform vec4 u_UvRect;
out vec2 v_Uv;
void main() {
  mat4 skin = u_Bones[a_BlendIndices.x] * a_BlendWeights.x + u_Bones[a_BlendIndices.y] * a_BlendWeights.y
    + u_Bones[a_BlendIndices.z] * a_BlendWeights.z + u_Bones[a_BlendIndices.w] * a_BlendWeights.w;
  vec3 position = u_Matrix * vec3((skin * vec4(a_Position, 1.0)).xy, 1.0);
  gl_Position = vec4(position.xy, 0.0, 1.0);
  v_Uv = u_UvRect.xy + a_TexCoord * u_UvRect.zw;
}
`;

/** Puppet-warped image layers: `.mdl` meshes skinned by their bone animations. */
export class ScenePuppetRenderer {
  private readonly programs = new Map<number, GlProgram>();

  constructor(private readonly gl: WebGL2RenderingContext, private readonly assets: SceneAssets) {}

  private program(bones: number): GlProgram {
    let program = this.programs.get(bones);
    if (!program) {
      program = compileProgram(this.gl, puppetVertex(bones), texturedQuadFragment);
      this.programs.set(bones, program);
    }
    return program;
  }

  /** `animationLayers` is the object's `animationlayers` list; layers name animations by id. */
  async load(path: string, animationLayers: unknown[]): Promise<ScenePuppet> {
    const { gl } = this;
    const bytes = await this.assets.bytes(path);
    if (!bytes) throw new Error('puppet-missing');
    const { meshes, end } = parseModel(bytes);
    const skeleton = parsePuppet(bytes, end);
    const program = this.program(Math.max(1, skeleton.bones.length));
    const layers: PuppetLayer[] = [];
    for (const raw of animationLayers.filter(isRecord)) {
      const animation = skeleton.animations.find((candidate) => candidate.id === Number(raw.animation));
      if (!animation) continue;
      layers.push({
        animation,
        rate: toNumber(raw.rate, 1),
        blend: toNumber(raw.blend, 1),
        additive: toBoolean(raw.additive, false),
        visible: toBoolean(raw.visible, true),
        time: 0,
      });
    }
    const gpuMeshes = meshes.map((mesh) => {
      const vao = gl.createVertexArray()!;
      gl.bindVertexArray(vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
      gl.bufferData(gl.ARRAY_BUFFER, mesh.vertices, gl.STATIC_DRAW);
      for (const attribute of mesh.attributes) {
        const location = gl.getAttribLocation(program.program, attribute.name);
        if (location < 0) continue;
        gl.enableVertexAttribArray(location);
        if (attribute.integer) gl.vertexAttribIPointer(location, attribute.size, gl.UNSIGNED_INT, mesh.stride, attribute.offset);
        else gl.vertexAttribPointer(location, attribute.size, gl.FLOAT, false, mesh.stride, attribute.offset);
      }
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, gl.createBuffer());
      gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, mesh.indices, gl.STATIC_DRAW);
      gl.bindVertexArray(null);
      return { vao, indexCount: mesh.indices.length };
    });
    return { meshes: gpuMeshes, program, pose: new PuppetPose(skeleton), layers };
  }

  advance(puppet: ScenePuppet, deltaSeconds: number): void {
    for (const layer of puppet.layers) layer.time += deltaSeconds * layer.rate;
    puppet.pose.update(puppet.layers);
  }

  /** `matrix` maps the mesh's layer-pixel space (centre origin, y up) to clip space. */
  draw(puppet: ScenePuppet, texture: WebGLTexture, matrix: Mat3, uv: number[], color: number[], blend: BlendMode): void {
    const { gl } = this;
    const uniforms = puppet.program.uniforms;
    gl.useProgram(puppet.program.program);
    applyBlend(gl, blend);
    gl.activeTexture(gl.TEXTURE0);
    gl.bindTexture(gl.TEXTURE_2D, texture);
    gl.uniform1i(uniforms.get('u_Texture')!.location, 0);
    gl.uniformMatrix3fv(uniforms.get('u_Matrix')!.location, false, matrix);
    gl.uniform4fv(uniforms.get('u_UvRect')!.location, uv);
    gl.uniform4fv(uniforms.get('u_Color')!.location, color);
    gl.uniformMatrix4fv(uniforms.get('u_Bones')!.location, false, puppet.pose.skin);
    for (const mesh of puppet.meshes) {
      gl.bindVertexArray(mesh.vao);
      gl.drawElements(gl.TRIANGLES, mesh.indexCount, gl.UNSIGNED_SHORT, 0);
    }
    gl.bindVertexArray(null);
  }
}
