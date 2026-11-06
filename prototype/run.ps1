param(
    [string]$GodotPath,
    [string]$DotnetPath,
    [switch]$Smoke,
    [switch]$MotionSmoke,
    [switch]$CombatSmoke,
    [switch]$CommandSmoke,
    [switch]$InteractionSmoke,
    [switch]$OrdersSmoke,
    [switch]$AbilitySmoke,
    [switch]$AudioSmoke,
    [switch]$Unmuted,
    [switch]$ElvenSmoke,
    [switch]$NatureSmoke,
    [switch]$BloodSmoke,
    [switch]$HeroSkillSmoke,
    [switch]$GlobalPowerSmoke,
    [switch]$PlacesSmoke,
    [switch]$HeroExperienceSmoke,
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
    [switch]$EvilSmoke,
    [switch]$ResearchSmoke,
    [switch]$SiegeSmoke,
    [switch]$DefenseSmoke,
    [switch]$VictorySmoke,
    [switch]$AiSmoke,
    [switch]$ExpansionSmoke,
    [switch]$ObserverSmoke,
    [switch]$Setup,
    [switch]$SetupSmoke,
    [switch]$Menu,
    [switch]$MenuSmoke,
    [switch]$Sandbox,
    [switch]$SandboxSmoke,
    [switch]$Observe,
    [switch]$FogSmoke,
    [switch]$Fog,
    [switch]$NoFog,
    [string[]]$Bots,
    [ValidateSet('good','evil')]
    [string[]]$Factions,
    [int]$Speed,
    [int]$Seed,
    [switch]$Profile,
    [ValidateRange(0,600)]
    [int]$ProfileUntil,
    [ValidateRange(0,600)]
    [int]$FastUntil,
    [switch]$CameraTour,
    [switch]$PerformanceSmoke,
    [switch]$Headless,
    [switch]$StressBenchmark,
    [ValidateRange(2,4000)]
    [int]$Units = 300,
    [switch]$EconomyRecoverySmoke,
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
$launchOnly = @('Skirmish','GodotPath','DotnetPath','Resolution','HideHud','DebugMenu','View','Unmuted')
$setupMode = $Setup -or $SetupSmoke -or ($Skirmish -and -not ($PSBoundParameters.Keys | Where-Object { $_ -notin $launchOnly }))
# Build-147: nothing that chooses a scene (or -Menu) opens the main menu; -View, -CapturePath and the like keep Helm's Deep.
$menuMode = $Menu -or $MenuSmoke -or -not ($PSBoundParameters.Keys | Where-Object { $_ -notin @('GodotPath','DotnetPath','Resolution','Unmuted','DebugMenu') })
$env:DOTNET_ROOT = Split-Path -Parent $DotnetPath
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($ExpansionSmoke) { $AiSmoke = $true; $NoAi = $true; $Speed = 16 }
if ($EconomyRecoverySmoke) {
    $Observe = $true; $Speed = 16
    if (-not $Map -and -not $CachePath) { $Map = 'MP06_Wheel of Fate' }
    if (-not $Bots) { $Bots = @('Gimli','Gimli','Gimli','Gimli','Gimli','Gimli') }
}
if ($ElvenSmoke -or $NatureSmoke -or $BloodSmoke -or $GlobalPowerSmoke -or $PlacesSmoke -or $HeroSkillSmoke -or $HeroExperienceSmoke -or $PerformanceSmoke -or $StressBenchmark) { $Sandbox = $true }
# Build-157: the stress benchmark renders in a window (never headless) at a fixed 1/60 s per frame.
if ($StressBenchmark -and -not $Resolution) { $Resolution = '1600x900' }
& $DotnetPath build (Join-Path $PSScriptRoot 'Reforged.csproj') --nologo
if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
$arguments = @('--path', $PSScriptRoot)
if ($Resolution) { $arguments += @('--resolution',$Resolution) }
if ($SkirmishSmoke -or $ConstructionSmoke -or $StructureSmoke -or $ProductionSmoke -or $PlacementSmoke -or $DwarfHallSmoke -or $MinimapSmoke -or $FrodoSmoke -or $TechSmoke -or $Tier2Smoke -or $EvilSmoke -or $ResearchSmoke -or $SiegeSmoke -or $DefenseSmoke -or $VictorySmoke -or $AiSmoke -or $ObserverSmoke -or $FogSmoke -or $Observe -or $Map -or $Sandbox -or $SandboxSmoke) { $Skirmish = $true }
if ($FogSmoke -or $Smoke -or $MotionSmoke -or $CombatSmoke -or $CommandSmoke -or $InteractionSmoke -or $OrdersSmoke -or $AbilitySmoke -or $SkirmishSmoke -or $ConstructionSmoke -or $StructureSmoke -or $ProductionSmoke -or $PlacementSmoke -or $DwarfHallSmoke -or $MinimapSmoke -or $FrodoSmoke -or $TechSmoke -or $Tier2Smoke -or $EvilSmoke -or $ResearchSmoke -or $SiegeSmoke -or $DefenseSmoke -or $VictorySmoke -or $AiSmoke -or $ObserverSmoke -or $SetupSmoke -or $MenuSmoke -or $SandboxSmoke -or $MeleeSmoke -or $EngagementSmoke -or $AttackMoveEngagementSmoke -or $FormationSmoke -or $SurfaceSmoke -or $ObstacleSmoke) {
    $renderRate = if ($MotionSmoke -or $FormationSmoke) { '80' } else { '20' }
    if (-not $CapturePath) { $arguments += '--headless' }
    $arguments += @('--fixed-fps', $renderRate)
}
if ($ElvenSmoke -or $NatureSmoke -or $BloodSmoke -or $GlobalPowerSmoke -or $PlacesSmoke -or $HeroSkillSmoke -or $HeroExperienceSmoke -or $PerformanceSmoke -or $EconomyRecoverySmoke) {
    if (-not $CapturePath) { $arguments += '--headless' }
    $arguments += @('--fixed-fps','20')
}
if ($AudioSmoke) {
    if (-not $CapturePath) { $arguments += '--headless' }
    $arguments += @('--fixed-fps','20')
}
if ($StressBenchmark) { $arguments += @('--fixed-fps','60') }
# Build-160: a match without a window (e.g. -Observe -Speed 16 -Profile -ProfileUntil 14 -Headless), for simulation timing.
if ($Headless -and -not ($arguments -contains '--headless')) { $arguments += '--headless' }
$arguments += '--'
if ($AudioSmoke) { $arguments += '--audio-smoke' }
$arguments += "--view=$View"
if ($HideHud) { $arguments += '--hide-hud' }
# Debug sessions start muted (build-142); -Unmuted plays the audio.
if ($Unmuted) { $arguments += '--unmuted' }
if ($DebugMenu) { $arguments += '--debug-menu' }
if ($Smoke -or $MotionSmoke) { $arguments += '--smoke' }
if ($MotionSmoke) { $arguments += '--motion-smoke' }
if ($CombatSmoke) { $arguments += '--combat-smoke' }
if ($CommandSmoke) { $arguments += '--command-smoke' }
if ($InteractionSmoke) { $arguments += '--interaction-smoke' }
if ($OrdersSmoke) { $arguments += '--orders-smoke' }
if ($AbilitySmoke) { $arguments += '--ability-smoke' }
if ($ElvenSmoke) { $arguments += '--elven-smoke' }
if ($NatureSmoke) { $arguments += '--nature-smoke' }
if ($BloodSmoke) { $arguments += '--blood-smoke' }
if ($GlobalPowerSmoke) { $arguments += '--global-power-smoke' }
if ($PlacesSmoke) { $arguments += '--places-smoke' }
if ($HeroSkillSmoke) { $arguments += '--hero-skill-smoke' }
if ($HeroExperienceSmoke) { $arguments += '--hero-experience-smoke' }
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
if ($EvilSmoke) { $arguments += '--evil-smoke'; if (-not $Factions) { $arguments += '--factions=evil,good' } }
if ($ResearchSmoke) { $arguments += '--research-smoke' }
if ($SiegeSmoke) { $arguments += '--siege-smoke' }
if ($DefenseSmoke) { $arguments += '--defense-smoke' }
if ($VictorySmoke) { $arguments += '--victory-smoke' }
if ($AiSmoke) { $arguments += '--ai-smoke' }
if ($ExpansionSmoke) { $arguments += '--expansion-smoke' }
if ($ObserverSmoke) { $arguments += @('--observer-smoke','--observe','--bots=Gimli,Aragorn') }
if ($FogSmoke) { $arguments += '--fog-smoke' }
# Build-150: every skirmish has fog of war; the smokes written before it run without it unless -Fog.
if ($NoFog -or (-not $Fog -and -not $FogSmoke -and ($arguments -contains '--fixed-fps'))) { $arguments += '--no-fog' }
elseif ($Fog) { $arguments += '--fog' }
if ($setupMode) { $arguments += '--setup' }
if ($SetupSmoke) { $arguments += '--setup-smoke' }
if ($menuMode) { $arguments += '--menu' }
if ($MenuSmoke) { $arguments += '--menu-smoke' }
if ($SandboxSmoke) { $arguments += '--sandbox-smoke' }
if ($Observe) { $arguments += '--observe' }
if ($Bots) { $arguments += "--bots=$($Bots -join ',')" }
if ($Factions) { $arguments += "--factions=$($Factions -join ',')" }
if ($Speed) { $arguments += "--speed=$Speed" }
if ($Seed) { $arguments += "--seed=$Seed" }
if ($Profile) { $arguments += '--profile' }
if ($ProfileUntil) { $arguments += "--profile-until=$ProfileUntil" }
# Build-167: e.g. -Map MP08_Rift -Observe -Bots ... -Profile -FastUntil 10 -ProfileUntil 12 -CameraTour (windowed, late-game play at 1x).
if ($FastUntil) { $arguments += "--fast-until=$FastUntil" }
if ($CameraTour) { $arguments += '--camera-tour' }
if ($PerformanceSmoke) { $arguments += @('--performance-smoke','--no-ai') }
if ($StressBenchmark) { $arguments += @('--stress-benchmark',"--stress-units=$Units",'--no-ai') }
if ($EconomyRecoverySmoke) { $arguments += '--economy-recovery-smoke' }
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
# The Sandbox test map (build-56): an empty field where you command both players; python tools/prepare_prototype.py --sandbox builds it.
if (($Sandbox -or $SandboxSmoke) -and -not $CachePath) { $CachePath = Join-Path $repo 'converted/skirmish-maps/Sandbox' }
if ($Skirmish -and -not $CachePath) { $CachePath = Join-Path $repo 'converted/skirmish' }
if ($CachePath) { $arguments += "--cache=$([System.IO.Path]::GetFullPath($CachePath))" }
if ($AudioSmoke) {
    # Godot can report a signal connection error while still returning exit code zero.
    $audioOutput = @(& $GodotPath @arguments 2>&1)
    $audioExit = $LASTEXITCODE
    $audioOutput | ForEach-Object { Write-Output $_ }
    if ($audioExit -eq 0 -and ($audioOutput -match '^ERROR:')) {
        Write-Output 'Audio smoke failed: Godot reported errors.'
        exit 1
    }
    exit $audioExit
}
& $GodotPath @arguments
exit $LASTEXITCODE
