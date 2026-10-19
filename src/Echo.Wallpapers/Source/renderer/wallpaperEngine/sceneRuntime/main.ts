import { SceneAssets } from './sceneAssets';
import { SceneRenderer, type SceneRuntimeConfig } from './sceneRenderer';

/** Scenes stay within 30 fps and follow the host's lower background budgets. */
const defaultFrameIntervalMs = 1000 / 30;

const postReady = (() => {
  let posted = false;
  return (): void => {
    if (posted) return;
    posted = true;
    window.parent.postMessage({ type: 'echo-we:ready' }, '*');
  };
})();

const start = async (): Promise<void> => {
  const poster = document.getElementById('poster') as HTMLImageElement;
  const showPoster = (): void => window.parent.postMessage({ type: 'echo-we:poster-ready' }, '*');
  if (poster.complete && poster.naturalWidth > 0) showPoster();
  else poster.addEventListener('load', showPoster, { once: true });
  const canvas = document.getElementById('scene') as HTMLCanvasElement;
  const config = JSON.parse(document.getElementById('echo-scene-config')?.textContent ?? '{}') as SceneRuntimeConfig;
  const gl = canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, premultipliedAlpha: false });
  if (!gl) {
    // The poster stays visible.
    window.parent.postMessage({ type: 'echo-we:static-ready' }, '*');
    return;
  }
  const renderer = await SceneRenderer.load(gl, canvas, new SceneAssets(new URL('./', window.location.href).href), config);
  // The host forwards the cursor only to scenes that react to it.
  if (renderer.wantsPointer) window.parent.postMessage({ type: 'echo-we:pointer-wanted' }, '*');
  let audioRequested = false;
  const requestAudioIfNeeded = (): void => {
    if (!audioRequested && renderer.wantsAudio) {
      audioRequested = true;
      window.parent.postMessage({ type: 'echo-we:audio-wanted' }, '*');
    }
  };
  requestAudioIfNeeded();

  let frameIntervalMs = defaultFrameIntervalMs;
  let paused = false;
  let frameRequest: number | null = null;
  let lastFrame = 0;
  const tick = (now: number): void => {
    frameRequest = null;
    if (paused) return;
    frameRequest = window.requestAnimationFrame(tick);
    if (lastFrame !== 0 && now - lastFrame < frameIntervalMs - 1) return;
    const delta = lastFrame === 0 ? frameIntervalMs / 1000 : Math.min(0.25, (now - lastFrame) / 1000);
    lastFrame = now;
    renderer.render(delta);
    requestAudioIfNeeded();
    if (canvas.dataset.ready !== 'true') {
      canvas.dataset.ready = 'true';
      postReady();
    }
  };

  window.addEventListener('message', (event: MessageEvent) => {
    if (event.source !== window.parent || !event.data || typeof event.data !== 'object') return;
    const data = event.data as { type?: unknown; samples?: unknown; paused?: unknown; frameIntervalMs?: unknown; x?: unknown; y?: unknown };
    if (data.type === 'echo-we:audio' && Array.isArray(data.samples) && data.samples.length === 128) {
      renderer.setSpectrum(data.samples as number[]);
    } else if (data.type === 'echo-we:media' || data.type === 'echo-we:playback' || data.type === 'echo-we:timeline') {
      renderer.dispatchMedia(data.type, data as Record<string, unknown>);
    } else if (data.type === 'echo-we:pointer' && typeof data.x === 'number' && typeof data.y === 'number') {
      renderer.setPointer(Math.min(1, Math.max(0, data.x)), Math.min(1, Math.max(0, data.y)));
    } else if (data.type === 'echo-we:frame-interval' && typeof data.frameIntervalMs === 'number') {
      frameIntervalMs = Math.max(defaultFrameIntervalMs, data.frameIntervalMs);
    } else if (data.type === 'echo-we:paused') {
      paused = data.paused === true;
      renderer.setPaused(paused);
      if (paused && frameRequest !== null) {
        window.cancelAnimationFrame(frameRequest);
        frameRequest = null;
      } else if (!paused && frameRequest === null) {
        lastFrame = 0;
        frameRequest = window.requestAnimationFrame(tick);
      }
    }
  });
  window.addEventListener('resize', () => renderer.resize());
  canvas.addEventListener('webglcontextlost', () => {
    paused = true;
    if (frameRequest !== null) window.cancelAnimationFrame(frameRequest);
    frameRequest = null;
    canvas.dataset.ready = 'false';
    window.parent.postMessage({ type: 'echo-we:static-ready' }, '*');
  });
  frameRequest = window.requestAnimationFrame(tick);
};

start().catch((error: unknown) => {
  console.error('[scene] failed to start', error);
  window.parent.postMessage({ type: 'echo-we:static-ready' }, '*');
});
