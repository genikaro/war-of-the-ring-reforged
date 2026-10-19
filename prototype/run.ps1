param(
    [string]$GodotPath,
    [string]$DotnetPath,
    [switch]$Smoke,
    [switch]$SurfaceSmoke,
    [switch]$ObstacleSmoke,
    [string]$CapturePath,
    [string]$CachePath
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
& $DotnetPath build (Join-Path $PSScriptRoot 'Reforged.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
$arguments = @('--path', $PSScriptRoot)
if ($Smoke -or $SurfaceSmoke -or $ObstacleSmoke) { $arguments += @('--headless', '--fixed-fps', '20') }
$arguments += '--'
if ($Smoke) { $arguments += '--smoke' }
if ($SurfaceSmoke) { $arguments += '--surface-smoke' }
if ($ObstacleSmoke) { $arguments += '--obstacle-smoke' }
if ($CapturePath) { $arguments += "--capture=$([System.IO.Path]::GetFullPath($CapturePath))" }
if ($CachePath) { $arguments += "--cache=$([System.IO.Path]::GetFullPath($CachePath))" }
& $GodotPath @arguments
exit $LASTEXITCODE
