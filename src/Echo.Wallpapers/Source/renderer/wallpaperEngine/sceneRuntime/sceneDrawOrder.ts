type DrawNode = { kind: string; model: { translucent: boolean } | null };
type TranslucentNode<T> = { node: T; distance: number; sceneIndex: number };
type ModelRun<T> = { start: number; translucent: Array<TranslucentNode<T>> };

const farthestFirst = <T>(a: TranslucentNode<T>, b: TranslucentNode<T>): number =>
  (b.distance - a.distance) || (a.sceneIndex - b.sceneIndex);

/** Scene topology is fixed after loading; only translucent model distances vary per frame. */
export class SceneDrawOrder<T extends DrawNode> {
  private readonly order: T[] = [];
  private readonly runs: Array<ModelRun<T>> = [];

  constructor(nodes: readonly T[]) {
    const pending: T[] = [];
    const flush = (): void => {
      for (const node of pending) if (!node.model!.translucent) this.order.push(node);
      const translucent: Array<TranslucentNode<T>> = [];
      for (let index = 0; index < pending.length; index += 1) {
        const node = pending[index];
        if (node.model!.translucent) translucent.push({ node, distance: 0, sceneIndex: index });
      }
      const start = this.order.length;
      for (const entry of translucent) this.order.push(entry.node);
      if (translucent.length > 1) this.runs.push({ start, translucent });
      pending.length = 0;
    };
    for (const node of nodes) {
      if (node.kind === 'model') pending.push(node);
      else if (node.kind !== 'group' && node.kind !== 'light') {
        flush();
        this.order.push(node);
      }
    }
    flush();
  }

  update(distance: (node: T) => number): readonly T[] {
    for (const run of this.runs) {
      for (const entry of run.translucent) entry.distance = distance(entry.node);
      run.translucent.sort(farthestFirst);
      for (let index = 0; index < run.translucent.length; index += 1) this.order[run.start + index] = run.translucent[index].node;
    }
    return this.order;
  }
}
