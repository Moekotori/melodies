/** Reserved path served next to a web wallpaper's own files. */
export const wallpaperEngineBridgeScriptName = '__echo_we_bridge__.js';

/**
 * Wallpaper Engine's page-side API on top of host `postMessage` events. The
 * frame is sandboxed without same-origin, so this is its only host channel and
 * it only ever receives data: audio bins, now-playing facts and pause state.
 */
export const createWallpaperEngineBridgeScript = (userProperties: Record<string, unknown>): string => `(() => {
  'use strict';
  const userProperties = ${JSON.stringify(userProperties).replace(/</gu, '\\u003c')};
  const listeners = { audio: [], properties: [], playback: [], timeline: [], thumbnail: [], status: [] };
  const call = (list, value) => {
    for (const callback of list) {
      try { callback(value); } catch (error) { console.error(error); }
    }
  };
  const register = (list) => (callback) => { if (typeof callback === 'function') list.push(callback); };
  window.wallpaperMediaIntegration = { PLAYBACK_STOPPED: 0, PLAYBACK_PLAYING: 1, PLAYBACK_PAUSED: 2 };
  window.wallpaperRegisterAudioListener = (callback) => {
    if (typeof callback !== 'function') return;
    if (listeners.audio.length === 0) window.parent.postMessage({ type: 'echo-we:audio-wanted' }, '*');
    listeners.audio.push(callback);
  };
  window.wallpaperRegisterMediaPropertiesListener = register(listeners.properties);
  window.wallpaperRegisterMediaPlaybackListener = register(listeners.playback);
  window.wallpaperRegisterMediaTimelineListener = register(listeners.timeline);
  window.wallpaperRegisterMediaThumbnailListener = register(listeners.thumbnail);
  window.wallpaperRegisterMediaStatusListener = (callback) => {
    register(listeners.status)(callback);
    if (typeof callback === 'function') { try { callback({ enabled: true }); } catch (error) { console.error(error); } }
  };
  const propertyListener = () => window.wallpaperPropertyListener;
  let applied = false;
  let frameRate = 30;
  const applyInitialProperties = () => {
    const listener = propertyListener();
    if (applied || !listener) return;
    applied = true;
    try { listener.applyGeneralProperties && listener.applyGeneralProperties({ fps: frameRate }); } catch (error) { console.error(error); }
    try { listener.applyUserProperties && listener.applyUserProperties(userProperties); } catch (error) { console.error(error); }
  };
  document.addEventListener('DOMContentLoaded', () => setTimeout(applyInitialProperties, 0));
  window.addEventListener('load', () => {
    applyInitialProperties();
    setTimeout(applyInitialProperties, 500);
    window.parent.postMessage({ type: 'echo-we:ready' }, '*');
  });
  window.addEventListener('message', (event) => {
    if (event.source !== window.parent) return;
    const data = event.data;
    if (!data || typeof data !== 'object') return;
    switch (data.type) {
      case 'echo-we:frame-interval': {
        frameRate = 1000 / data.frameIntervalMs;
        const listener = propertyListener();
        try { listener && listener.applyGeneralProperties && listener.applyGeneralProperties({ fps: frameRate }); } catch (error) { console.error(error); }
        break;
      }
      case 'echo-we:audio':
        if (Array.isArray(data.samples) && data.samples.length === 128) call(listeners.audio, data.samples);
        break;
      case 'echo-we:media':
        call(listeners.properties, data.properties);
        break;
      case 'echo-we:playback':
        call(listeners.playback, { state: data.state });
        break;
      case 'echo-we:timeline':
        call(listeners.timeline, { position: data.position, duration: data.duration });
        break;
      case 'echo-we:paused': {
        const listener = propertyListener();
        try { listener && listener.setPaused && listener.setPaused(Boolean(data.paused)); } catch (error) { console.error(error); }
        break;
      }
      default:
        break;
    }
  });
})();
`;

/** Places the bridge before any page script, keeping a leading doctype intact. */
export const injectWallpaperEngineBridge = (html: string, bridgeUrl: string): string => {
  const tag = `<script src="${bridgeUrl}"></script>`;
  const head = /<head(\s[^>]*)?>/iu.exec(html);
  if (head) return html.slice(0, head.index + head[0].length) + tag + html.slice(head.index + head[0].length);
  const opening = /<html(\s[^>]*)?>|<!doctype[^>]*>/iu.exec(html);
  if (opening) return html.slice(0, opening.index + opening[0].length) + tag + html.slice(opening.index + opening[0].length);
  return tag + html;
};
