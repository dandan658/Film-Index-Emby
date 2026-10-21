# Film Index Emby — development checkpoint

Target: Emby **4.10.1.0**. Build: **0.1.0.0-development**. GPL community derivative with independent additions. This is not a modified or authorized StrmAssistant PRO binary.

## Verification

- Exact official 4.10.1.0 compilation passes, using both portable and Windows reference distributions.
- **49** assembly, rule and real-FFmpeg checks pass.
- **117** fixture integration/contract checks pass inside isolated official Windows 4.10.1.0.
- **8** JavaScript assertions pass; the generated native module also passes Node syntax validation.
- Reports: `tests/results/`. These numbers are not feature counts or proof of full parity.
- Generated media and disposable accounts only; no production NAS was accessed.
- No local Docker or usable WSL. fnOS `amilys/embyserver` deployment remains untested.
- All fixture servers launch hidden with `-service -noautorunwebapp` and stop after their report. Never launch the old interactive test server.

## Implemented and exercised

Exact-version API fixes; keyword/tag extraction exclusions; managed OpenCC conversion; music metadata/cover/stream persistence; external audio and subtitle scanning with failure preservation; permission-preserving Chinese title search; native proxy routing and scoped credentials; custom UDP discovery; same-user completion progress handling; selected-item cleanup; real FFmpeg capture with framing, cancellation, timeout and atomic output; authenticated extraction worker with root/identity/size validation and fallback; cross-library progress/favorite separation through actual physical library roots; version labels/quality ordering and configurable folder merge limit.

Local metadata supports Movie/Series/Season/Episode/BoxSet cache and overrides, patches, field locks, local artwork, external IDs, official/critic ratings, studios, production locations and special-episode display ordering. Conflicting identities are rejected before mutation. This normalized format does not claim full upstream cache compatibility.

Presentation adds search-history suppression, series/season episode totals, per-user library badges, descending seasons/episodes, season-linked collections and separate web season/episode styles. Web code wraps the installed native module; no copied Emby module is distributed. Reverse log viewing preserves stack traces, admin authorization and native sanitization; raw downloads stay chronological.

Native TMDB adaptation adds Chinese fallback languages, script conversion and TMDB defaults for newly created libraries. Artwork ordering respects explicit simplified/traditional labels across providers and preserves unlabelled ambiguity; no visual OCR is claimed.

STRM subtitle requests resolve the referenced source and require exact IMDb/season/episode identity for remote URLs. Native OpenSubtitles account, download and server permissions remain intact. Actual online search/download still needs acceptance with a valid external account.

IntroDB.app and TheIntroDB v4 have separate ID models. The latter requires TVDB IDs, pinned episode IDs and a corroborated release duration. Ambiguous split intro sequences are not collapsed into Emby's single pair. A real native refresh persists fixture markers to the chapter repository; live third-party coverage is untested.

Two generated audio/video files produce real native fingerprints concurrently, retaining separate 2/3-minute library settings. Scheduled workers now drain owned work and reset state on cancellation/failure; a canceled task can no longer skip its semaphore-release callback.

Douban supports independent JSON-LD parsing, subject/IMDb cache lookup, offline assistance with field locks and identity checks, and an optional single-thread rate-limited cache task for manually linked subject IDs. Native refresh and offline scheduled application pass. A live public page returned no usable JSON-LD, so online build is not accepted as verified. No access restriction bypass is implemented.

Notifications use a recording native-interface sink; external delivery is untested. Distributed extraction uses an actual HTTP round trip inside one fixture server; cross-host operation is untested.

## Remaining work — release is NOT complete

1. ISO/BDMV media information, MPLS/audio/subtitle details, DV P7, custom encoder chapter/BIF paths and real disc fixtures.
2. Complete Douban IMDb discovery, season-table construction, cast localization, poster fallback and richer rating update behavior; live acceptance.
3. Full local metadata people, groups, advanced ratings, special ordering compatibility and persistent image cache.
4. Live second-source marker import, full-season fingerprint matching, broader parallel cancellation/load acceptance.
5. Actual OpenSubtitles download, notification delivery and native DOM/client UI acceptance.
6. Linux/fnOS verification, installation/backup/rollback scripts, archive/license/source/hash validation.

**No complete installation archive is produced or delivered. Do not relabel this checkpoint as full parity.**

## Reproduction

Use .NET 8 and official server distribution 4.10.1.0:
https://github.com/MediaBrowser/Emby.Releases/releases/tag/4.10.1.0

```powershell
dotnet build ./StrmAssistant/StrmAssistant.csproj -c Release -p:EmbyServerPath=C:/path/to/emby/system
./tests/run-windows-integration.ps1 -EmbySystemPath C:/path/to/windows/emby/system -DotNet C:/path/to/dotnet.exe
node --check C:/fixture-root/listcontroller-patched.js
node ./tests/web-presentation.cjs C:/fixture-root
dotnet run --project ./tests/Compatibility -- C:/path/to/emby/system ./StrmAssistant/bin/Release/net8.0 C:/path/to/ffmpeg.exe
```

Fixture roots are printed in the report and kept for diagnosis. Test harness DLL and official server assemblies must never be included in an installation archive. OpenCC attribution is in `StrmAssistant/Data/OpenCC/`; GPL and original notices are preserved.

Community baseline: `beb65cf8e4d7b19ce418c3aa32cfb6eff04acfb2`, https://github.com/sjtuross/StrmAssistant. Public PRO documentation is a requirements reference, not a claim of implemented parity.
