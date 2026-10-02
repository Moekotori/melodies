# 流派迁移视觉 QA

source visual truth: C:/Users/Moe/AppData/Local/Temp/codex-clipboard-566aa24a-ef04-458e-bd9e-a41c74cbd1c7.png
implementation: misc/genres-migration/preview/genres-light.png
additional states: genres-dark.png, genres-narrow.png, genre-detail-dark.png, genre-tracks-dark.png

Viewport: source 2977×1780 screenshot pixels (conversation displays 2048×1225); implementation native Avalonia 1440×900 logical/pixel, 96 DPI, no CSS/browser layer. Reference aspect-preserving comparison at width 1440 corresponds to 861px height. The extra 39px and existing Avalonia shell are expected migration differences. This is layout/feature migration into the current native shell, not reconstruction of old title/player chrome. Test names/counts/covers are isolated fixture data.
State: Chinese/light, local library, default track-count descending wall. Full reference and final render were opened in the same tool-result comparison; additional dark/detail/narrow renders also inspected. Focused surfaces are legible at this size: title/search toolbar, circular masks, name/stat lines, and bottom clipping.

## Findings and fixes

- P2 first render: title missing because navigation.genres resource did not exist. Added explicit Chinese/English keys; final snapshot compiled the Chinese key. First retry used a preview resource override while waiting for the final compilation.
- P2 first render: max-width search field centered in its star column. Named search area now uses measured bounded width and left alignment. Final screenshot starts search at the same content edge as the title.
- Final geometry: original open wall retained, no individual framed cards. 124px circular real artwork, serif/CJK-aware missing-art marks, centered names and per-genre track/album counts. Seven columns at 1440; three at 800 with toolbar in two rows. Additional clipped row is correctly scrollable above the persistent playback bar.

## Required surfaces

- Typography: existing Echo native font/tokens; 30px heading, 12px semibold card name, 11px statistics. Reference hierarchy retained; responsive column count differs with viewport.
- Spacing/layout: original left heading/search, right source/sort/refresh, circular open grid. Search alignment corrected; 22px horizontal gutters and 216px rows reuse current artist-wall density.
- Colors/tokens: current Echo light/dark semantic resources. Existing native muted-text color is somewhat bluer than old screenshot; intentionally shared with other migrated pages.
- Image quality: actual local album image leases, uniform-fill circular clipping and bounded decoder. Fixture covers copied read-only from local SMTC cache; displayed genre associations/counts are not production facts.
- Copy: flow labels/search/empty/error/count/order translated; canonical metadata shown as stored. Cloud switch disabled with capability explanation because remote library port is absent.

## Primary interaction evidence

SQLite: normalization, casing/fullwidth, unclassified, whole slash tag, paging, retag/album maintenance, missing/restoration, revision/cancellation, backup/backfill assertions passed before the first combined run reached UI.
UI: search/empty/latest generation, End key deep paging, hide/restore, genre→album→genre, paged tracks, simulated play/next/enqueue, live language, narrow layout and disposal passed after creating the missing fixture audio paths. Final visual retry passed.
Native app: no browser console. Snapshot XAML build has five existing unrelated loader warnings; no genre errors. Shared source full-build acceptance and live audio remain unverified; details in docs/GENRES_MIGRATION.md.

## Comparison history

1. Initial inspected render: missing title and search alignment recorded as P2.
2. Added resources and left-aligned adaptive search. Compiled snapshot desktop, copied its own assembly into isolated Preview and rerendered all UI states; final reference/final-render comparison confirms both fixes.

## Implementation checklist

Completed: scoped flow, data owner, cancellation, resource lifetime, native render/interaction checks, P2 visual repairs, migration and rollback documentation.
P3: visible shortcut hint and precise old sort-menu styling can follow existing native control-system consolidation. Remote feature and real device acceptance remain separate.

final result: passed
