# Changelog

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
