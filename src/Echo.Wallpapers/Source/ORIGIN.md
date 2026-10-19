The TypeScript scene runtime and web bridge are copied from the user's Electron ECHO repository
`moekotori/echosteam`, commit `1906021d4e860e7c3d479617590d2dda3095c6ac` (2026-10-19).
Original paths: `src/renderer/wallpaperEngine/sceneRuntime`,
`src/shared/wallpaper/wallpaperEngine`, and
`src/main/integrations/wallpaperEngine/import/wallpaperEngineWebBridge.ts`.
The original AGPL-3.0-only license is retained in this directory.

`Runtime/scene-runtime.js` is the generated browser bundle, with no external npm dependencies.
Build with esbuild: `esbuild Source/renderer/wallpaperEngine/sceneRuntime/main.ts --bundle --format=iife --platform=browser --target=chrome120 --minify --keep-names --outfile=Runtime/scene-runtime.js`.
The checked-in bundle makes normal .NET builds independent of Node and the reference checkout.
`Runtime/web-bridge.js` retains ECHO's property/media/pause bridge; its property JSON is supplied by the local asset server.

Wallpaper Engine's installed shaders, fonts, materials and base script modules remain read-only
runtime resources. They are neither copied from the installation nor redistributed with this application.
