using Reforged.Simulation;

public partial class Main
{
    private static readonly string[] EvilResearchNames = ["DARKNESS","PERPETUAL_DARKNESS","SAVAGE_INFLUENCE","PARALYZE","SPRAY_VENOM",
        "POISON_ARROWS","GOBLIN_FRENZY","FLAME_ARROWS","GREATER_PERCEPTION","POISON_DART","SWEEPING_AXE","ASSASSINS_CLOAK","MORGUL_BLADE"];
    private int _evilResearchIndex,_evilResearchTick=-1,_evilUpgradePhase,_evilUpgradeSlayer=-1,_evilUpgradeEnemy=-1,_evilUpgradeTowerEnemy=-1;

    /// <summary>Continues the Evil Sandbox smoke with real imported research, one normal HUD command at a time,
    /// then a paid Poison Dart cast through the player's targeting/command path.</summary>
    private bool EvilUpgradeResearchSmoke()
    {
        var world=_world!;
        if (_evilResearchIndex<EvilResearchNames.Length)
        {
            string name="TECHNIQUE_"+EvilResearchNames[_evilResearchIndex];
            var technique=world.Techniques.FirstOrDefault(t=>t.Name==name);
            if (technique==null) { _evilChecks["research_available_"+name]=false;_evilResearchIndex++;return false; }
            if (world.HasResearched(0,name))
            {
                _evilChecks["research_available_"+name]=true;_evilResearchIndex++;_evilResearchTick=-1;return false;
            }
            if (_evilResearchTick<0)
            {
                var site=world.ConstructionSites.Values.First(s=>s.Team==0 && s.Complete && s.Building==technique.Building);
                _selected.Clear();_selectedStructure=site.Obstacle;
                _evilChecks["research_icon_"+name]=_commands.Art(_techniquePortraits[name])!=null;
                ChooseResearch(technique.Slot);_evilResearchTick=world.Tick;
            }
            if (world.Tick-_evilResearchTick>200)
            { _evilChecks["research_available_"+name]=false;_evilResearchIndex++;_evilResearchTick=-1; }
            return false;
        }
        if (_evilUpgradePhase==0)
        {
            var home=world.ConstructionSites.Values.First(s=>s.Team==0 && s.Building==EvilHovel);
            int Muster(int team,string name,int dx)
            {
                var definition=_rules.Units[name] with {Passive=true};
                var nav=world.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                var occupied=world.Units.Where(u=>u.Present).Select(u=>u.Node).ToHashSet();
                int cell=nav.Nearest(world.Navigation.Cell(home.X+dx,home.Z-18000),occupied,radius:64);
                var unit=world.Spawn(team,cell,definition);AddUnitView(unit);return unit.Id;
            }
            _evilUpgradeSlayer=Muster(0,"UNIT_E_HARADRIM_ASSASSIN",-6000);
            _evilUpgradeEnemy=Muster(1,"UNIT_G_GONDOR_SWORDSMAN",-10000);
            var tower=world.ConstructionSites.Values.First(s=>s.Team==0 && s.Building=="BUILDING_E_TOWER");
            var defender=_rules.Units["UNIT_G_GONDOR_SWORDSMAN"] with {Passive=true,MaxHealth=2000};
            var defenseNav=world.Navigation.ForBody(defender.BodyRadiusMm,defender.BodyHeightMm);
            var defenseTaken=world.Units.Where(u=>u.Present).Select(u=>u.Node).ToHashSet();
            int defenseCell=defenseNav.Nearest(world.Navigation.Cell(tower.X+6000,tower.Z),defenseTaken,radius:32);
            var defenseTarget=world.Spawn(1,defenseCell,defender);AddUnitView(defenseTarget);_evilUpgradeTowerEnemy=defenseTarget.Id;
            // Earlier hero probes left enemies near this tower; give this probe its own current target.
            tower.DefenseTarget=defenseTarget.Id;tower.DefenseSiteTarget=-1;tower.DefenseCooldown=0;
            world.Units[_evilUpgradeEnemy].Health=2000;
            _selectedStructure=null;_selected.Clear();_selected.Add(_evilUpgradeSlayer);
            ChooseAbility(0);world.EnqueueAbility(0,[_evilUpgradeSlayer],0,_evilUpgradeEnemy);
            _evilResearchTick=world.Tick;_evilUpgradePhase=1;return false;
        }
        if (_evilUpgradePhase==1)
        {
            if (world.Tick-_evilResearchTick<100) return false;
            var slayer=world.Units[_evilUpgradeSlayer];var enemy=world.Units[_evilUpgradeEnemy];
            _evilChecks["researched_slayer_is_cloaked"]=slayer.ElvenCloaked;
            _evilChecks["imported_poison_dart_casts_and_hurts"]=enemy.Poisons.Any(p=>p.Ability.Kind==AbilityKind.PoisonDart) && enemy.Health<2000;
            _evilChecks["imported_poison_dart_starts_cooldown"]=slayer.AbilityReadyAt[0]>world.Tick;
            _evilChecks["imported_tower_poison_hits_without_a_unit_caster"]=world.Units[_evilUpgradeTowerEnemy].Poisons.Any(p=>p.Site>=0 && p.Source<0);
            var tower=world.ConstructionSites.Values.First(s=>s.Team==0 && s.Building=="BUILDING_E_TOWER");
            _evilNotes["poison_tower_probe"]=$"defense={world.SiteDefense(tower)} target={tower.DefenseTarget} cooldown={tower.DefenseCooldown} enemy={world.Units[_evilUpgradeTowerEnemy].X},{world.Units[_evilUpgradeTowerEnemy].Z} tower={tower.X},{tower.Z} poison={world.Units[_evilUpgradeTowerEnemy].Poisons.Count}";
            _evilUpgradePhase=2;
        }
        return true;
    }
}
