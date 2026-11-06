# Troubleshooting

Known pitfalls and how to fix them. Back to the [README](../README.md).

## Not yet verified

- A full Windows restart on 1.0.1 or later, with the app installed the right way. Expected: the tray icon appears without opening the app, and the saved settings are shown.
- Away on real hardware over a full away time, and that the first touch wakes the sub. The rules are covered by the build-time tests.
- A renamed or unplugged-and-replugged output picked up through device notifications. Registration is checked by `--hardware`.
- Whether subs detect the 20 kHz tone.

## The sub still goes to standby

- **Check the log:** `Played … on '<device>'` lines should appear every interval.
  - **`Skipped:` lines** mean the output wasn't found or couldn't play. Check the Output setting and the cable.
  - **`Paused:` lines** name the reason: snooze, PC locked, quiet hours, away, recording in an app, or a hold app being open.
- **Music was playing, but the sub fell asleep anyway:** the music may be quieter than the sub needs, yet still above **Audio counts from**, so it held the bursts. Run Calibrate…, then **Match calibration**, or raise the threshold.
- **Raise the volume or run Calibrate…** at your usual Windows, interface or amp, and sub volume. A stronger input signal to the sub, with the sub's own level lowered, can also help.
- **Try another frequency** for the low tone (40 or 50 Hz suits some subs), or add 20 Hz or 20 kHz.
- **Check the standby time** under Timing. If it's longer than your sub's real one, the bursts come too late.

## A burst plays while music is on

- Check that **Wait for silence** is on, and that the output carrying the music has a chip that lights up while it plays.
- **The level bar moves but the dot stays dark:** the music peaks below **Audio counts from**. Lower the threshold, or use **Match calibration**.
- **No chip lights up:** the music is going through an ASIO driver, or comes in through its inputs, so Windows' meter can't see it. Add the player or DAW to **Hold while these apps are open**, or play it through a Windows output.
- **Chips show an error:** Windows' audio service couldn't be read. Bursts then run on the normal schedule.

## Bursts stopped and the status says Paused

- **"recording in *app*":** an app has an input open. Voice chat apps often keep the mic open all the time. Close it, or turn off **Hold while recording**.
- **"*app* is open":** remove the app from **Hold while these apps are open**.
- **"you're away":** no keyboard, mouse or audio for the away time. Any input ends it, or turn off **Let the sub sleep when you're away**.

## Output busy

- The status says "Output busy", and the log says "'*name*' is in use by another app in exclusive mode."
- **Cause:** an app (often a DAW, or a player in exclusive mode) took the output for itself, so no other sound can play on it.
- **Fix:** turn off exclusive mode in that app, or add it to **Hold while these apps are open**. Bursts retry every 60 s.

## Output not found

- The status says "Output not found", or the Output card shows "(not connected)".
- **Cause:** the device is unplugged or disabled. The output is found by its Windows ID first, so a rename doesn't cause this. A reinstalled driver can give it a new ID; the name is then used, and the new ID is saved.
- **Fix:** pick the right device in the Output list. Bursts retry every 60 s.

## Audio on one output holds bursts, but that output doesn't reach the sub

By default SubWake watches every output on the burst output's device, for example headphones and speakers on the same sound card. Use **Listening → Choose outputs…** and turn off the outputs that don't feed the sub.

## Update check says "Couldn't reach GitHub"

The PC is offline, or a firewall blocks `api.github.com`. Keep-awake isn't affected. It tries again in an hour; or turn **Check for updates** off and check the [releases page](https://github.com/Rumenito/SubWake/releases) yourself.

## "Windows protected your PC" when installing

The installer isn't code-signed yet. Click **More info**, then **Run anyway**. Check the download against the SHA-256 checksum in the release first, if you like.

## The window shows "needs Microsoft WebView2"

The WebView2 runtime is missing or failed to start. Keep-awake still runs from the tray. Install the WebView2 Runtime from Microsoft, then reopen the window.

## Build fails at "Tests"

- The build prints the `FAIL` lines. The full output is in `Working\logs\tests.log`, and compile errors in `Working\logs\tests-build.log`.
- Fix the rule or update the test, then build again. Don't skip the step.

## Build fails at "Publish"

- **Read `Working\logs\publish.log`.**
- **Empty log, or "The command could not be loaded":** the `dotnet` found has no SDK. The build skips runtime-only installs, so make sure `Toolchains\dotnet\sdk\` exists. See [Workflow](WORKFLOW.md#toolchain).
- **`Working\payload` files can't be deleted:** a test copy is still running from it. Run `Working\payload\SubWake.exe --stop`.

## Setup can't replace files

Setup runs `--stop` on the installed copy before copying files. If that fails, choose **Exit** from the tray icon and run Setup again.
