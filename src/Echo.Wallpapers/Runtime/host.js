(() => {
  'use strict';
  const frame = document.getElementById('wallpaper');
  const state = window.__echoStatus = { ready: false, failed: false, audio: false, pointer: false };
  window.__echoSend = message => frame.contentWindow.postMessage(message, '*');
  window.addEventListener('message', event => {
    if (event.source !== frame.contentWindow) return;
    switch (event.data?.type) {
      case 'echo-we:ready': state.ready = true; state.failed = false; break;
      case 'echo-we:static-ready': state.ready = false; state.failed = true; break;
      case 'echo-we:audio-wanted': state.audio = true; break;
      case 'echo-we:pointer-wanted': state.pointer = true; break;
    }
  });
})();
