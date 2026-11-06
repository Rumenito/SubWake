# State and protocol

Settings schema, stored data, system names and the window ↔ host messages. Back to the [README](../README.md).

## Files

| Path | Contents |
|---|---|
| `%LOCALAPPDATA%\SubWake\settings.json` | Settings: camelCase, indented. Saved on every change by writing `settings.json.tmp`, then moving it over. |
| `%LOCALAPPDATA%\SubWake\subwake.log` | Log (see below). Over 1 MB it's moved to `subwake.log.old` (one generation). The activity timeline is rebuilt from it at start. |
| `%LOCALAPPDATA%\SubWake\WebView2\` | WebView2 user data folder |
| `%LOCALAPPDATA%\Programs\SubWake\` | Installed app: `SubWake.exe`, `SubWake.ico`, `LICENSE.txt`, `ui\`, `licenses\`, runtime |
| `SUBWAKE_DATA` environment variable | Points a test copy at another data folder |

Uninstall removes only the install folder.

## Settings

| Key | Type | Default | Validation (`Normalize`) |
|---|---|---|---|
| `enabled` | bool | `true` | — |
| `setupDone` | bool | `false` | First-run setup finished. Before that, no bursts (pause reason "finish setup"), and the window opens even with `--startup` |
| `subStandbyMinutes` | number | `15` | Snapped to the closest of 5, 10, 15, 20, 30, 60. Sets the interval choices, the sub timer and the calibration wait (+1 min) |
| `toneLow` | bool | `true` | The main low tone. If all three tones are off, `toneLow` is set on |
| `lowToneHz` | number | `35` | Snapped to the closest of 25, 30, 35, 40, 50, 60 |
| `tone20` | bool | `false` | as above |
| `tone20k` | bool | `false` | as above |
| `levelDb` | number | `-40` | Clamped to −60…−6, rounded to whole dB. The peak of the low tones together |
| `intervalMinutes` | number | `10` | Snapped to the closest choice for the standby time (`Settings.IntervalsFor`): 5 min → 2, 3, 4; 10 → 3, 5, 7, 9; 15 → 5, 8, 10, 12, 14; 20 → 5, 10, 15, 18; 30 → 10, 15, 20, 25, 28; 60 → 15, 30, 45, 55 |
| `durationSeconds` | number | `1.5` | Snapped to the closest of 1, 1.5, 2 |
| `deviceId` | string or null | `null` | Windows endpoint ID. Blank → `null`. Filled in by the app when the output is found |
| `deviceName` | string | `""` | Trimmed. Chosen in setup. Kept current on rename; the fallback when the ID is gone |
| `skipWhileAudio` | bool | `true` | — (wait for silence) |
| `watchOutputIds` | string array or null | `null` | `null`: every output on the burst output's device. A list: only those endpoint IDs, plus the burst output, which always counts. Blanks and duplicates dropped, at most 32 |
| `signalThresholdDb` | number | `-50` | Clamped to −70…−10, rounded. "Audio counts from" |
| `holdWhileRecording` | bool | `true` | — |
| `holdApps` | string array | `[]` | Folder and `.exe` removed, blanks and case-insensitive duplicates dropped, at most 20 |
| `sleepWhenAway` | bool | `true` | — |
| `awayMinutes` | number | `30` | Snapped to the closest of 15, 30, 60, 120 |
| `burstAfterWake` | bool | `true` | — (unlock, wake or return) |
| `pauseWhenLocked` | bool | `false` | — |
| `quietHours` | bool | `false` | — |
| `quietStart` | `"HH:mm"` | `"00:00"` | Invalid → default |
| `quietEnd` | `"HH:mm"` | `"07:00"` | Invalid → default |
| `notifyDeviceMissing` | bool | `true` | — |
| `checkForUpdates` | bool | `true` | The daily update check |
| `installerUpdateChoice` | number or null | `null` | The installer's last update-check choice that was applied (see the registry below). A different value from the registry overrides `checkForUpdates` once |
| `lastUpdateCheck` | local date-time or null | `null` | Last automatic check. A failed check is retried after 1 h |
| `updateNotifiedVersion` | string or null | `null` | The newer version the tray balloon was shown for, so it shows once per version |
| `calibratedWakeDb` | number or null | `null` | Clamped to −60…−6. Where the sub woke in the last calibration |
| `trayHintShown` | bool | `false` | Set after the first window close |
| `windowBounds` | `[x, y, width, height]` or null | `null` | Anything but 4 numbers → `null`. Saved on window close, in device pixels |

- **Fixed value:** the 20 kHz level is `HighToneDb = −40` and isn't a setting.
- **Not saved:** a snooze ends when the app restarts.
- **Unreadable or invalid file:** defaults are used and the problem is logged.
- **Merges:** `Settings.With(changes)` applies only known keys, then normalises. Unknown keys are ignored. Keys missing from an older file get their defaults.
- **Adding a setting:**
  1. Add the property with its default to `Settings`.
  2. Add validation to `Normalize`.
  3. Add a log line in `ApplySettings` if it should be logged.
  4. Add a control with `data-setting="<camelKey>"` for a switch, or a handler in `app.js`.
  5. Add a test in `Source/Tests/Tests.cs`.

## System names

| Kind | Name |
|---|---|
| Single-instance mutex | `Local\SubWake` (a test copy with `SUBWAKE_DATA` set: `Local\SubWakeTest`) |
| Show-window event | `Local\SubWakeShow` (test copy: `…ShowTest`) |
| Stop event | `Local\SubWakeStop` (test copy: `…StopTest`) |
| Autostart | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `SubWake` = `"<exe>" --startup` |
| Installer choices | `HKCU\Software\SubWake`, DWORD `CheckForUpdates` (1 or 0), written by Setup on every install, removed on uninstall |
| Installer AppId | `SubWake`. Uninstall key `SubWake_is1` under HKCU |
| WebView2 virtual host | `https://subwake.local/` → `<app>\ui` (DenyCors) |
| Update check | `GET https://api.github.com/repos/<GitHubRepo>/releases/latest`, user agent `SubWake/<version>`. `GitHubRepo` is a property in `SubWake.csproj`, read through `AssemblyMetadata` |

