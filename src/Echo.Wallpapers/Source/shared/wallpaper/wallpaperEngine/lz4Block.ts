/** Raw LZ4 block decompression (no frame header), as used by Wallpaper Engine `.tex` mipmaps. */
export const decompressLz4Block = (input: Uint8Array, outputSize: number): Uint8Array => {
  const output = new Uint8Array(outputSize);
  let ip = 0;
  let op = 0;
  const readLength = (initial: number): number => {
    let length = initial;
    if (initial !== 15) return length;
    let byte = 255;
    while (byte === 255) {
      if (ip >= input.length) throw new Error('lz4-truncated');
      byte = input[ip++];
      length += byte;
    }
    return length;
  };

  while (ip < input.length) {
    const token = input[ip++];
    const literalLength = readLength(token >>> 4);
    if (ip + literalLength > input.length || op + literalLength > outputSize) throw new Error('lz4-literal-overflow');
    output.set(input.subarray(ip, ip + literalLength), op);
    ip += literalLength;
    op += literalLength;
    if (ip >= input.length) break;

    if (ip + 2 > input.length) throw new Error('lz4-truncated');
    const offset = input[ip] | (input[ip + 1] << 8);
    ip += 2;
    if (offset === 0 || offset > op) throw new Error('lz4-offset-invalid');
    const matchLength = readLength(token & 15) + 4;
    if (op + matchLength > outputSize) throw new Error('lz4-match-overflow');
    let match = op - offset;
    if (offset >= matchLength) {
      output.copyWithin(op, match, match + matchLength);
      op += matchLength;
    } else {
      // Overlapping copy repeats the most recent bytes.
      for (let index = 0; index < matchLength; index += 1) output[op++] = output[match++];
    }
  }

  if (op !== outputSize) throw new Error('lz4-size-mismatch');
  return output;
};
