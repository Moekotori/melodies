/** Vertex attribute flags of Wallpaper Engine `.mdl` meshes. */
const flagNormal = 2;
const flagTangent = 4;
const flagUv = 8;
const flagColor = 16;
const flagTwoUvs = 32;
const flagSkinning = 0x01800000;

export type ModelAttribute = { name: string; size: number; offset: number; integer: boolean };

export type ModelMesh = {
  /** Material files; meshes with several draw once per material. */
  materials: string[];
  stride: number;
  attributes: ModelAttribute[];
  vertexCount: number;
  vertices: ArrayBuffer;
  indices: Uint16Array;
};

const readCString = (bytes: Uint8Array, start: number): { value: string; next: number } => {
  let end = start;
  while (end < bytes.length && bytes[end] !== 0) end += 1;
  return { value: new TextDecoder().decode(bytes.subarray(start, end)), next: end + 1 };
};

/** Interleaved layout implied by the flags, in file order. */
export const modelVertexLayout = (flags: number): { stride: number; attributes: ModelAttribute[] } => {
  const attributes: ModelAttribute[] = [];
  let offset = 0;
  const add = (name: string, size: number, integer = false): void => {
    attributes.push({ name, size, offset, integer });
    offset += size * 4;
  };
  add('a_Position', 3);
  if (flags & flagNormal) add('a_Normal', 3);
  if (flags & flagTangent) add('a_Tangent4', 4);
  if (flags & flagSkinning) {
    add('a_BlendIndices', 4, true);
    add('a_BlendWeights', 4);
  }
  if (flags & flagTwoUvs) {
    // Lightmapped shaders read both sets as one `a_TexCoordVec4`.
    attributes.push({ name: 'a_TexCoordVec4', size: 4, offset, integer: false });
    add('a_TexCoord', 2);
    add('a_TexCoordC2', 2);
  } else if (flags & flagUv) add('a_TexCoord', 2);
  if (flags & flagColor) add('a_Color', 4);
  return { stride: offset, attributes };
};

/**
 * Parses `MDLV00xx` meshes. Versions differ in per-mesh framing: 0016+ repeat
 * the vertex flags per mesh, 0017+ add a bounding box before them, 0023+ a short
 * trailer after the indices. `end` is where the meshes stop; puppet skeleton
 * and animation sections follow it.
 */
export const parseModel = (bytes: Uint8Array): { meshes: ModelMesh[]; end: number } => {
  const magic = String.fromCharCode(...bytes.subarray(0, 8));
  const match = /^MDLV(\d{4})$/u.exec(magic);
  if (!match) throw new Error('model-not-mdl');
  const version = Number(match[1]);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let position = 9;
  const int32 = (): number => {
    if (position + 4 > bytes.length) throw new Error('model-truncated');
    const value = view.getInt32(position, true);
    position += 4;
    return value;
  };
  const headerFlags = int32();
  const materialsPerMesh = int32();
  const meshCount = int32();
  if (materialsPerMesh < 1 || materialsPerMesh > 16 || meshCount < 1 || meshCount > 256) throw new Error('model-header-invalid');

  const meshes: ModelMesh[] = [];
  for (let mesh = 0; mesh < meshCount; mesh += 1) {
    const materials: string[] = [];
    for (let index = 0; index < materialsPerMesh; index += 1) {
      const material = readCString(bytes, position);
      materials.push(material.value);
      position = material.next;
    }
    int32();
    if (version >= 17) position += 24;
    const flags = version >= 16 ? int32() : headerFlags;
    const vertexBytes = int32();
    if (vertexBytes < 0 || position + vertexBytes > bytes.length) throw new Error('model-vertices-invalid');
    const vertices = bytes.slice(position, position + vertexBytes).buffer;
    position += vertexBytes;
    const indexBytes = int32();
    if (indexBytes < 0 || position + indexBytes > bytes.length) throw new Error('model-indices-invalid');
    const indices = new Uint16Array(bytes.slice(position, position + indexBytes).buffer);
    position += indexBytes;
    if (version >= 23) position += 6;

    const { stride, attributes } = modelVertexLayout(flags);
    const vertexCount = vertexBytes / stride;
    if (!Number.isInteger(vertexCount) || indices.some((value) => value >= vertexCount)) throw new Error('model-layout-unknown');
    meshes.push({ materials, stride, attributes, vertexCount, vertices, indices });
  }
  return { meshes, end: position };
};
