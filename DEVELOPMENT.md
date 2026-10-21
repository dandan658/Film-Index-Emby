# Film Index Emby — development status

Target: Emby 4.10.1.0. This is a community-source derivative, not StrmAssistant PRO.
Upstream: https://github.com/sjtuross/StrmAssistant
Baseline: beb65cf8e4d7b19ce418c3aa32cfb6eff04acfb2
License and upstream copyright notices remain in LICENSE and source files.

## Completed in this working copy

- Installed a local .NET 8 SDK outside the source tree.
- Compiles against official Emby 4.10.1.0 server assemblies, not an approximate NuGet SDK.
- Updated chapter filtering, static media-source overload, mount metadata, external subtitle resolver, thumbnail arguments and fingerprint task result handling.
- Distinct plugin ID and assembly name; excluded upstream automatic binary replacement.
- Added independently implemented keyword/tag exclusions at media information, thumbnail and fingerprint entry points.
- Eighteen assembly and exclusion-rule checks pass. These do not certify successful media processing.

## Not completed

All PRO feature parity is **not implemented**. Remaining independent implementations include search/database integration, network discovery and proxy controls, ISO/music/image workflows, distributed workers, local metadata services, subtitle/audio enhancements, multiversion preferences, notifications and interface enhancements. Each needs separate acceptance tests; merely exposing an option does not count as implementation.

The PRO feature overview is a requirements reference, not an implementation:
https://github.com/sjtuross/StrmAssistant/wiki/PRO版的功能

The adapted baseline loaded in an isolated official Windows Emby 4.10.1.0 process and the server completed startup. The public system-info endpoint confirmed 4.10.1.0. No initialization warning remained after removing the legacy private monitor-field mutation. The subsequent blacklist extension passed compilation and unit checks, but has not been exercised through a configured media library. No production NAS was accessed. Test server and tray processes were stopped.

User deployment target: fnOS Docker; supplied image name/version `amilys/embyserver 4.10.1.0`. This third-party Linux image has not been tested. No changes to its licensing mechanisms are part of this derivative.

No final installation package has been approved by tests. Do not present this build as the complete requested product.

## Reproduce compilation and checks

Obtain the official 4.10.1.0 server distribution from:
https://github.com/MediaBrowser/Emby.Releases/releases/tag/4.10.1.0

With .NET 8 SDK installed, supply the extracted system directory:

```powershell
dotnet build ./StrmAssistant/StrmAssistant.csproj -c Release -p:EmbyServerPath=C:/path/to/emby/system
dotnet run --project ./tests/Compatibility -- C:/path/to/emby/system ./StrmAssistant/bin/Release/net8.0
```

Emby server assemblies are references only. They must not be included in a plugin package. The current community ChineseConverter dependency also requires a redistribution review or replacement before packaging.
