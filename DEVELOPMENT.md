# Film Index Emby — development checkpoint

Target: Emby **4.10.1.0**. Build: **0.1.0.0-development**. This GPL community derivative is not a modified or authorized StrmAssistant PRO binary.

## Current verification

- Exact official 4.10.1.0 compilation passes.
- **43** assembly, rule and real-FFmpeg checks pass.
- **66** fixture integration/contract checks pass inside an isolated official Windows 4.10.1.0 server.
- Reports: `tests/results/`. Checks are not a count of product features or proof of full parity.
- Generated media and disposable users only; no production NAS was accessed.
- Notification checks use a recording sink, not actual external channel delivery.
- Distributed extraction uses a real HTTP round trip within one fixture server, not a certified cross-host deployment.
- No local Docker or usable WSL installation; the user's fnOS `amilys/embyserver` image has not been tested.

## Implemented and exercised

Exact-version native API fixes; keyword/tag extraction exclusions; managed OpenCC conversion; music metadata/cover/stream persistence; external audio and subtitle scanning with failure preservation; permission-preserving Chinese title search; native HTTP proxy routing and scoped credentials; custom UDP discovery; same-user completion progress handling; safe selected-item cleanup; real FFmpeg capture with framing, cancellation, timeout and atomic output; worker authentication/root/identity validation and fallback; cross-library progress/favorite separation with restoration when disabled; source label/quality ordering without mutating cached objects; configurable native folder version limit; basic local metadata cache, overrides, patches, field locks and local artwork. Native metadata refresh discovers and applies the local provider.

Additional code needing broader acceptance includes notification events, IntroDB public metadata import and per-library fingerprint duration. The notification monitor also subscribes to native collection membership events and publishes its own image/restore/cleanup changes.

## Remaining work — release is NOT complete

1. ISO information extraction and ISO/custom-encoder chapter/BIF paths using real disc fixtures.
2. STRM OpenSubtitles search and download acceptance.
3. Douban caching/application; Chinese-script-aware remote artwork; native TMDB defaults; traditional-Chinese metadata fallback.
4. Full local metadata coverage: people, external IDs, groups, rating mappings, special ordering and persistent image cache. The current normalized cache is not a claim of full upstream cache compatibility.
5. Second public intro source, native marker import through a full scan, actual fingerprint/per-library-duration tests.
6. Search recommendation suppression; episode totals, library badges, season/episode ordering, forced presentation styles, season-derived collections and reverse log display.
7. Real notification delivery and browser UI acceptance.
8. fnOS/Linux image verification, installation/backup/rollback scripts and final archive/license/hash checks.

**No complete installation package has been produced. Do not label this build as complete or full PRO parity.**

## Windows test operation

Use hidden process startup and **`-service -noautorunwebapp`**. The old interactive launch repeatedly opened browser pages; do not resume it. The current workspace runner stops its process after a report. The harness DLL is fixture-only and must never be shipped as a plugin.

## Reproduce build/checks

Use .NET 8 and the extracted official server distribution:
https://github.com/MediaBrowser/Emby.Releases/releases/tag/4.10.1.0

```powershell
dotnet build ./StrmAssistant/StrmAssistant.csproj -c Release -p:EmbyServerPath=C:/path/to/emby/system
dotnet build ./tests/IntegrationHarness -c Release -p:EmbyServerPath=C:/path/to/emby/system
dotnet run --project ./tests/Compatibility -- C:/path/to/emby/system ./StrmAssistant/bin/Release/net8.0 C:/path/to/ffmpeg.exe
```

Emby server assemblies are references only, never package contents. OpenCC data attribution/license are in `StrmAssistant/Data/OpenCC/`. GPL-3.0 and original notices are preserved.

Upstream community baseline: `beb65cf8e4d7b19ce418c3aa32cfb6eff04acfb2`, https://github.com/sjtuross/StrmAssistant.
The public PRO overview is a requirements reference, not a delivered-feature claim.
