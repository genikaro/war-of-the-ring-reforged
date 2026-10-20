# Project workflow

- Each implemented change set must increment `BuildInfo.Number` in `prototype/BuildInfo.cs` once. The first numbered change set is build-1. Keep the number stable while fixing or validating that same change set; compiling alone does not increment it.
- Keep the top-right build/date/FPS display connected to that single build number.
