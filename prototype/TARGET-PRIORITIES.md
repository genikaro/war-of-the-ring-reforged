# Build-66: siege commitment and base defense

Previously the assault controller treated any worker within 12 m of any party member
as a reason to give the entire party explicit attack orders. The home defense controller
could only use soldiers outside an assault, raid or capture squad. A marching army
therefore ignored damage to its own base.

An assault now retains its building objective. A passive worker appearing nearby does
not redirect it. Military units can draw a response within 12 m on the march, reduced
to 6 m once the party commits to a building. An enemy actively attacking a member, or
that damaged nearby allies in the last three seconds, is a threat across attack cadence
gaps. Responders are selected by existing assignment, the attacked member and distance,
with deterministic ID ties. Normally up to two soldiers answer a worker and four a
military unit; multiple threats share the available soldiers. Other members continue
their existing march or building orders. Responders resume the objective after the
threat dies or ceases to qualify. A covering tower can still replace an unarmed target.
The Demolisher trait retains its offensive preference to ignore local distractions.
Members temporarily idle because all building attack spots are occupied retain that
party commitment too. Their autonomous target selection respects the same response
limit; a worker cannot recruit the waiting rear ranks independently. The regression
suite includes a siege with more than 100 party members.

Player attack-move uses the same commitment rule after selecting a building. Only
contact, nearby military obstacles or active/recent attackers interrupt it, with a
limited local response. A directly attacked soldier may always defend itself. Its
building target survives that fight and resumes afterwards. Explicit player orders
to focus a building retain their existing behavior.

Actual attacks on buildings, or workers near owned buildings, can recall soldiers
from assaults, raids and capture squads. Merely spotting a worker does not recall an
army. Existing local defenders count toward the response; the nearest available field
soldiers fill the shortfall. They immediately cancel their outbound orders and defend.
A completed Citadel at 40% health or less recalls the available field army. Expansion
garrisons retain their own posts. A new foundation's initially low health does not
trigger a full army recall.

Recalled soldiers stay reserved until three seconds after the last asset threat or
active combat against the reserve. They cannot be recruited into an assault during
that interval. Afterwards ordinary assault formation and reinforcement resume. The
bot status displays the number assigned to base defense. Damage history and defense
reservation state are included in deterministic replay digests.

Regression scenarios cover successive worker arrivals during a siege, damaging
workers and military flankers, resuming the same building, recall during a march,
critical Citadels, raiders and capture squads, quiet-period reservation, passive
workers at home and identical simulation replay. Run the focused checks with:

```powershell
.\converted\tooling\dotnet\dotnet.exe run --project tests/simulation -c Release -- --target-priority-checks
```

The recall size currently uses unit counts, rather than a combat strength estimate.
It is a deterministic response policy; it does not predict travel time or guarantee
that a distant army can arrive before a building falls.

Validation: 113 focused siege/AI assertions and the full simulation suite passed,
including the crowded siege regression. All 18 seeded extended combat scenarios
passed their replay, collision and search-budget checks, with up to 400 units per
side. Godot AI, siege and attack-move engagement smoke tests passed.
