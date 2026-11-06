# Workflow

Toolchain, build, test, install and release. Back to the [README](../README.md).

## Folder layout

| Folder | Contents | In git |
|---|---|---|
| `Source/` | App inputs: `Desktop/`, `UI/`, `Packaging/`, and `Tests/` (build-time tests) | Yes |
| `Tools/` | `Build.ps1`, `Make-Icon.ps1` | Yes |
| `Docs/` | Topic docs | Yes |
| `Toolchains/` | Project-local tools: `dotnet\` (.NET SDK 10.0.403, runtime 10.0.14) and `InnoSetup\` (Inno Setup 6.7.4, portable) | No |
| `Cache/` | `NuGet\` packages and `DotNetHome\` for the build | No |
| `Working/` | Generated: `bin\<project>\`, `obj\<project>\`, `tests\` (the built test runner), `payload\` (the published app), `logs\` (`tests-build.log`, `tests.log`, `publish.log`, `installer.log`) | No |
| `Builds/` | Installers `SubWake-Setup-<version>-x64.exe`. Old versions are kept. | `.exe` ignored |
| `.github/` | `workflows/build.yml` (CI build and draft releases), issue templates | Yes |

## Toolchain

`Tools/Build.ps1` looks for each tool in this order:

- **.NET SDK:** `[ROOT_DIR]\Toolchains\dotnet\dotnet.exe`, then `dotnet` on PATH. A `dotnet` without an `sdk\` folder (runtime only) is skipped.
- **Inno Setup:** `[ROOT_DIR]\Toolchains\InnoSetup\ISCC.exe`, then `Program Files (x86)\Inno Setup 6`, then `Program Files\Inno Setup 6`.

To rebuild `Toolchains\` on a new machine:

- **.NET SDK:** extract the official .NET 10.0.4xx SDK zip for win-x64 (from `builds.dotnet.microsoft.com`; check its SHA-512 against `release-metadata/10.0/releases.json`) into `Toolchains\dotnet`.
- **Inno Setup:** run the signed Inno Setup 6 installer (from `jrsoftware.org`) with `/VERYSILENT /CURRENTUSER /PORTABLE=1 /NOICONS /DIR="[ROOT_DIR]\Toolchains\InnoSetup"`. Run it from a normal folder, not the temp folder.

Packages restore from nuget.org into `Cache\NuGet`. Never point the build at another project's tools or caches.

## Build

```
powershell -ExecutionPolicy Bypass -File Tools\Build.ps1                 # app + installer in Builds\
powershell -ExecutionPolicy Bypass -File Tools\Build.ps1 -SkipInstaller  # app only, in Working\payload
powershell -ExecutionPolicy Bypass -File Tools\Make-Icon.ps1             # regenerate Source\Desktop\SubWake.ico
```

`Build.ps1`:

1. Reads `<Version>` from the `.csproj`.
2. Clears `Working\payload`. It refuses to delete anything outside `Working\`, and first `--stop`s a test copy running from there.
3. Builds and runs the tests (`Source/Tests`, into `Working\tests`). A failing test stops the build: the summary and any `FAIL` lines are printed, and the full output is in `Working\logs\tests.log`.
4. Publishes Release, win-x64, self-contained, ReadyToRun. The WebView2 DLLs are excluded from ReadyToRun so their Microsoft signatures stay intact, and the unused WPF reference is removed.
5. Copies the runtime's and WebView2's license and notice files into `payload\licenses`.
6. Unless `-SkipInstaller` is passed, copies `LICENSE` into the payload and compiles `Source/Packaging/SubWake.iss`. It passes in `AppVersion`, `PayloadRoot`, `OutputRoot` and `AppIcon`.

A full build takes about 3 minutes. The installer is about 44 MB.

**Icon:** `Make-Icon.ps1` draws a dark rounded square with a silver sine burst. It writes 16–128 px frames as bitmaps and 256 px as PNG.

**Debug build:** `dotnet build` produces a debug build that needs a system-wide .NET 10 runtime to launch. The published build carries its own.

## Tests

`Source/Tests` is a plain console program that references the app (through `InternalsVisibleTo`) and needs no test packages. It checks the pure rules:

- **Schedule:** first burst, interval, hold after audio, wake, retry, calibration bursts, interval change.
- **Pause rules:** the order of reasons and their text, away, the sub timer.
- **Settings:** clamping, snapping, at least one tone, hold-app cleaning, quiet hours across midnight, merges, log text.
- **Burst sound:** the low-tone peak rule, fades, sample rate, no clipping.
- **Devices:** finding by ID, name and truncated name; interface grouping; Windows error → status.
- **Activity:** rebuilding the timeline from log lines, today's counts, pruning.
- **Clock:** schedule time ↔ wall clock.

Run it by hand with `Working\tests\SubWake.Tests.exe` after a build. Extra modes, which aren't run by the build:

| Argument | Does |
|---|---|
| `--hardware` | Runs the tests, then lists every output and input with its endpoint ID, watches the Windows default output's device for 3 s and prints their levels, shows whether device notifications and idle time work, and starts a second copy with `--record-briefly` to check that recording is detected |
| `--tone "<output>" <dBFS> <seconds>` | Plays a 35 Hz test tone from a separate process, which the app should count as audio |
| `--record-briefly` | Opens the first input for 5 s |

When a rule changes, change or add its test in `Tests.cs`.

## Manual test

- **Quick run:** start `Working\payload\SubWake.exe` with `SUBWAKE_DATA` set to a folder under `Working\` (for example `Working\demo`). It then uses that folder for settings and log, leaves the real `%LOCALAPPDATA%\SubWake` alone, and uses its own single-instance names, so it runs beside the installed app. An empty folder shows first-run setup. Without `SUBWAKE_DATA`, it uses the real settings, so note them first.
- **Screenshots for the README:** start the copy as above and with the DevTools port (next point), then run `node Tools/Screenshot.mjs Docs/images/<name>.png [--eval "<js>"]`. Don't touch the window while it captures: a wheel over a slider changes the setting.
- **Driving the window from a script:** start the exe with the environment variable `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222`, then connect over the Chrome DevTools Protocol at `http://127.0.0.1:9222/json`. The page's global `state` holds the last host state, and `send()` posts messages. This works on the Release build.
- **Things to check after a UI or behaviour change:**
  - The volume slider and Calibrate are visible without scrolling.
  - The mouse wheel changes the level 1 dB per notch, saves after a pause, and stops at −6 and −60.
  - In calibration, "Sub woke up" doesn't change the volume, Keep leaves it alone, and Use applies the suggestion.
  - The last tone can't be switched off.
  - Wait for silence: all CONNECT 6 outputs are listed with level bars. A `--tone` at −55 dBFS on Out 3/4 moves its bar but doesn't light the chip at −50, and does at −60. The app's own burst on the burst output doesn't light it. Switching the option off brings back the countdown.
  - Hold while recording: `--record-briefly` shows "Paused · recording in SubWake.Tests" and clears afterwards.
  - Hold apps: adding `explorer` pauses ("explorer is open"), and × removes it. **Add app…** lists open apps.
  - Snooze 30 min shows "Paused until *hh:mm*", and Resume now ends it.
  - Calibration with audio playing notes that the wait started over.
  - No horizontal overflow, smallest text 10 px, stat labels fit.
  - Quit exits the app and saves the window position.
