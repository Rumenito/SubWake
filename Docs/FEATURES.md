# Features

Everything the app does, with defaults and edge cases. Back to the [README](../README.md). Exact settings keys and ranges are in [State and protocol](STATE-AND-PROTOCOL.md).

## First-run setup

A new install (no `settings.json`) opens the window with a setup dialog, even when started with `--startup`. Until it's finished, no bursts play and the status reads "Paused · finish setup".

1. **Which output feeds your subwoofer?** All active outputs. Windows' default output is preselected and marked "(Windows default)".
2. **When does your sub go to standby?** 5, 10, 15, 20, 30 or 60 min (15 preselected).
3. **Wake sound volume**, −40 dBFS to start, and **Play test burst** (plays on the chosen output, at once).

**Start SubWake** (enabled once an output is chosen) saves `setupDone`, logs "Setup finished: …" and plays the first burst. Esc doesn't close the dialog; closing the window is fine, and setup shows again next time.

## Window controls

| Control | Default | Behaviour |
|---|---|---|
| Keep subwoofer awake | On | Master switch. Turning it on plays a burst straight away. |
| Play burst now | — | Plays one burst. Disabled while a burst plays ("Playing…") or during calibration. Doesn't wait for silence. |
| Pause for 30 min · 1 h · 2 h | — | Under Play burst now. Snoozes bursts, then resumes by itself. While snoozed the row reads "Paused until *hh:mm* · Resume now". Disabled when keep-awake is off. Not saved: a restart ends a snooze. |
| Wake sound volume | −40 dBFS | Range −60 to −6 in whole dB. Sets the **combined peak** of the low tone and the 20 Hz tone (see [Burst sound](#burst-sound)). Drag (saves on release), arrow keys, or mouse wheel over the slider at 1 dB per notch (shown at once, saved 350 ms after scrolling stops). Incoming state never moves the slider while it has focus or a wheel save is pending. A green mark shows where the sub woke in the last calibration, labelled "Woke at −*X*". |
| Calibrate… | — | Opens the calibration dialog (see below). Disabled while calibrating. |
| Low tone | On | The main keep-awake tone. |
| Frequency | 35 Hz | 25, 30, 35, 40, 50 or 60 Hz. Shown only while the low tone is on. |
| 20 Hz tone | Off | *Experimental*: many subs reproduce it poorly. Try it if the low tone doesn't keep the sub awake. |
| 20 kHz tone | Off | *Experimental*: above human hearing, fixed at −40 dBFS, also reaches the main speakers' tweeters. Its text warns that dogs and cats can hear it. |
| Your sub sleeps after | 15 min | 5, 10, 15, 20, 30 or 60 min: the sub's auto-standby time. Sets the interval choices, the sub timer and the calibration wait. |
| Burst every | 10 min | The choices for the standby time, all under it: 5 min → 2, 3, 4; 10 → 3, 5, 7, 9; 15 → 5, 8, 10, 12, 14; 20 → 5, 10, 15, 18; 30 → 10, 15, 20, 25, 28; 60 → 15, 30, 45, 55. A standby change moves the interval to the closest choice. |
| Burst length | 1.5 s | 1, 1.5 or 2 s, including 0.2 s fades. |
| Output | Chosen in setup | Any active playback device, saved by its Windows endpoint ID (see [Output](#output)). A saved device that isn't present stays in the list as "*name*  (not connected)". |
| **Listening card** | | |
| Wait for silence | On | No bursts while audio plays on the watched outputs (see [Wait for silence](#wait-for-silence)). Below it, one chip per watched output with a live level bar and a mark at the threshold. The dot is lit while that output carries audio that counts. Under it, a row **Outputs that count as audio** reads "Automatic: every output on the same device as your sub's output (*N* now)." or "Your choice: *N* outputs, including your sub's output.", with a **Choose outputs…** button. The button opens a list of every output with a switch: the burst output is locked on, **Use automatic** goes back to the default, **Done** closes the list. Chips, the list and the threshold slider are hidden when off. |
| Audio counts from | −50 dBFS | Range −70 to −10. Only audio whose peak reaches this level holds bursts. **Match calibration (−*X* dBFS)** appears when a calibration result exists and sets the threshold to it. The calibration mark shows on this slider too. |
| Hold while recording | On | No bursts while any app records from any input (see [Holds](#holds-recording-and-apps)). Shows "Recording now: *apps*" underneath while it's holding. |
| Hold while these apps are open | None | **Add app…** lists apps with a window; pick one to add it. Each app is a chip with ×. The chip's dot is lit while it runs. |
| **Options card** | | |
| Let the sub sleep when you're away | On, 30 min | No bursts after 15, 30, 60 or 120 min without keyboard, mouse or audio (see [Away](#away)). The time choice shows only when it's on. |
| Start with Windows | On (set by Setup) | Reads and writes the per-user Run value directly, so it always shows the real state. |
| Burst after unlock, wake or return | On | Plays a burst on unlock, 5 s after resume from sleep (so the audio interface has time to come back), and when you come back from away. It only brings the next burst forward, never pushes it back, and still waits for silence. |
| Pause while locked | Off | No bursts while the PC is locked, so the sub can sleep. |
| Quiet hours | Off, 00:00–07:00 | No bursts inside the window, which may cross midnight. Local wall-clock time. The time fields show only when it's on. Start = end means no quiet hours. Bursts resume when the window ends. |
| Alert if output is missing | On | One balloon when the output disappears: "'*name*' isn't connected. Bursts resume when it's back." |
| Check for updates | On (set by Setup) | See [Update check](#update-check). |
| Open log · version · Check for updates · Quit app | — | Footer. **Check for updates** checks now and then reads "Checking…", "You have the latest version." or "Couldn't reach GitHub…". Quit exits the whole app, not just the window. |

**Tones:** any mix of the three can be on, but the last one that's on can't be switched off. It stays visibly on and locked.

**Hero area:**
- **Update banner** above the hero, when a newer release exists: "SubWake *x.y.z* is available. You have *a.b.c*." with **Download**, which opens the release page in the browser.
- **Summary line:** "35 Hz + 20 kHz · 1.5 s every 10 min · −40 dBFS".
- **Next:** a countdown, "Now" while playing, "After audio" while holding for music, "Off", or "Paused".
- **Last:** the time as HH:MM. Hovering shows the full burst description.
- **Output:** "Connected", or "Missing" in amber.
- **Audio:** "Playing" or "Silent" on the watched outputs. Hovering names the outputs.
- **Sub timer:** time since the sub last got signal (the later of the last burst and the last audio that counts), as "4:12 / 15:00" against the standby time, with a bar. The bar turns amber after 80 % of it. From the standby time on it reads "Likely asleep". "—" until the first burst or audio.
- The last error, if any, shows in a line underneath.

**Activity card ("ACTIVITY · LAST 6 HOURS"):** a strip with a tick per burst, green bands for music and hatching for pauses, an axis (6 h ago, 4 h, 2 h, Now), a legend, and "Today: *N* bursts · music *1 h 10 min*" since midnight. Hovering a tick or band shows its time. The history is rebuilt from the log at start, so it survives restarts.

**Level note:** the sound that reaches the sub also depends on the Windows volume for that output, the interface or amp, and the sub's gain. Calibrate at your usual settings.

## Burst sound

- Sine tones with 0.2 s raised-cosine fades, played at the output's own sample rate and written to every channel.
- **Low tones (35 Hz, 20 Hz) share the volume as a peak.** When both are on, they're scaled together so the mix peaks at the set level instead of adding up past it. Each one is then about 6 dB quieter than it would be alone. Earlier versions let two tones at −6 dBFS reach 0 dBFS and clip.
- The 20 kHz tone is added on top at −40 dBFS. The burst never clips.
- Log text: "35 Hz at −40 dBFS for 1.5 s", or "35 Hz + 20 Hz at −6 dBFS peak + 20 kHz at −40 dBFS for 2 s".

## Schedule

- Timing uses the system tick counter, not the wall clock, so daylight saving changes and time syncs never make a burst late. Quiet hours, the log and the activity history use local time.
- The first burst plays right after the app starts, unless audio is playing (see below).
- After a burst plays, the next one is due *interval* minutes later.
- If a burst fails, the next try is 60 s later. Each distinct error message is logged once, until a burst succeeds.
- Changing the interval reschedules from the last burst, or plays now if that time has already passed.
- Changing the output clears the error and the alert, and tries the new output now.
- Burst parameters are taken when a burst starts. Changes apply to the next burst.

## Output

- Saved by Windows endpoint ID, with the name as a fallback. The app finds it by ID first, then by exact name, then by a name that contains the saved one.
- Settings from 1.0.x had only a name. The first time it's found, the ID is saved and the log says "Output remembered by its Windows ID: '*name*'."
- If the output is renamed in Windows, the new name is saved and logged: "Output renamed to '*name*'."
- The device list comes from the audio monitor, which Windows notifies when outputs are added, removed, enabled, disabled or renamed. As a safety net it also re-scans every 60 s (every 5 s if notifications aren't available).

## Wait for silence

Audio that reaches the sub keeps it awake by itself. A burst during music would only intrude, so the app waits for silence.

- **Watched outputs, automatic (default):** the burst output and every active playback output on the same device. Windows names outputs "*output* (*device*)", so for "Out 1/2 (CONNECT 6)" that's every output containing "(CONNECT 6)", and for "Speakers (Realtek(R) Audio)" every one containing "(Realtek(R) Audio)". If the name has no "(…)" part, only outputs whose name contains it are watched.
- **Watched outputs, chosen:** **Choose outputs…** saves a list of endpoint IDs (`watchOutputIds`). The burst output always counts. Use it when an output on the same device doesn't reach the sub (headphones), or one on another device does.
- **Signal:** a Windows peak meter reading at or above **Audio counts from** (default −50 dBFS), sampled every 50 ms. Quieter audio (a fade tail, faint background sound) doesn't hold bursts, because it may not keep the sub awake either. Best set from calibration with **Match calibration**.
- **Rule:** the next burst is due no earlier than *last signal + interval*. While audio plays, the burst keeps moving back, and it plays one full interval after the audio stops. Unlock, wake, return and startup bursts also wait. **Play burst now** doesn't.
- **Own bursts don't count:** while a burst plays (and 0.5 s after), the burst output is read per app and the app's own sound is left out, so music on that output still counts. Other outputs are read as usual. If per-app meters aren't available, the burst output is skipped during the burst.
- **Turning it off** drops any wait. The next burst is due *interval* after the last burst, or after now if there hasn't been one.
- **Log:** "Audio detected on *outputs*." when audio starts, and "Audio stopped." after 15 s of silence, so the gaps between tracks don't add lines.
- **Status:** "Active · holding for music" with a softly pulsing green dot. Tray: full-colour icon with a green dot, tooltip "SubWake · music playing, burst waits".
- **Not seen by the meter:**
  - audio played through an audio interface's ASIO driver, which bypasses Windows (use [Hold while these apps are open](#holds-recording-and-apps) for a DAW);
  - anything the interface mixes in from its inputs.

  Bursts then run on the normal schedule.
- **If metering fails or nothing matches:** the error is shown in place of the chips, and bursts run on the normal schedule.

## Holds: recording and apps

These stop bursts outright (the sub may fall asleep). When a hold ends, an overdue burst plays straight away, unless audio is playing.

- **Hold while recording:** checks every second for a running recording session on any active input (microphones and interface inputs), from any app but this one. The app is named by its file description or process name. Status: "Paused · recording in *app*". Apps that keep a microphone open all the time (voice chat) hold bursts the whole time; turn the option off if that's unwanted. Recording through ASIO isn't seen.
- **Hold while these apps are open:** process names, checked every 5 s. Status: "Paused · *app* is open", "*a* and *b* are open", or "*a* and *N* more are open". For a DAW that plays through ASIO, so its audio is never interrupted by a burst. While it's open and idle, the sub can sleep.

## Away

- **Away** means no keyboard or mouse input and no audio above −70 dBFS on the watched outputs for the chosen time. The app's own bursts don't count as activity.
- While away: status "Paused · you're away", no bursts, the sub sleeps. Overnight with the PC unlocked, this replaces bursts every few minutes.
- The first keyboard or mouse input ends it. With **Burst after unlock, wake or return** on, a burst plays at once (after any music) so the sub is ready.

## Status and tray

| Mode | Shown as | Tray icon |
|---|---|---|
| active | Active (green dot) | Full colour |
| holding | Active · holding for music (pulsing green dot) | Full colour, green dot |
| calibrating | Calibrating | Full colour |
| off | Off | Grey |
| paused | Paused · finish setup / snoozed until *hh:mm* / PC locked / quiet hours until *hh:mm* / you're away / recording in *app* / *app* is open | Grey |
| warning | Output not found / Output busy / Playback problem (amber) | Grey, amber dot |

- **Priority, highest first:** calibrating, off, paused, warning, holding, active.
- **Pause reasons,** highest first: setup not finished, snooze, PC locked, quiet hours, away, recording, open apps.
- **Warnings come from the last failed burst:**
  - **Output busy:** another app holds the output in exclusive mode, such as a DAW.
  - **Output not found:** the output is missing or was unplugged.
  - **Playback problem:** anything else, with the Windows error code in the error line.

**Tooltip:** "SubWake · next burst in *N* min" (at least 1), "· music playing, burst waits", or the status text. It's cut to 63 characters.

**Tray menu:**
- **Open SubWake** (bold)
- **Keep subwoofer awake** (a checkbox, same as the main switch)
- **Play burst now**
- **Pause for** ▸ 30 minutes / 1 hour / 2 hours / Resume now (enabled while snoozed)
- **Exit**

Left-clicking the icon opens the window.

**Window:**
- About 440 × 800 logical px at zoom 1.1, never taller than the screen's working area minus 60 px. Minimum 440 × 480.
- Opens where it was last closed, if that spot is still on a screen; otherwise centred. A second launch, or the tray, brings it forward and un-minimises it.
- The first time it's closed, a balloon explains the app is still in the tray. That's remembered in settings.

## Update check

- **When:** one minute after start, then once a day (`lastUpdateCheck`, local time; a clock set back counts as due). A failed check retries after an hour and is logged once per error. **Check for updates** in the footer checks at once.
- **What:** `GET https://api.github.com/repos/<GitHubRepo>/releases/latest` with the user agent `SubWake/<version>`. Drafts and pre-releases aren't "latest". No release yet (404) means up to date.
- **Newer:** the tag ("v2.1.0") is compared as a version with the running one. A newer one shows the banner, logs "Update available: SubWake *x.y.z*.", and shows one tray balloon per version ("Click to open SubWake and download it."; clicking opens the window).
- **Download** opens only `https://github.com/<repo>/…` pages. Nothing downloads or installs by itself.
- **Off:** the Options switch, or Setup's "Check for updates once a day" box. Setup writes its choice to `HKCU\Software\SubWake\CheckForUpdates`; the app applies it whenever it differs from the last applied choice, so a reinstall can turn it off and a later change in the window sticks.

## Calibration

Finds roughly the quietest level that wakes the sub, then lets the user choose.

1. **Start screen:** instructions and Start. Set Windows, the interface or amp, and the sub to your usual volume, and keep everything silent.
2. **Waiting:** a countdown of the standby time plus 1 minute (16 min for 15) so the sub can fall asleep. **It's asleep, start now** skips the wait. Any audio above −70 dBFS on the watched outputs starts the wait over, with the note "Audio played, so the wait started over. Keep everything silent."
3. **Stepping:**
   - Bursts of the low tones only (no 20 kHz) at −60, −57 … −6 dBFS, +3 dB per step.
   - Each step comes *burst length* + 12 s after the previous one.
   - A progress bar and the current level are shown.
   - Other audio during the test sends it back to waiting ("Audio played during the test, so it went back to waiting."). The app's own calibration bursts don't count.
   - The user clicks **Sub woke up**.
4. **Done:**
   - **After Sub woke up:** "The sub woke at or just before *X* dBFS…". The suggestion is *X* + 6 dB, capped at −6, with two buttons: **Keep −*current* dBFS** and **Use −*suggested* dBFS**. *X* is saved as the calibration result (the slider marks and **Match calibration**); that changes no volume. The next regular burst is due one interval later.
   - **No wake-up by −6 dBFS:** a message to check that the output feeds the sub and that its volume or gain isn't down. The dialog offers Close, and a burst plays now.

Rules:
- Regular bursts, holds, burst-after-wake and wait-for-silence are suspended while waiting or stepping.
- **Cancel**, or Esc while running, stops calibration and plays a burst now to wake the sub (after any music).
- Esc or Close when not running just closes the dialog.
- If the window is opened during calibration, the dialog reopens at the current step.
- The volume changes only on **Use**.

## Single instance and command line

- **No arguments:** opens the window. If a copy is already running, it brings that window forward instead.
- **`--startup`:** tray only. Used at sign-in. If a copy is already running, it exits without bringing the window forward.
- **`--stop`:** asks the running copy to exit, waits up to 10 s, then exits. Used by Setup and the uninstaller.
- **`--configure-startup=on|off`:** sets Start with Windows and exits. Exit code 1 on failure. Used by the uninstaller.

## Install and uninstall

- **Install:** per user, no admin needed, into `%LOCALAPPDATA%\Programs\SubWake`. Needs Windows 10 22H2 (build 19045) or later, x64.
- **Wizard pages:** a note on what SubWake does and changes, its privacy and how to uninstall (`Source/Packaging/Privacy.txt`), then the tasks.
- **Wizard tasks:**
  - **Start when I sign in:** checked. Upgrades keep the previous choice.
  - **Check for updates once a day:** checked.
  - **Desktop shortcut:** unchecked.
- **Shortcuts:** a Start menu shortcut is always created.
- **Before installing**, Setup:
  - warns, but continues, if WebView2 is missing;
  - stops a running copy;
- **After installing**, Setup offers to open the app. A silent install starts it with `--startup`.
- **Uninstall:** stops the app and removes the Run value and `HKCU\Software\SubWake`. It **keeps** `%LOCALAPPDATA%\SubWake` (settings and log).
- **Upgrades:** installing a newer version over an older one keeps all settings. New settings get their defaults.
