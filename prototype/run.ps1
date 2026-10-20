param(
    [string]$GodotPath,
    [string]$DotnetPath,
    [switch]$Smoke,
    [switch]$MotionSmoke,
    [switch]$CombatSmoke,
    [switch]$CommandSmoke,
    [switch]$OrdersSmoke,
    [switch]$AbilitySmoke,
    [switch]$Skirmish,
    [switch]$SkirmishSmoke,
    [switch]$ConstructionSmoke,
    [switch]$MeleeSmoke,
    [switch]$EngagementSmoke,
    [switch]$AttackMoveEngagementSmoke,
    [switch]$FormationSmoke,
    [switch]$SurfaceSmoke,
    [switch]$ObstacleSmoke,
    [ValidateSet('squad', 'fortress', 'overview')]
    [string]$View = 'squad',
    [switch]$HideHud,
    [switch]$DebugMenu,
    [string]$CapturePath,
    [ValidatePattern('^[1-9][0-9]{2,4}x[1-9][0-9]{2,4}$')]
    [string]$Resolution,
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
if ($Resolution) { $arguments += @('--resolution',$Resolution) }
if ($SkirmishSmoke -or $ConstructionSmoke) { $Skirmish = $true }
if ($Smoke -or $MotionSmoke -or $CombatSmoke -or $CommandSmoke -or $OrdersSmoke -or $AbilitySmoke -or $SkirmishSmoke -or $ConstructionSmoke -or $MeleeSmoke -or $EngagementSmoke -or $AttackMoveEngagementSmoke -or $FormationSmoke -or $SurfaceSmoke -or $ObstacleSmoke) {
    $renderRate = if ($MotionSmoke -or $FormationSmoke) { '80' } else { '20' }
    if (-not $CapturePath) { $arguments += '--headless' }
    $arguments += @('--fixed-fps', $renderRate)
}
$arguments += '--'
$arguments += "--view=$View"
if ($HideHud) { $arguments += '--hide-hud' }
if ($DebugMenu) { $arguments += '--debug-menu' }
if ($Smoke -or $MotionSmoke) { $arguments += '--smoke' }
if ($MotionSmoke) { $arguments += '--motion-smoke' }
if ($CombatSmoke) { $arguments += '--combat-smoke' }
if ($CommandSmoke) { $arguments += '--command-smoke' }
if ($OrdersSmoke) { $arguments += '--orders-smoke' }
if ($AbilitySmoke) { $arguments += '--ability-smoke' }
if ($SkirmishSmoke) { $arguments += '--skirmish-smoke' }
if ($ConstructionSmoke) { $arguments += '--construction-smoke' }
if ($MeleeSmoke) { $arguments += '--melee-smoke' }
if ($EngagementSmoke) { $arguments += '--engagement-smoke' }
if ($AttackMoveEngagementSmoke) { $arguments += '--attack-move-engagement-smoke' }
if ($FormationSmoke) { $arguments += '--formation-smoke' }
if ($SurfaceSmoke) { $arguments += '--surface-smoke' }
if ($ObstacleSmoke) { $arguments += '--obstacle-smoke' }
if ($CapturePath) { $arguments += "--capture=$([System.IO.Path]::GetFullPath($CapturePath))" }
if ($CapturePath -and $Resolution) { $arguments += "--capture-resolution=$Resolution" }
if ($Skirmish -and -not $CachePath) { $CachePath = Join-Path $repo 'converted/skirmish' }
if ($CachePath) { $arguments += "--cache=$([System.IO.Path]::GetFullPath($CachePath))" }
& $GodotPath @arguments
exit $LASTEXITCODE
