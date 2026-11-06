# Contributing

Thanks for helping. SubWake is small on purpose, so the best contributions keep it that way.

- **Bugs:** open an [issue](../../issues/new?template=bug-report.yml). Include your Windows version, the output names and the last lines of `%LOCALAPPDATA%\SubWake\subwake.log` (the window's **Open log** link).
- **Your subwoofer works (or doesn't):** use the [subwoofer report](../../issues/new?template=subwoofer-report.yml), so it can be added to the compatibility list.
- **Code:** open a pull request. Every pull request is reviewed by the maintainer before it's merged.
  - Read the docs in [Docs](Docs/) first: they hold the design rules and how the app works.
  - Timing and pause rules live in `Source/Desktop/Schedule.cs`, with no Windows calls, and every rule has a test in `Source/Tests/Tests.cs`. The build stops on a failing test.
  - App changes bump `<Version>` in `Source/Desktop/SubWake.csproj` and add a line to [CHANGELOG.md](CHANGELOG.md).
  - Run `powershell -ExecutionPolicy Bypass -File Tools\Build.ps1` before opening the pull request.

By contributing, you agree that your contribution is licensed under the [MIT license](LICENSE).
