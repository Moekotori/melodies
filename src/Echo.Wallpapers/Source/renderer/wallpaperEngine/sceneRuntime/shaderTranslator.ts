/**
 * Translates Wallpaper Engine's shader dialect (GLSL 1.x syntax carrying
 * HLSL-isms, written for a compiler that converts int literals implicitly)
 * into WebGL2's GLSL ES 3.00.
 */

export type ShaderUniform = {
  type: string;
  name: string;
  arraySize: number | null;
  annotation: Record<string, unknown> | null;
};

export type ShaderCombo = { combo: string; defaultValue: number };

const includePattern = /^[ \t]*#include\s+"([^"]+)"[^\n]*$/gmu;
const comboPattern = /\/\/\s*\[COMBO\]\s*(\{[^\n]*\})/gu;
const uniformPattern = /^[ \t]*uniform\s+(?:(?:lowp|mediump|highp)\s+)?(\w+)\s+(\w+)\s*(?:\[\s*(\w+)\s*\])?\s*;[ \t]*(?:\/\/\s*(\{[^\n]*\}))?/gmu;

const parseJsonComment = (text: string): Record<string, unknown> | null => {
  try {
    const value = JSON.parse(text) as unknown;
    return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : null;
  } catch {
    return null;
  }
};

export const listShaderIncludes = (source: string): string[] =>
  [...source.matchAll(includePattern)].map((match) => match[1]);

/** Inlines `#include "x.h"` once per header; unknown headers become empty. */
export const inlineShaderIncludes = (source: string, headers: Map<string, string>, seen = new Set<string>()): string =>
  source.replace(includePattern, (_line, name: string) => {
    if (seen.has(name)) return '';
    seen.add(name);
    const header = headers.get(name);
    return header === undefined ? '' : inlineShaderIncludes(header, headers, seen);
  });

export const parseShaderCombos = (source: string): ShaderCombo[] => {
  const combos: ShaderCombo[] = [];
  for (const match of source.matchAll(comboPattern)) {
    const annotation = parseJsonComment(match[1]);
    if (typeof annotation?.combo === 'string') {
      combos.push({ combo: annotation.combo, defaultValue: Number(annotation.default ?? 0) || 0 });
    }
  }
  return combos;
};

export const parseShaderUniforms = (source: string): ShaderUniform[] =>
  [...source.matchAll(uniformPattern)].map((match) => ({
    type: match[1],
    name: match[2],
    arraySize: match[3] ? Number(match[3]) || null : null,
    annotation: match[4] ? parseJsonComment(match[4]) : null,
  }));

// HLSL stays undefined: `#ifdef HLSL` branches flip Y for Direct3D, which the
// runtime's top-row-first texture convention already matches without them.
const dialectMacros = `#define GLSL 1
#define mul(x, y) ((y) * (x))
#define frac fract
#define lerp mix
#define saturate(x) clamp((x), 0.0, 1.0)
#define texSample2D texture
#define texSample2DLod textureLod
#define CAST2(x) vec2(x)
#define CAST3(x) vec3(x)
#define CAST4(x) vec4(x)
#define CAST3X3(x) mat3(x)
#define atan2 atan
#define ddx dFdx
#define ddy dFdy
#define rsqrt inversesqrt
#define fmod(x, y) ((x) - (y) * trunc((x) / (y)))
#define log10(x) (log2(x) * 0.30102999566)
#define float2 vec2
#define float3 vec3
#define float4 vec4
#define float3x3 mat3
#define float4x4 mat4
`;

/**
 * HLSL broadcasts scalars into vectors for these built-ins; GLSL ES needs
 * explicit overloads. Defined before the renaming macros, so they call the
 * real built-ins.
 */
