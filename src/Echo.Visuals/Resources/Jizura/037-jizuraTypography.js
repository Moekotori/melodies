/* Local system-font roles for ICPC. No downloaded faces or bundled font resources.
   Dedicated keys keep glyph/metric caches distinct from other looks. */
(() => {
  'use strict';
  const cjk = '"Yu Gothic UI","Hiragino Sans","Microsoft YaHei UI","PingFang SC",sans-serif';
  Object.assign(J.FONTS, {
    echoIcpcDisplay: { label: 'ICPC / System Sans Semibold', family: '"Segoe UI Variable Display","Segoe UI","Helvetica Neue",Arial', weight: 600, kind: 'gothic', fb: cjk },
    echoIcpcBody: { label: 'ICPC / System Sans', family: '"Segoe UI Variable Text","Segoe UI","Helvetica Neue",Arial', weight: 400, kind: 'gothic', fb: cjk },
    echoIcpcMono: { label: 'ICPC / System Mono', family: '"Cascadia Mono",Consolas,"SFMono-Regular"', weight: 500, kind: 'mono', fb: cjk },
  });
})();
