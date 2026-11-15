# Exports the game as a Windows executable (build-147): build/WarOfTheRingReforged.exe, a release build with the game
# packed inside (the .NET runtime and assemblies sit beside it in data_Reforged_windows_x86_64), and
# WarOfTheRingReforged.console.exe, the same game with a console for the log. Double-clicked, it opens on the launcher,
# which asks for the original game's folder; the game reads that install in place (build-217), so nothing from the
# original game is packed or needed beside the executable.
# Build-220: -Release exports into build/release/WarOfTheRingReforged-build-<N>-win64/ with the release README,
# LICENSE and NOTICE.md, and zips it beside the folder with its SHA-256 (.zip.sha256), ready for a GitHub release.
# Godot reads export templates from %APPDATA%\Godot; this script points APPDATA at converted/tooling/appdata for the
# export only. -DownloadTemplates fetches the official templates once (about 1.2 GB) and checks their SHA-512.
param(
    [string]$GodotPath,
    [string]$DotnetPath,
    [string]$Output,
    [switch]$DebugBuild,
    [switch]$DownloadTemplates,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $DotnetPath) {
    $bundled = Join-Path $repo 'converted/tooling/dotnet/dotnet.exe'
    if (Test-Path -LiteralPath $bundled) { $DotnetPath = $bundled }
    else { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
if (-not $GodotPath) {
    $bundled = Join-Path $repo 'converted/tooling/godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
    if (Test-Path -LiteralPath $bundled) { $GodotPath = $bundled }
    else { $GodotPath = (Get-Command godot -ErrorAction Stop).Source }
}
$env:DOTNET_ROOT = Split-Path -Parent $DotnetPath
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$version = '4.7.2.stable.mono'
$appData = Join-Path $repo 'converted/tooling/appdata'
$templates = Join-Path $appData "Godot/export_templates/$version"
$templateNames = @('version.txt','windows_release_x86_64.exe','windows_release_x86_64_console.exe','windows_debug_x86_64.exe','windows_debug_x86_64_console.exe')
if (-not (Test-Path -LiteralPath (Join-Path $templates 'windows_release_x86_64.exe'))) {
    if (-not $DownloadTemplates) {
        throw "Godot $version export templates are missing from $templates. Run ./prototype/export.ps1 -DownloadTemplates (about 1.2 GB from github.com/godotengine/godot-builds)."
    }
    $release = 'https://github.com/godotengine/godot-builds/releases/download/4.7.2-stable'
    $archiveName = 'Godot_v4.7.2-stable_mono_export_templates.tpz'
    $downloads = Join-Path $repo 'converted/tooling/downloads'
    New-Item -ItemType Directory -Force $downloads | Out-Null
    $sums = Join-Path $downloads 'SHA512-SUMS.txt'
    $archive = Join-Path $downloads $archiveName
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri "$release/SHA512-SUMS.txt" -OutFile $sums -UseBasicParsing
    $expected = ((Get-Content $sums | Where-Object { $_ -match [regex]::Escape($archiveName) }) -split '\s+')[0]
    if (-not $expected) { throw "No checksum for $archiveName in $release/SHA512-SUMS.txt" }
    Write-Output "Downloading $archiveName ..."
    if (Get-Command curl.exe -ErrorAction SilentlyContinue) { & curl.exe -sSL --retry 3 -o $archive "$release/$archiveName"; if ($LASTEXITCODE -ne 0) { throw 'Download failed' } }
    else { Invoke-WebRequest -Uri "$release/$archiveName" -OutFile $archive -UseBasicParsing }
    $actual = (Get-FileHash -Algorithm SHA512 -LiteralPath $archive).Hash
    if ($actual -ne $expected) { Remove-Item -LiteralPath $archive; throw "Checksum mismatch for $archiveName" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    New-Item -ItemType Directory -Force $templates | Out-Null
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($name in $templateNames) {
            $entry = $zip.GetEntry("templates/$name")
            if (-not $entry) { throw "templates/$name missing from $archiveName" }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $templates $name),$true)
        }
    } finally { $zip.Dispose() }
    Remove-Item -LiteralPath $archive
}

if ($Release) {
    if ($DebugBuild -or $Output) { throw '-Release makes a release build in build/release; leave out -DebugBuild and -Output' }
    $build = [regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'BuildInfo.cs')),'Number\s*=\s*(\d+)').Groups[1].Value
    if (-not $build) { throw 'No build number in BuildInfo.cs' }
    $releaseName = "WarOfTheRingReforged-build-$build-win64"
    $releaseRoot = Join-Path $repo 'build/release'
    $stage = Join-Path $releaseRoot $releaseName
    if (Test-Path -LiteralPath $stage) { Remove-Item -Recurse -Force -LiteralPath $stage }
    $Output = Join-Path $stage 'WarOfTheRingReforged.exe'
}
if (-not $Output) { $Output = Join-Path $repo 'build/WarOfTheRingReforged.exe' }
$Output = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path -Parent $Output) | Out-Null
& $DotnetPath build (Join-Path $PSScriptRoot 'Reforged.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
$mode = if ($DebugBuild) { '--export-debug' } else { '--export-release' }
$previousAppData = $env:APPDATA
try {
    $env:APPDATA = $appData
    & $GodotPath --headless --path $PSScriptRoot $mode 'Windows Desktop' $Output
    $exportExit = $LASTEXITCODE
} finally { $env:APPDATA = $previousAppData }
if ($exportExit -ne 0 -or -not (Test-Path -LiteralPath $Output)) { throw "Export failed (exit $exportExit)" }
Write-Output "Exported $Output; it reads the original game from the folder chosen in its launcher."
if ($Release) {
    $readme = (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repo 'docs/release/README.txt')).Replace('{BUILD}',"build-$build")
    [System.IO.File]::WriteAllText((Join-Path $stage 'README.txt'),$readme.Replace("`r`n","`n").Replace("`n","`r`n"),(New-Object System.Text.UTF8Encoding $true))
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'NOTICE.md') -Destination $stage
    $zip = Join-Path $releaseRoot "$releaseName.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
    Add-Type -AssemblyName System.IO.Compression,System.IO.Compression.FileSystem
    # Entry by entry: Windows PowerShell's ZipFile.CreateFromDirectory writes backslashes, which the ZIP format forbids.
    $archive = [System.IO.Compression.ZipFile]::Open($zip,[System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
            $entry = "$releaseName/" + $file.FullName.Substring($stage.Length + 1).Replace('\','/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entry,[System.IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$zip.sha256","$hash  $releaseName.zip`n")
    Write-Output "Packaged $zip ($([math]::Round((Get-Item -LiteralPath $zip).Length/1MB)) MB, SHA-256 $hash)"
}