const broadcastOverloads = ((): string => {
  const lines: string[] = [];
  const floatTypes = ['vec2', 'vec3', 'vec4'];
  const intTypes = ['ivec2', 'ivec3', 'ivec4'];
  lines.push('float we_pow(float a, float b) { return pow(a, b); }');
  for (const name of ['max', 'min']) {
    lines.push(`float we_${name}(float a, float b) { return ${name}(a, b); }`);
    lines.push(`int we_${name}(int a, int b) { return ${name}(a, b); }`);
  }
  for (const type of floatTypes) {
    lines.push(`${type} we_pow(${type} a, ${type} b) { return pow(a, b); }`);
    lines.push(`${type} we_pow(${type} a, float b) { return pow(a, ${type}(b)); }`);
    lines.push(`${type} we_pow(float a, ${type} b) { return pow(${type}(a), b); }`);
    for (const name of ['max', 'min']) {
      lines.push(`${type} we_${name}(${type} a, ${type} b) { return ${name}(a, b); }`);
      lines.push(`${type} we_${name}(${type} a, float b) { return ${name}(a, b); }`);
      lines.push(`${type} we_${name}(float a, ${type} b) { return ${name}(${type}(a), b); }`);
    }
  }
  for (const type of intTypes) {
    for (const name of ['max', 'min']) {
      lines.push(`${type} we_${name}(${type} a, ${type} b) { return ${name}(a, b); }`);
      lines.push(`${type} we_${name}(${type} a, int b) { return ${name}(a, b); }`);
    }
  }
  lines.push('#define pow we_pow', '#define max we_max', '#define min we_min');
  return lines.join('\n');
})();

/**
 * Wallpaper Engine's compiler accepts uniforms declared after the header
 * functions that use them; GLSL needs them first. Unconditional top-level
 * uniforms move in front of the first function definition.
 */