- **Screenshots over DevTools:** the window zoom is 1.1, so multiply the clip size from `Page.getLayoutMetrics` by 1.1, or the right edge is cut off.
- **Ending a test copy:** run `SubWake.exe --stop` with the same `SUBWAKE_DATA` set. Without it, `--stop` stops the installed app instead.

## Install and release

1. Bump `<Version>` in `Source/Desktop/SubWake.csproj` and add the changes to `CHANGELOG.md`.
2. Run `Tools\Build.ps1`.
3. Run the new installer from `Builds\` in the user's normal session. Installing over an older version keeps settings.
4. Check from outside any sandbox:
   - the uninstall entry `SubWake_is1` shows the new version;
   - the Run value exists;
   - `settings.json` is unchanged;
   - the log shows `Started v<new>`.
5. Update the docs for any behaviour change.

## Public release (GitHub)

1. Steps 1–2 above, and commit.
2. Tag and push: `git tag v<version>` then `git push origin main --tags`.
3. GitHub Actions (`.github/workflows/build.yml`) checks that the tag matches `<Version>`, builds and tests on `windows-latest`, and creates a **draft** release with the installer, its `.sha256` file and the changelog section.
4. Review the draft and publish it. Installed copies see it through the update check within a day.
5. **Code signing (once SignPath approves the project):** a signing step goes between the build and the release job. Each signing request is approved by hand in SignPath.

`GitHubRepo` in `SubWake.csproj` must match the repository (`owner/name`), or the update check can't find releases.

## Versions

| Version | Changes |
|---|---|
| 1.0.0 | First desktop app, replacing the PowerShell script: tray, window, tones, timing, options, calibration |
| 1.0.1 | Wake-sound volume card with slider and mouse wheel. Calibration only suggests (Keep / Use). Window zoom 1.1. |
| 1.0.2 | Builds with its own toolchain. .NET runtime 10.0.14. Calibration text says "Use" to match the button. |
| 1.0.3 | Wait for silence: no bursts while any CONNECT 6 output carries audio. Audio stat and output chips in the window. State push every 2 s. |
| 1.1.0 | WASAPI playback; output saved by Windows ID. Low tones share the volume as a peak (no clipping). Tick-counter schedule clock. Real error in the status (Output busy). Device-change notifications. Audio threshold (−50 dBFS) with Match calibration. Hold while recording, hold while apps are open, let the sub sleep when away, snooze from the tray and window. Sub timer, activity timeline, live level bars, tray status dots, window position memory, 10 px minimum text. Calibration goes to −6 dBFS, restarts on audio and remembers the result. Build-time tests. |
| 2.0.1 | Burst level back to 1.0 (each low tone at the set level), faster calibration. |
| 2.0.0 | Renamed SubWake, first public release: first-run setup, sub standby time, low tone frequency, chosen watched outputs, update check, new icon, MIT license, privacy note in Setup, CI builds and draft releases. |
