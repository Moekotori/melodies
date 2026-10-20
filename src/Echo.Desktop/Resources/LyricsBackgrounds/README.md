# Lyrics background previews

320 × 180 WebP thumbnails derived from 512 × 288 reference frames captured on 2026-10-15 from ECHO's existing
shader, flowing-light, geometric-ambience and cover-flow renderers. No live
renderer is initialized by the picker. Captures use a fixed dark palette and
ECHO's existing `echo-album-placeholder-mascot-dark.png` for cover scenes.
The six `coverPixel*` previews were captured on 2026-10-19 the same way, from the compiled scene sources.
The theme swatch and blurred-cover swatches represent the plain theme and
cover sources; default follows the selected lyrics style in the application.

Sources and licensing are unchanged from the ECHO renderers and bundled assets;
no third-party assets, user media or additional dependencies were introduced.
The temporary capture fixture is in ignored `misc/lyrics-quick-switch/`.
Production imports use `?no-inline`, visible-card mounting, native lazy image loading, and unmount when
a card leaves the scroll viewport or the menu closes. Custom image wallpaper previews use the existing local wallpaper
protocol; an unselected custom source or video uses an image/video icon.