export const hoistUniforms = (code: string): string => {
  const kept: string[] = [];
  const hoisted: string[] = [];
  let depth = 0;
  for (const line of code.split('\n')) {
    if (/^\s*#\s*if/u.test(line)) depth += 1;
    else if (/^\s*#\s*endif/u.test(line)) depth = Math.max(0, depth - 1);
    if (depth === 0 && /^\s*uniform\s[^;{}]*;/u.test(line)) hoisted.push(line);
    else kept.push(line);
  }
  if (hoisted.length === 0) return code;
  // Insert before the first function defined outside any conditional block.
  depth = 0;
  for (let index = 0; index < kept.length; index += 1) {
    const line = kept[index];
    if (/^\s*#\s*if/u.test(line)) depth += 1;
    else if (/^\s*#\s*endif/u.test(line)) depth = Math.max(0, depth - 1);
    // A definition header: `type name(params) {` on one line, or params continuing on the next.
    else if (depth === 0 && /^[ \t]*[A-Za-z_]\w*(?:[ \t]+[A-Za-z_]\w*)+[ \t]*\([^;{]*(?:\)\s*\{|$)/u.test(line)) {
      return [...kept.slice(0, index), ...hoisted, ...kept.slice(index)].join('\n');
    }
  }
  return code;
};

const varyingPattern = /^[ \t]*varying\s+(?:(?:lowp|mediump|highp)\s+)?(float|vec2|vec3|vec4)\s+(\w+)\s*;[^\n]*$/gmu;
const componentCounts: Record<string, number> = { float: 1, vec2: 2, vec3: 3, vec4: 4 };

/**
 * Wallpaper Engine links stages loosely: a fragment shader may read a varying
 * with fewer components than the vertex shader writes, may assign to its
 * varyings, and may use uniforms only the vertex shader declares. GLSL
 * requires exact matches and read-only inputs.
 */
export const reconcileShaderStages = (vertex: string, fragment: string): { vertex: string; fragment: string } => {
  const vertexVaryings = new Map([...vertex.matchAll(varyingPattern)].map((match) => [match[2], match[1]]));
  const copied: Array<{ name: string; type: string; written: string }> = [];
  let reconciled = fragment.replace(varyingPattern, (line, type: string, name: string) => {
    const written = vertexVaryings.get(name) ?? type;
    const narrowed = componentCounts[written] > componentCounts[type];
    const assigned = new RegExp(`\\b${name}\\s*(?:\\.\\s*[xyzwrgba]+\\s*)?[-+*/]?=(?!=)`, 'u').test(fragment);
    if (!narrowed && !assigned) return line;
    copied.push({ name, type, written });
    return `__WE_VARYING_${name}__`;
  });
  for (const { name, type, written } of copied) {
    // The varying keeps the vertex's name and width; the fragment works on a local copy.
    const swizzle = componentCounts[written] > componentCounts[type] ? `.${'xyzw'.slice(0, componentCounts[type])}` : '';
    reconciled = reconciled
      .replace(new RegExp(`\\b${name}\\b`, 'gu'), `we_local_${name}`)
      .replace(`__WE_VARYING_${name}__`, `varying ${written} ${name};\n${type} we_local_${name};`)
      .replace(/\bvoid\s+main\s*\(\s*(?:void)?\s*\)\s*\{/u, (open) => `${open}\nwe_local_${name} = ${name}${swizzle};`);
  }
  const fragmentUniforms = new Set(parseShaderUniforms(reconciled).map((uniform) => uniform.name));
  const borrowed = parseShaderUniforms(vertex)
    .filter((uniform) => !fragmentUniforms.has(uniform.name) && new RegExp(`\\b${uniform.name}\\b`, 'u').test(reconciled))
    .map((uniform) => `uniform ${uniform.type} ${uniform.name}${uniform.arraySize ? `[${uniform.arraySize}]` : ''};`);
  return { vertex, fragment: borrowed.length > 0 ? `${borrowed.join('\n')}\n${reconciled}` : reconciled };
};

/** GLSL ES 3.00 reserves these; Wallpaper Engine shaders may use them as names. */
const reservedIdentifiers = new Set(['input', 'output', 'sample', 'filter', 'common', 'partition', 'active', 'resource', 'superp', 'patch']);
const intTypes = new Set(['int', 'uint', 'ivec2', 'ivec3', 'ivec4', 'uvec2', 'uvec3', 'uvec4']);
const intFunctions = new Set([...intTypes, 'textureSize', 'texelFetch']);
const typeKeywords = new Set([...intTypes, 'float', 'bool', 'void', 'vec2', 'vec3', 'vec4', 'mat2', 'mat3', 'mat4', 'bvec2', 'bvec3', 'bvec4']);
const bitOperators = new Set(['%', '&', '|', '^']);
const floatTypes = new Set(['float', 'vec2', 'vec3', 'vec4', 'mat2', 'mat3', 'mat4']);
const arithmetic = new Set(['+', '-', '*', '/']);

type Token = { text: string; kind: 'trivia' | 'ident' | 'number' | 'punct' };

// Directives first, and newlines on their own, so an indented `#if` is still seen at line start.
const tokenPattern = /(^[ \t]*#[^\n]*)|(\/\/[^\n]*|\/\*[\s\S]*?\*\/|[ \t]+|\r?\n)|([A-Za-z_]\w*)|(\d+\.\d*(?:[eE][+-]?\d+)?[fF]?|\.\d+(?:[eE][+-]?\d+)?[fF]?|\d+[eE][+-]?\d+[fF]?|0[xX][0-9a-fA-F]+[uU]?|\d+[uU]?)|([\s\S])/gmu;

const tokenize = (code: string): Token[] => {
  const tokens: Token[] = [];
  for (const match of code.matchAll(tokenPattern)) {
    if (match[1] !== undefined || match[2] !== undefined) tokens.push({ text: match[0], kind: 'trivia' });
    else if (match[3] !== undefined) tokens.push({ text: match[0], kind: 'ident' });
    else if (match[4] !== undefined) tokens.push({ text: match[0], kind: 'number' });
    else tokens.push({ text: match[0], kind: 'punct' });
  }
  return tokens;
};

const isPlainInteger = (text: string): boolean => /^\d+$/u.test(text);
const isOperatorChar = (text: string): boolean => /^[-+*/%<>=!&|^~?:]$/u.test(text);

/**
 * Integer literals in float context compile in Wallpaper Engine's toolchain but
 * not in GLSL ES. Literals stay integers only where integers are required:
 * array indices, int declarations and loops, int casts, bit operations, and
 * comparisons or arithmetic next to int-typed variables.
 */
export const promoteIntegerLiterals = (code: string): string => {
  const tokens = tokenize(code);
  const significant = tokens.map((token, index) => (token.kind === 'trivia' ? -1 : index)).filter((index) => index >= 0);
  const intVariables = new Set([...code.matchAll(/\b(?:const\s+)?(?:u?int|[iu]vec[234])\s+([A-Za-z_]\w*)/gu)].map((match) => match[1]));
  const floatVariables = new Set([...code.matchAll(/\b(?:const\s+)?(?:float|vec[234]|mat[234])\s+([A-Za-z_]\w*)/gu)].map((match) => match[1]));
  const text = (position: number): string => (position >= 0 && position < significant.length ? tokens[significant[position]].text : '');

  let bracketDepth = 0;
  let braceDepth = 0;
  const parenStack: boolean[] = [];
  let statementStart = 0;
  let pendingFunctionInt = false;
  let functionReturnsInt = false;

  for (let position = 0; position < significant.length; position += 1) {
    const token = tokens[significant[position]];
    const value = token.text;
    if (token.kind === 'ident' && reservedIdentifiers.has(value)) token.text = `we_${value}`;
    if (value === '[') bracketDepth += 1;
    else if (value === ']') bracketDepth = Math.max(0, bracketDepth - 1);
    else if (value === '(') {
      const previous = text(position - 1);
      const parentInt = parenStack.length > 0 && parenStack[parenStack.length - 1];
      const forHeader = previous === 'for' && intTypes.has(text(position + 1));
      parenStack.push(parentInt || intFunctions.has(previous) || forHeader);
      if (braceDepth === 0 && typeKeywords.has(text(position - 2)) && tokens[significant[position - 1]]?.kind === 'ident') {
        pendingFunctionInt = intTypes.has(text(position - 2));
      }
    } else if (value === ')') parenStack.pop();
    else if (value === '{') {
      if (braceDepth === 0) functionReturnsInt = pendingFunctionInt;
      braceDepth += 1;
      statementStart = position + 1;
    } else if (value === '}') {
      braceDepth = Math.max(0, braceDepth - 1);
      if (braceDepth === 0) functionReturnsInt = false;
      statementStart = position + 1;
    } else if (value === ';' && parenStack.length === 0) statementStart = position + 1;

    const first = text(statementStart) === 'const' ? text(statementStart + 1) : text(statementStart);
    const insideIntContext = bracketDepth > 0 || (parenStack.length > 0 && parenStack[parenStack.length - 1]);
    // A float declaration initialised from int variables: cast them, as HLSL would implicitly.
    const floatDeclaration = floatTypes.has(first);
    if (token.kind === 'ident' && intVariables.has(value) && !insideIntContext
      && !typeKeywords.has(text(position - 1)) && text(position + 1) !== '(' && text(position + 1) !== '[') {
      // Also in plain arithmetic against a float operand, e.g. `(i / sampleDrop)`.
      const before = text(position - 1);
      const after = text(position + 1);
      const floatOperand = (operand: string): boolean => floatVariables.has(operand) || /^(?:\d*\.\d|\d+\.|\d+[eE])/u.test(operand);
      const mixesWithFloat = (arithmetic.has(before) && floatOperand(text(position - 2)))
        || (arithmetic.has(after) && floatOperand(text(position + 2)));
      if (floatDeclaration || mixesWithFloat) {
        token.text = `float(${value})`;
        continue;
      }
    }

    if (token.kind !== 'number' || !isPlainInteger(value)) continue;
    if (insideIntContext) {
      // A float expression being cast or indexed, e.g. `arr[int(uv.x * 16 + 0.01)]`.
      const floatOperand = (operand: number): boolean => /^(?:\d*\.\d|\d+\.|\d+[eE])/u.test(text(operand))
        || floatVariables.has(text(operand)) || (text(operand - 1) === '.' && floatVariables.has(text(operand - 2)));
      if ((arithmetic.has(text(position - 1)) && floatOperand(position - 2)) || (arithmetic.has(text(position + 1)) && floatOperand(position + 2))) {
        token.text = `${value}.0`;
      }
      continue;
    }
    if (intTypes.has(first) || first === 'case' || (first === 'return' && functionReturnsInt)) continue;
    if (floatDeclaration) {
      token.text = `${value}.0`;
      continue;
    }

    let back = position - 1;
    let backOperator = '';
    while (back >= 0 && isOperatorChar(text(back))) backOperator = text(back--) + backOperator;
    let forward = position + 1;
    let forwardOperator = '';
    while (forward < significant.length && isOperatorChar(text(forward))) forwardOperator += text(forward++);
    const touchesInt = (backOperator && intVariables.has(text(back))) || (forwardOperator && intVariables.has(text(forward)));
    const bitwise = [...backOperator.slice(-2), ...forwardOperator.slice(0, 2)].some((char) => bitOperators.has(char))
      || backOperator.endsWith('<<') || backOperator.endsWith('>>') || forwardOperator.startsWith('<<') || forwardOperator.startsWith('>>');
    if (touchesInt || bitwise) continue;
    token.text = `${value}.0`;
  }
  return tokens.map((token) => token.text).join('');
};

const definedNames = (source: string): Set<string> =>
  new Set([...source.matchAll(/^[ \t]*#define\s+(\w+)/gmu)].map((match) => match[1]));

/** GLSL ES rejects undefined identifiers in `#if`; Wallpaper Engine treats them as 0. */
const undefinedConditionNames = (source: string, defined: Set<string>): string[] => {
  const names = new Set<string>();
  for (const match of source.matchAll(/^[ \t]*#(?:el)?if\s+([^\n]*)/gmu)) {
    const expression = match[1].replace(/\/\/.*$/u, '').replace(/defined\s*\(?\s*\w+\s*\)?/gu, ' ');
    for (const identifier of expression.matchAll(/\b[A-Za-z_]\w*\b/gu)) {
      if (!defined.has(identifier[0])) names.add(identifier[0]);
    }
  }
  return [...names];
};

/** Wallpaper Engine's preprocessor ignores stray `#endif`s; GLSL rejects them. */
const dropUnmatchedEndifs = (code: string): string => {
  let depth = 0;
  return code.split('\n').filter((line) => {
    if (/^\s*#\s*if/u.test(line)) depth += 1;
    else if (/^\s*#\s*endif/u.test(line)) {
      if (depth === 0) return false;
      depth -= 1;
    }
    return true;
  }).join('\n');
};

export const translateWeShader = (
  source: string,
  stage: 'vertex' | 'fragment',
  defines: Record<string, number>,
): string => {
  let code = source
    .replace(/^[ \t]*#(?:version|require|include)\b[^\n]*$/gmu, '')
    .replace(/^[ \t]*precision\s+\w+\s+\w+\s*;/gmu, '');
  if (stage === 'vertex') {
    code = code.replace(/\battribute\b/gu, 'in').replace(/\bvarying\b/gu, 'out');
  } else {
    code = code.replace(/\bvarying\b/gu, 'in').replace(/\bgl_FragData\s*\[\s*0\s*\]/gu, 'we_FragColor').replace(/\bgl_FragColor\b/gu, 'we_FragColor');
  }
  code = code.replace(/\btexture2DLod\s*\(/gu, 'textureLod(').replace(/\btexture2D\s*\(/gu, 'texture(');
  code = hoistUniforms(promoteIntegerLiterals(dropUnmatchedEndifs(code)));

  const defineLines = Object.entries(defines).map(([name, value]) => `#define ${name} ${Math.trunc(value)}`);
  const known = new Set([...Object.keys(defines), ...definedNames(code), ...definedNames(dialectMacros)]);
  // Names also probed with #ifdef/defined() must stay undefined; replace them by 0 inside #if only.
  const probed = new Set([...code.matchAll(/(?:#ifn?def\s+|defined\s*\(?\s*)(\w+)/gu)].map((match) => match[1]));
  const undefinedNames = undefinedConditionNames(code, known);
  const substituted = undefinedNames.filter((name) => probed.has(name));
  if (substituted.length > 0) {
    const pattern = new RegExp(`\\b(${substituted.join('|')})\\b`, 'gu');
    code = code.replace(/^([ \t]*#(?:el)?if\s+)([^\n]*)$/gmu, (_line, directive: string, expression: string) =>
      directive + expression.replace(/defined\s*\(?\s*\w+\s*\)?|\b\w+\b/gu, (token) => (token.startsWith('defined') ? token : token.replace(pattern, '0'))));
  }
  const missing = undefinedNames.filter((name) => !probed.has(name)).map((name) => `#define ${name} 0`);
  return [
    '#version 300 es',
    'precision highp float;',
    'precision highp int;',
    'precision highp sampler2D;',
    dialectMacros,
    broadcastOverloads,
    ...defineLines,
    ...missing,
    stage === 'fragment' ? 'out vec4 we_FragColor;' : '',
    code,
  ].join('\n');
};
