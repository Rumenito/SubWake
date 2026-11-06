# Changelog

## 2.0.1

- **Fix:** the burst is as loud as in 1.0 again. Since 1.1.0 the low tones were scaled so their *sum* peaked at the volume setting, about 6 dB quieter, and some subs stopped waking. Now each tone plays at the set level and they add up, scaled only to stay just under 0 dBFS.
- **Faster calibration:** a step every burst length + 1 s instead of + 12 s, so a full run takes about a minute instead of four and a half.

## 2.0.0

The first public release, for any subwoofer with auto-standby.

- **New name and icon.**
- **First-run setup:** pick the output that feeds your sub, its standby time and a starting volume, with a test burst. No bursts play before setup is finished.
- **Your sub's standby time** (5, 10, 15, 20, 30 or 60 minutes). The interval choices, the sub timer and the calibration wait follow it.
- **Low tone frequency:** 25, 30, 35, 40, 50 or 60 Hz (35 Hz by default).
- **Choose which outputs count as audio** for wait for silence. The default still watches every output on the burst output's device.
- **Update check:** once a day, asks GitHub for the newest release and shows a banner. It can be turned off in Setup and in the app, and nothing installs by itself.
- New installs start at −40 dBFS. The 20 kHz tone now warns that pets can hear it.
- The installer shows what SubWake does, what it changes and its privacy note.
- MIT license, privacy policy, and builds on GitHub Actions.

## 1.1.0 and earlier

Private builds: WASAPI playback, wait for silence, holds while recording and while apps are open, away mode, calibration, activity timeline and build-time tests.
