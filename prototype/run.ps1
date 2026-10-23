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
    [ValidatePattern('^MP[0-9]{2}_[^\/.]+$')]
    [string]$Map,
    [switch]$SkirmishSmoke,
    [switch]$ConstructionSmoke,
    [switch]$StructureSmoke,
    [switch]$ProductionSmoke,
    [switch]$PlacementSmoke,
    [switch]$DwarfHallSmoke,
    [switch]$MinimapSmoke,
    [switch]$FrodoSmoke,
    [switch]$TechSmoke,
    [switch]$Tier2Smoke,
    [switch]$ResearchSmoke,
    [switch]$SiegeSmoke,
    [switch]$DefenseSmoke,
    [switch]$VictorySmoke,
    [switch]$AiSmoke,
    [switch]$ObserverSmoke,
    [switch]$Setup,
    [switch]$SetupSmoke,
    [switch]$Observe,
    [string[]]$Bots,
    [int]$Speed,
    [int]$Seed,
    [switch]$Profile,
    [switch]$NoAi,
    [string]$AiPersonality,
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
# -Skirmish with nothing else that chooses the match opens the setup screen; -Setup always does.
$launchOnly = @('Skirmish','GodotPath','DotnetPath','Resolution','HideHud','DebugMenu','View')
$setupMode = $Setup -or $SetupSmoke -or ($Skirmish -and -not ($PSBoundParameters.Keys | Where-Object { $_ -notin $launchOnly }))
$env:DOTNET_ROOT = Split-Path -Parent $DotnetPath
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $DotnetPath build (Join-Path $PSScriptRoot 'Reforged.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
$arguments = @('--path', $PSScriptRoot)
if ($Resolution) { $arguments += @('--resolution',$Resolution) }
if ($SkirmishSmoke -or $ConstructionSmoke -or $StructureSmoke -or $ProductionSmoke -or $PlacementSmoke -or $DwarfHallSmoke -or $MinimapSmoke -or $FrodoSmoke -or $TechSmoke -or $Tier2Smoke -or $ResearchSmoke -or $SiegeSmoke -or $DefenseSmoke -or $VictorySmoke -or $AiSmoke -or $ObserverSmoke -or $Observe -or $Map) { $Skirmish = $true }
if ($Smoke -or $MotionSmoke -or $CombatSmoke -or $CommandSmoke -or $OrdersSmoke -or $AbilitySmoke -or $SkirmishSmoke -or $ConstructionSmoke -or $StructureSmoke -or $ProductionSmoke -or $PlacementSmoke -or $DwarfHallSmoke -or $MinimapSmoke -or $FrodoSmoke -or $TechSmoke -or $Tier2Smoke -or $ResearchSmoke -or $SiegeSmoke -or $DefenseSmoke -or $VictorySmoke -or $AiSmoke -or $ObserverSmoke -or $SetupSmoke -or $MeleeSmoke -or $EngagementSmoke -or $AttackMoveEngagementSmoke -or $FormationSmoke -or $SurfaceSmoke -or $ObstacleSmoke) {
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
if ($StructureSmoke) { $arguments += '--structure-smoke' }
if ($ProductionSmoke) { $arguments += '--production-smoke' }
if ($PlacementSmoke) { $arguments += '--placement-smoke' }
if ($DwarfHallSmoke) { $arguments += '--dwarf-hall-smoke' }
if ($MinimapSmoke) { $arguments += '--minimap-smoke' }
if ($FrodoSmoke) { $arguments += '--frodo-smoke' }
if ($TechSmoke) { $arguments += '--tech-smoke' }
if ($Tier2Smoke) { $arguments += '--tier2-smoke' }
if ($ResearchSmoke) { $arguments += '--research-smoke' }
if ($SiegeSmoke) { $arguments += '--siege-smoke' }
if ($DefenseSmoke) { $arguments += '--defense-smoke' }
if ($VictorySmoke) { $arguments += '--victory-smoke' }
if ($AiSmoke) { $arguments += '--ai-smoke' }
if ($ObserverSmoke) { $arguments += @('--observer-smoke','--observe','--bots=Gimli,Aragorn') }
if ($setupMode) { $arguments += '--setup' }
if ($SetupSmoke) { $arguments += '--setup-smoke' }
if ($Observe) { $arguments += '--observe' }
if ($Bots) { $arguments += "--bots=$($Bots -join ',')" }
if ($Speed) { $arguments += "--speed=$Speed" }
if ($Seed) { $arguments += "--seed=$Seed" }
if ($Profile) { $arguments += '--profile' }
if ($NoAi) { $arguments += '--no-ai' }
if ($AiPersonality) { $arguments += "--ai-personality=$AiPersonality" }
if ($MeleeSmoke) { $arguments += '--melee-smoke' }
if ($EngagementSmoke) { $arguments += '--engagement-smoke' }
if ($AttackMoveEngagementSmoke) { $arguments += '--attack-move-engagement-smoke' }
if ($FormationSmoke) { $arguments += '--formation-smoke' }
if ($SurfaceSmoke) { $arguments += '--surface-smoke' }
if ($ObstacleSmoke) { $arguments += '--obstacle-smoke' }
if ($CapturePath) { $arguments += "--capture=$([System.IO.Path]::GetFullPath($CapturePath))" }
if ($CapturePath -and $Resolution) { $arguments += "--capture-resolution=$Resolution" }
# Bilbo's Bluffs lives in converted/skirmish; -Map MP04_Wasteland uses converted/skirmish-maps/MP04_Wasteland.
if ($Map -and -not $CachePath) { $CachePath = Join-Path $repo "converted/skirmish-maps/$Map" }
if ($Skirmish -and -not $CachePath) { $CachePath = Join-Path $repo 'converted/skirmish' }
if ($CachePath) { $arguments += "--cache=$([System.IO.Path]::GetFullPath($CachePath))" }
& $GodotPath @arguments
exit $LASTEXITCODE
