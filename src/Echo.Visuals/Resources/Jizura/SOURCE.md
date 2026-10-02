# JIZURA native visual resources

This native port interprets local visual scripts through Jint and renders Canvas calls through Skia. It exposes no browser, Node, network, filesystem or audio APIs. `manifest.json` records the 91 original source hashes; `looks.json` contains 23 first-party directions. Native raster translations and validation limits are documented in `docs/JIZURA_NATIVE_ENGINE.md`.

The following is the read-only source vendor's original attribution and historical browser-runtime description; iframe implementation details apply to the old ECHO runtime, not this native port.

## Original vendor attribution

ECHO exposes this optional style as **Beta**. Its selected-style drawer includes
the upstream GitHub link, copyright attribution and expandable complete MIT
License text. These controls/styles are scoped to JIZURA; other lyrics styles
retain their existing labels, rendering and settings.

- Upstream: https://github.com/852wa/JIZURA
- Revision: `8da975fb362d966b065217618aedafd5a35a39e0`
- License: MIT, Copyright (c) 2026 hakoniwa. Full text: [LICENSE](./LICENSE).
- These JavaScript files are unmodified upstream sources, in upstream build order.
- `sources.json` records SHA-256 for each copied source and the license.
- MIT permits commercial redistribution with the copyright and permission notice.
  The complete license is also embedded in the built iframe document.
- Only visual planning and rendering are assembled. The upstream audio decoder,
  editor, export module, mp4-muxer, sample media and external font assets are omitted.
- ECHO's separate adapter supplies exact text, line times and a presentation clock,
  suppresses remote font loading, limits raster resolution and cache residency,
  and disposes the sandboxed runtime when this optional style is unmounted.
- The persistent parent module retains only local script asset URLs. Source text
  exists only in the currently mounted stage's document, and compiled engine code
  stays inside its iframe. An abortable four-request
  loader reads local assets only while the stage is mounted (8 MiB text cap).
  Its temporary srcdoc is discarded with the stage. Classic scripts run in build
  order inside the opaque frame; its original network-denying CSP is retained.
- Missing/untimed/blank lyrics never activate the style. Leaving the page or
  switching styles unmounts the complete stage, cancels subscriptions/timers,
  clears its srcdoc, and disposes raster backing surfaces and caches. The saved
  style preference remains available for the next song with usable timed lyrics.
- The iframe has no same-origin permission, preload API, network capability,
  audio input, local file path, persistence or playback authority.
- Users' music and lyrics retain their respective rights; this engine license
  grants no rights to third-party songs, lyrics or fonts.

## ECHO playback integration

Continuous motion defaults to 60 FPS (`koma: 0`, `onTwos: false`). The original
12-FPS stepped look is an explicit option. A foreground-only local frame choice
does not modify ECHO's global frame preference; hidden, moving, unfocused and
pressure-reduced budgets are retained. Bounded one-second draw counters report
render calls, not guaranteed display presentation or GPU frame completion.

The original `J.omakase` function supplies coherent full-composition randomization.
Frame rate, quality, stepped motion and extra-expression choices persist through
the existing AppSettings owner. Automatic quality can drop filters and ghost
passes for a costly plan; lightweight mode also caps the canvas at 960 x 540.

## ECHO Type Motion

ECHO owns the look gallery, controls, four visual directions (Mist, Nightfall,
Dusk and Paper Echo), palette variants and effect curation. These first-party
modules use JIZURA rendering primitives and are not copies of its editor UI.
All 27 upstream styles remain selectable in a separate original-style group;
that group retains upstream palettes, effect selection and `J.omakase` behavior.
The UI credits JIZURA as a tribute; the complete MIT notice remains included.

The default frame follows the window using the same aspect-neutral planner.
Cinema mode preserves 16:9. Opening the gallery parks visual frame production
while retaining the supplied playback clock; selecting a look resumes drawing.
Gallery previews are first-party CSS/SVG posters with a fixed maximum of 32 original entries,
without raster image generation, base64 payloads or a parallel thumbnail cache.

## Frame rate and performance

Custom foreground frame rates are integer values from 1 through 240 FPS. Drawing
remains bounded by the display cadence and the existing hidden/unfocused/pressure
budgets. Low-rate rendering uses one cancellable delayed wake instead of an
otherwise idle full-rate RAF loop.

