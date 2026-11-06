# Privacy policy

SubWake collects no personal data. It has no telemetry, analytics, ads or accounts.

## What stays on your PC

- **Audio levels:** SubWake reads Windows' peak meters on the outputs it watches, to know when audio is playing. It never records or stores audio.
- **Recording apps:** it checks which apps have a microphone or input open, so it can hold bursts while you record. It only keeps their names in memory.
- **Open apps:** it checks whether the apps you chose under "Hold while these apps are open" are running.
- **Keyboard and mouse:** it reads only how long ago you last used them (Windows' idle time), for "Let the sub sleep when you're away". It never reads what you type.
- **Settings and log:** saved in `%LOCALAPPDATA%\SubWake` (`settings.json`, `subwake.log`). The log lists bursts, pauses, audio start and stop, output names and the names of recording or hold apps. It never leaves your PC.

## The one network request: the update check

If **Check for updates** is on (you choose this during installation, and can change it any time in Options), SubWake asks GitHub once a day for the newest release:

- **Where it goes:** `https://api.github.com/repos/Rumenito/SubWake/releases/latest`.
- **What's sent:** an ordinary web request with the user agent `SubWake/<version>`. Like any web request, GitHub sees your IP address. Nothing else is sent: no settings, no identifiers, no usage data.
- **What happens next:** if there's a newer version, SubWake shows a banner and one notification. **Download** opens the release page in your browser. Nothing is downloaded or installed by itself.
- GitHub's handling of the request is covered by the [GitHub General Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

**Check for updates** in the footer runs the same check on request.

## Third-party components

SubWake bundles the .NET runtime and Microsoft WebView2 to draw its window. WebView2 is only used to show SubWake's own local page (`https://subwake.local`, served from the app's folder). No web content is loaded from the internet.

## Changes

Any change to this policy is listed in the [changelog](CHANGELOG.md) and shown in the installer.
