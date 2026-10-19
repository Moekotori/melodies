import type { SceneAssets } from './sceneAssets';
import { loadShaderHeaders } from './sceneMaterials';
import { compileProgram, texturedQuadVertex, type GlProgram } from './sceneGl';
import { inlineShaderIncludes, translateWeShader } from './shaderTranslator';

/**
 * Layer `colorBlendMode`: blends the layer over a snapshot of what is drawn
 * beneath it with Wallpaper Engine's own `ApplyBlending` from its installed
 * `common_blending.h`. Written in its dialect so the translator handles it.
 */
const blendFragmentSource = `#include "common_blending.h"
uniform sampler2D g_Texture0;
uniform sampler2D g_Texture1;
uniform vec4 u_Color;
uniform vec2 u_TargetSize;
varying vec2 v_Uv;
void main() {
  vec4 layer = texSample2D(g_Texture0, v_Uv) * u_Color;
  vec3 background = texSample2D(g_Texture1, gl_FragCoord.xy / u_TargetSize).rgb;
  gl_FragColor = vec4(ApplyBlending(BLENDMODE, background, layer.rgb, layer.a), 1.0);
}
`;

export class SceneBlendModes {
  private readonly programs = new Map<number, GlProgram | null>();

  private constructor(private readonly gl: WebGL2RenderingContext, private readonly fragment: string | null) {}

  static async load(gl: WebGL2RenderingContext, assets: SceneAssets): Promise<SceneBlendModes> {
    const headers = new Map<string, string>();
    await loadShaderHeaders(assets, blendFragmentSource, headers);
    const available = Boolean(headers.get('common_blending.h'));
    return new SceneBlendModes(gl, available ? inlineShaderIncludes(blendFragmentSource, headers) : null);
  }

  /** Null when Wallpaper Engine's blending header is unavailable or the mode fails to compile. */
  program(mode: number): GlProgram | null {
    if (this.programs.has(mode)) return this.programs.get(mode)!;
    let program: GlProgram | null = null;
    if (this.fragment) {
      try {
        program = compileProgram(this.gl, texturedQuadVertex, translateWeShader(this.fragment, 'fragment', { BLENDMODE: mode }));
      } catch (error) {
        console.warn(`[scene] blend mode ${mode} skipped`, error);
      }
    }
    this.programs.set(mode, program);
    return program;
  }
}
