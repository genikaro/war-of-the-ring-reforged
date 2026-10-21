using Reforged.Simulation;

/// <summary>Razing: a contender without a standing building is defeated, and the last one standing wins.</summary>
public static class OutcomeChecks
{
    public static void Run(Action<bool,string> check)
    {
        const int W = 40,H = 30,Keep0 = 30000,Keep1 = 30001;
        int Cell(int x,int z) => z*W+x;
        int[] Box(int x0,int z0) => [.. from z in Enumerable.Range(z0,3) from x in Enumerable.Range(x0,3) select Cell(x,z)];
        SimWorld Fixture(bool contenders = true)
        {
            var nav = new GridNavigation(W,H,1000,Enumerable.Repeat(true,W*H).ToArray(),new int[(W+1)*(H+1)],
                obstacles: [new NavigationObstacle(Keep0,Box(4,14),[]),new NavigationObstacle(Keep1,Box(33,14),[])]);
            var rules = new CombatRules(); rules.Units.Add("probe",UnitDefinition.Probe);
            var world = new SimWorld(nav,rules);
            world.BuildingRules.Add("keep",new("keep","Keep",null,0,0,60,40,750000,750000,100000,50000,[]));
            world.ConstructionSites.Add(Keep0,new(Keep0,Keep0,2000,"keep",5500,15500) { Team = 0,Health = 40,Complete = true,Prebuilt = true });
            world.ConstructionSites.Add(Keep1,new(Keep1,Keep1,2000,"keep",34500,15500) { Team = 1,Health = 40,Complete = true,Prebuilt = true });
            if (contenders) { world.Contenders.Add(0); world.Contenders.Add(1); }
            world.Spawn(0,Cell(30,15)); world.Spawn(0,Cell(30,17)); world.Spawn(1,Cell(20,25));
            return world;
        }
        void Step(SimWorld world,int ticks) { for (int i = 0; i < ticks; i++) world.Step(); }
        int RazedAt(SimWorld world,int limit)
        {
            for (int i = 0; i < limit && !world.MatchOver; i++) { world.Step(); if (world.NewlyDefeated.Count > 0) return world.Tick-1; }
            return -1;
        }

        var standing = Fixture();
        Step(standing,100);
        check(!standing.MatchOver && standing.Winner == -1 && standing.DefeatedTeams.Count == 0,"the match runs while both sides own a building");

        var razed = Fixture();
        razed.Enqueue(0,[0,1],OrderKind.AttackStructure,target: Keep1);
        int tick = RazedAt(razed,600);
        check(tick >= 0 && razed.MatchOver && razed.Winner == 0 && razed.OutcomeTick == tick && razed.DefeatedTeams.SetEquals([1])
            && !razed.ConstructionSites.ContainsKey(Keep1),"razing the enemy's last building wins on that tick");
        check(razed.Units[2].Present,"Razing ignores the enemy units still standing");
        Step(razed,40);
        check(razed.Winner == 0 && razed.OutcomeTick == tick && razed.NewlyDefeated.Count == 0,"a decided match stays decided while play continues");
        var replay = Fixture(); replay.Commands.AddRange(razed.Commands); Step(replay,razed.Tick);
        check(replay.StateDigest() == razed.StateDigest(),"the outcome replays identically");

        var foundation = Fixture();
        foundation.ConstructionSites.Add(30002,new(30002,Keep1,1000,"keep",20500,5500) { Team = 1,Health = 2 });
        foundation.Enqueue(0,[0,1],OrderKind.AttackStructure,target: Keep1);
        Step(foundation,600);
        check(!foundation.ConstructionSites.ContainsKey(Keep1) && !foundation.MatchOver,"an unfinished foundation still counts as a building");

        var unclaimed = Fixture();
        unclaimed.ConstructionSites.Add(7,new(7,Keep1,1000));
        unclaimed.Enqueue(0,[0,1],OrderKind.AttackStructure,target: Keep1);
        check(RazedAt(unclaimed,600) >= 0 && unclaimed.Winner == 0,"an unclaimed resource site belongs to nobody");

        var sandbox = Fixture(contenders: false);
        sandbox.Enqueue(0,[0,1],OrderKind.AttackStructure,target: Keep1);
        Step(sandbox,600);
        check(!sandbox.ConstructionSites.ContainsKey(Keep1) && !sandbox.MatchOver && sandbox.DefeatedTeams.Count == 0,
            "a world without contenders, like the Helm's Deep sandbox, never decides a match");

        var own = Fixture();
        own.Enqueue(0,[0,1],OrderKind.AttackStructure,cell: SimWorld.ForcedSiege,target: Keep0);
        check(RazedAt(own,600) >= 0 && own.Winner == 1 && own.DefeatedTeams.SetEquals([0]),"razing one's own last building loses");
    }
}
