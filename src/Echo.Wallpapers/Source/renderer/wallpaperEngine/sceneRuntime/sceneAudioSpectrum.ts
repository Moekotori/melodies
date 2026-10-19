/** One scene owns one sample window and six reusable shader uniform buffers. */
export class SceneAudioSpectrum {
  readonly samples = new Float32Array(128);
  private revision = 0;
  private readonly bands: Array<{ values: Float32Array; revision: number }> = [];

  constructor() {
    for (const size of [16, 32, 64]) {
      for (const offset of [0, 64]) {
        this.bands.push({
          values: size === 64 ? this.samples.subarray(offset, offset + 64) : new Float32Array(size),
          revision: -1,
        });
      }
    }
  }

  update(samples: ArrayLike<number>): void {
    for (let index = 0; index < 128; index += 1) this.samples[index] = Number(samples[index]) || 0;
    this.revision += 1;
  }

  /** Aggregate each requested channel/size once per incoming spectrum, not per draw pass. */
  uniform(size: 16 | 32 | 64, right: boolean): Float32Array {
    const band = this.bands[(size === 16 ? 0 : size === 32 ? 2 : 4) + (right ? 1 : 0)];
    if (size !== 64 && band.revision !== this.revision) {
      const step = 64 / size;
      const channel = right ? 64 : 0;
      for (let index = 0; index < size; index += 1) {
        let sum = 0;
        for (let offset = 0; offset < step; offset += 1) sum += this.samples[channel + (index * step) + offset];
        band.values[index] = sum / step;
      }
      band.revision = this.revision;
    }
    return band.values;
  }
}