## Window messages

**Page → host** (`chrome.webview.postMessage`):

| `type` | Fields | Effect |
|---|---|---|
| `ready` | — | Marks the window ready and pushes state |
| `set` | `settings`: partial object | Merge, validate, save, log, reschedule, push. The output is set as `{deviceId, deviceName}` together |
| `startup` | `value`: bool | Write or delete the Run value |
| `play-now` | — | Burst now, unless one is playing or calibration is running |
| `snooze` | `minutes`: 30, 60 or 120 | Snooze bursts |
| `resume` | — | End a snooze |
| `list-apps` | — | Replies with `apps` |
| `calibrate` | `action`: `start`, `skip-wait`, `woke`, `apply`, `cancel`, `close` | Calibration step. Each action applies only in its phase |
| `open-log` | — | Opens the log in the default editor. Creates it first if missing |
| `check-updates` | — | Runs the update check now and reports the result in `update.status` |
| `open-update` | — | Opens the newer release's page (only `https://github.com/<repo>/…` URLs) |
| `quit` | — | Exits on the next tick, outside the browser callback |

**Host → page** (`PostWebMessageAsJson`, camelCase). Times are wall-clock Unix milliseconds or `null`.

`type: "state"` is sent on every change, every 2 s, and on `ready`:

| Field | Meaning |
|---|---|
| `version` | App version, without the build suffix |
| `settings` | The full settings object |
| `startup` | Whether the Run value exists |
| `status` | `{mode, text}`. Modes: `active`, `holding`, `off`, `paused`, `warning`, `calibrating` |
| `nextBurstAt` | `null` when off, paused or calibrating |
| `lastBurstAt`, `lastBurstText` | The last successful burst |
| `lastError` | Last error text, or `null` |
| `playing` | Whether a burst is playing |
| `devices` | `[{id, name, watched}]`, active playback outputs; `watched` if wait-for-silence watches it |
| `defaultOutputId` | Windows' default output, only before setup is done |
| `intervals` | The interval choices for the standby time |
| `update` | `{available: newer version or null; checking; status: text after a manual check}` |
| `deviceFound` | Whether the saved output is present (`true` until the monitor's first scan) |
| `audio` | `{watching: output names; active: outputs above the threshold in the last 3 s; recording: app names; holding: waiting for silence now; error: text or null}` |
| `subTimerFrom` | Later of the last burst and the last audio that counts |
| `subStandbyMs` | The standby time in ms |
| `snoozedUntil` | End of the snooze, or `null` |
| `holdAppsOpen` | Hold apps running now |
| `history` | `{from, to, bursts: [ms], music: [[start, end or null]], paused: [[start, end or null]], today: {bursts, musicMinutes}}`, the last 6 hours |
| `calibration` | `{phase: none, waiting, stepping or done; waitUntil (waiting only); level (last played); suggestedDb; startDb: −60; maxDb: −6; waitMinutes; message; note}` |

`type: "levels"`: `{levels: {"<output name>": dBFS}}`, the loudest reading on each watched output since the last message (−100 = silent). Sent every 250 ms while the window is open.

`type: "apps"`: `{apps: [{name: process name, title: window title}]}`, apps with a window, sorted.

The page counts down locally each second between pushes.

## Log

The format is `yyyy-MM-dd HH:mm:ss  message`, with two spaces, the same as the earlier PowerShell version. Logging errors are swallowed. `Activity.FromLog` reads the lines marked ⏱ to rebuild the timeline.

- ⏱ `Started v<ver>: <tones> for <s> s every <n> min on '<device>'.`, plus ` Keep-awake is off.` when off
- ⏱ `Played <tones> for <s> s on '<device>' (scheduled|manual|calibration).`. Tones read `35 Hz at -30 dBFS`, or `35 Hz + 20 Hz at -6 dBFS peak + 20 kHz at -40 dBFS`
- `Skipped: <error>`, once per distinct error
- ⏱ `Paused: <reason>.` (snoozed until HH:mm, PC locked, quiet hours until HH:mm, you're away, recording in *app*, *app* is open), ⏱ `Resumed.`
- `Snoozed for 30 minutes|1 hour|2 hours, until HH:mm.`, `Snooze ended.`
- `Keep-awake turned on.` / `off.`
- `Settings: …` (tones, level, duration, interval or device changed)
- `Output remembered by its Windows ID: '<name>'.`, `Output renamed to '<name>'.`
- ⏱ `Audio detected on <outputs>.`, ⏱ `Audio stopped.` (after 15 s of silence; the timeline ends the music 15 s earlier)
- `Audio counts from <n> dBFS.`
- `Hold while open: <apps>|no apps.`
- `Options: wait for silence on|off, hold while recording on|off, sleep when away after N min|off, burst after wake on|off, pause when locked on|off, quiet hours HH:mm-HH:mm|off, missing-output alert on|off.`
- `Start with Windows on|off.`
- `Calibration started…`, `Calibration: audio played, waiting for silence again.`, `Calibration: sub woke at or just before X dBFS; suggested Y dBFS.`, `Calibration: no wake-up up to -6 dBFS.`, `Calibration cancelled.`
- Errors:
  - `Settings could not be read, using defaults: …`
  - `Settings could not be saved: …`
  - `Window could not load (is Microsoft WebView2 installed?): …`
  - `Window message ignored: …`
  - `Log could not be opened: …`
  - `Start with Windows could not be changed: …`
  - `Unexpected error: …`
- `Log opened.` (only when the log didn't exist yet), ⏱ `Stopped.`