On faster displays the same delayed wake also skips RAF polling between lower
target-rate frames. Reduced-motion cuts wake at their next cut end/start boundary,
adjusted for playback speed; incoming clock corrections reschedule that one timer.
Animated cuts, gaps and transparent-mask effects retain the original rendering
semantics. These changes add no raster cache, playback clock or resident song-data
copy.

Drawing settings (frame rate, quality and stepped motion) are separate from the
lyrics/style plan, so changing them does not retransmit song text, rebuild cuts,
or discard glyph caches. Continuous clock packets coalesce to at most 10 Hz;
state changes and seeks bypass this throttle, while unchanged paused/hidden
packets are suppressed. The host remains the owner of audio and playback facts.

The original cache owners use bounded LRU maps, with 6 MiB glyph raster, 6 MiB
tinted raster and 4 MiB paper-raster storage estimates (2/2/1 MiB under pressure).
These are cached pixel estimates, not whole-process or GPU residency guarantees.
Language changes invalidate glyph metrics; closing the runtime releases all
cache references. Effect rendering uses a binary-searched time window over the
original sorted event timeline, preserving effect-duration and impulse tails
without scanning the entire song every frame.

## Menu visibility and shortcut

The menu is independent of corner-control auto-hide. Only explicit collapse,
reveal-button clicks or the configured menu shortcut change its visibility.
The lyrics visual-settings drawer records/clears that keyboard binding through
the existing AppSettings owner and checks enabled shortcut conflicts.
The opaque frame relays Escape as a fixed lyrics-back command and the configured
menu key chord; the parent verifies the sender and canonical accelerator. Escape
works without a menu binding and uses the existing lyrics-page navigation event.
Text input, repeated/composing keys and active
shortcut recording are excluded. Closing the page removes listeners and the
frame has no global-shortcut, Node or raw IPC capability.

## ECHO signature looks and backdrops

ECHO owns fourteen looks (Mist, Nightfall, Dusk, Paper Echo plus Blue Sky, Rain City,
Festival, Kabuki, Snow Light, Retro Café, After School, Magic Stars, Cat Shrine, Pixel Play). They are data and canvas code written
for ECHO (`jizuraLooks.ts`, `jizuraSignatureLooks.ts`, `jizuraRhythmLook.ts`, `jizuraArtDirection.js`,
`jizuraEchoKit.js`, `jizuraBackdropsDay.js`, `jizuraBackdropsNight.js`, `jizuraBackdropsSweet.js`, `jizuraBackdropsSweetScenes.js`, `jizuraBackdropsDayMore.js`, `jizuraBackdropsSweetMore.js`, `jizuraBackdropsNeko.js`, `jizuraBackdropsNekoMore.js`, `jizuraBackdropsNightMore.js`, `jizuraBackdropsPixel.js`, `jizuraBackdropsPixelMore.js`, `jizuraBackdropsEchoMist.js`, `jizuraBackdropsEchoNight.js`, `jizuraBackdropsEchoDusk.js`, `jizuraBackdropsEchoPaper.js`, `jizuraBackdropsEchoCuteA.js`, `jizuraBackdropsEchoCuteB.js`, `jizuraBackdropsEchoStage.js`, `jizuraBackdropsEchoRockA.js`, `jizuraBackdropsEchoRockB.js`, `jizuraBackdropsEchoPlay.js`, `jizuraBackdropsEchoHifi.js`, `jizuraBackdropsEchoCode.js`, `jizuraBackdropsEchoArena.js`, `jizuraBackdropsEchoRhythm.js`, `jizuraBackdropsEchoRhythmLanes.js`, `jizuraBackdropsEchoRhythmFields.js`, `jizuraBackdropsEchoRhythmStages.js`, `jizuraRhythmTypography.js`) that plug into the unmodified upstream registries; each
look ships hand-built colourways and one or several first-party layered scenes (taking turns line by line) with an optional light foreground layer drawn over the lyric, and two text treatments (gold foil, neon tube). Every
shape is drawn procedurally: no third-party images, fonts or other assets are
added, so no additional notice is required. Backdrops fill lines the planner left
bare and work without the optional extra expression packs.
