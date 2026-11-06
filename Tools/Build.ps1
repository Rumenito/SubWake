# Builds SubWake: publishes a self-contained Windows x64 app and packages it with Inno Setup.
# Needs the .NET 10 SDK and Inno Setup 6: from this project's Toolchains folder, or installed system-wide.
#   -SkipInstaller   Publish only (Working\payload), no Setup.exe.
param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$Workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot))

function Find-Tool([string[]]$Candidates, [string]$What) {
    foreach ($c in $Candidates) { if ($c -and (Test-Path -LiteralPath $c)) { return $c } }
    throw "$What not found. Looked in: $($Candidates -join '; ')"
}
function Assert-Exit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE). See the log in Working\logs." } }
function Clear-Generated([string]$Path) {
    # Only ever delete inside Working\.
    $resolved = [IO.Path]::GetFullPath($Path); $allowed = Join-Path $Workspace 'Working'
    if (-not $resolved.StartsWith($allowed + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to delete outside Working: $resolved" }
    # A test copy running from here keeps its files locked: ask it to exit and wait for it.
    $running = @(Get-Process SubWake -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($resolved + '\', [StringComparison]::OrdinalIgnoreCase) })
    if ($running.Count) { & $running[0].Path --stop; $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

# A dotnet install with only runtimes (no sdk folder) can't build, so skip it.
$dotnet = Find-Tool @(@(
    (Join-Path $Workspace 'Toolchains\dotnet\dotnet.exe'),
    (Get-Command dotnet -ErrorAction SilentlyContinue).Source) |
    Where-Object { $_ -and (Get-ChildItem -LiteralPath (Join-Path (Split-Path $_) 'sdk') -Directory -ErrorAction SilentlyContinue) }) '.NET 10 SDK'
$iscc = Find-Tool @(
    (Join-Path $Workspace 'Toolchains\InnoSetup\ISCC.exe'),
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe") 'Inno Setup (ISCC.exe)'

$env:DOTNET_ROOT = Split-Path $dotnet
$env:DOTNET_CLI_HOME = Join-Path $Workspace 'Cache\DotNetHome'
$env:NUGET_PACKAGES = Join-Path $Workspace 'Cache\NuGet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$project = Join-Path $Workspace 'Source\Desktop\SubWake.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$payload = Join-Path $Workspace 'Working\payload'
$logs = Join-Path $Workspace 'Working\logs'
Clear-Generated $payload
New-Item -ItemType Directory -Path $payload, $logs -Force | Out-Null

# Build-time tests: the schedule, pause, settings, burst, device and history rules. A failure stops the build.
Write-Output 'Running tests...'
$tests = Join-Path $Workspace 'Source\Tests\SubWake.Tests.csproj'
& $dotnet build $tests -c Release -o (Join-Path $Workspace 'Working\tests') *> (Join-Path $logs 'tests-build.log'); Assert-Exit 'Test build'
$testOutput = & (Join-Path $Workspace 'Working\tests\SubWake.Tests.exe') 2>&1
$testOutput | Set-Content -LiteralPath (Join-Path $logs 'tests.log') -Encoding utf8
$testOutput | Where-Object { $_ -match '^FAIL|tests (passed|failed)' } | ForEach-Object { Write-Output "  $_" }
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. See Working\logs\tests.log.' }

Write-Output "Publishing SubWake $version..."
$publishArgs = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $payload,
    '-p:PublishReadyToRun=true', "-p:Version=$version")
& $dotnet @publishArgs *> (Join-Path $logs 'publish.log'); Assert-Exit 'Publish'

# Third-party notices for the bundled .NET runtime and WebView2.
$licenses = Join-Path $payload 'licenses'
Copy-Item -LiteralPath (Join-Path $Workspace 'LICENSE') (Join-Path $payload 'LICENSE.txt')
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
$runtime = Get-Content -LiteralPath (Join-Path $payload 'SubWake.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $package = Join-Path $env:NUGET_PACKAGES ($framework.name.ToLower() + '.runtime.win-x64\' + $framework.version)
    if (Test-Path -LiteralPath $package) {
        Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' } |
            ForEach-Object { Copy-Item $_.FullName (Join-Path $licenses ($framework.name + '-' + $_.Name)) }
    }
}
$webVersion = (([xml](Get-Content -LiteralPath $project -Raw)).Project.ItemGroup.PackageReference | Where-Object Include -eq 'Microsoft.Web.WebView2').Version
foreach ($name in 'LICENSE.txt', 'NOTICE.txt') {
    $file = Join-Path $env:NUGET_PACKAGES "microsoft.web.webview2\$webVersion\$name"
    if (Test-Path -LiteralPath $file) { Copy-Item $file (Join-Path $licenses "WebView2-$name") }
}

if ($SkipInstaller) { Write-Output "Payload ready: $payload"; return }

$builds = Join-Path $Workspace 'Builds'
New-Item -ItemType Directory -Path $builds -Force | Out-Null
Write-Output 'Packaging installer...'
& $iscc "/DAppVersion=$version" "/DPayloadRoot=$payload" "/DOutputRoot=$builds" "/DAppIcon=$Workspace\Source\Desktop\SubWake.ico" `
    (Join-Path $Workspace 'Source\Packaging\SubWake.iss') *> (Join-Path $logs 'installer.log'); Assert-Exit 'Installer'
$setup = Get-Item (Join-Path $builds "SubWake-Setup-$version-x64.exe")
Write-Output ("Installer ready: {0} ({1:N1} MB)" -f $setup.FullName, ($setup.Length / 1MB))
