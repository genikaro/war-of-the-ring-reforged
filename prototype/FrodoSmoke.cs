using Godot;
using Reforged.Presentation;
using Reforged.Rendering;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _frodoSmoke;
    private readonly Dictionary<string,bool> _frodoChecks = [];
    private int _frodoId = -1,_frodoRingTick = -1;
    private const string SmokeFrodo = "UNIT_G_HERO_FRODO";

    private void FrodoSmokeInput()
    {
        var world = _world!; var citadel = world.Producers[10000];
        SimUnit? Frodo() => world.Units.FirstOrDefault(u => u.Team == 0 && u.Definition.Name == SmokeFrodo && u.Health > 0);
        Vector2 Portrait() => _heroBar.Portrait(0).GetGlobalRect().GetCenter();
        bool Faded(bool expected) => _views[_frodoId] is { Translucent: var translucent, Ghost: { Length: > 0 } ghost } && translucent == expected
            && ghost.All(g => (g.Mesh.GetSurfaceOverrideMaterial(g.Surface) is BaseMaterial3D { Transparency: BaseMaterial3D.TransparencyEnum.Alpha }) == expected);
        switch (_frames)
        {
            case 2:
                // Test fixture: the start bank (115 food) cannot afford Frodo (150 food, 100 ore). Everything after this uses UI input.
                world.Economies[0].FoodMilli += 300_000; world.Economies[0].OreMilli += 300_000;world.GrantResources(0,0,fate:2);break;
            case 3: _focus = _structureViews[10000].Root.GlobalPosition; UpdateCamera(); Click(StructureSmokePoint(10000)); break;
            case 6:
                _frodoChecks["citadel_offers_frodo_with_source_cost"] = citadel.Units.Take(2).SequenceEqual(["UNIT_G_HUMAN_WORKER",SmokeFrodo])
                    && _commands.TrainButton(1) is { Visible: true,Disabled: false,Icon: not null } && _commands.TrainButton(1).TooltipText.Contains(HudTip.Costs(150,100,0,0)+"\n"+CommandPanel.Seconds("tip.time",720));
                Press(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 7: Release(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 9:
                _frodoChecks["frodo_training_pays_without_population"] = citadel.Queue.Count == 1 && citadel.Queue[0].Unit == SmokeFrodo
                    && world.Economies[0].Food == 265 && world.Economies[0].Ore == 525 && world.PopulationUsed(0) == 4;
                _frodoChecks["unique_hero_button_disabled"] = _commands.TrainButton(1).Disabled && _commands.TrainButton(1).TooltipText.Contains($"{Shown("UNIT_G_HERO_FRODO","Frodo")} is already in play");
                Press(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 10: Release(_commands.TrainButton(1).GetGlobalRect().GetCenter()); break;
            case 12:
                _frodoChecks["second_frodo_refused"] = citadel.Queue.Count == 1 && world.Economies[0].Food == 265; break;
            case 760:
                var frodo = Frodo(); _frodoId = frodo?.Id ?? -1;
                float scale = _commands.EffectiveScale; var rect = _heroBar.Portrait(0).GetGlobalRect(); var screen = GetViewport().GetVisibleRect().Size;
                _frodoChecks["frodo_trained_with_source_stats"] = frodo is { MaxHealth: 305,Health: 305 } && frodo.Definition.Hero && frodo.Definition.Abilities?.Length == 3
                    && _views.ContainsKey(frodo.Id) && citadel.Queue.Count == 0;
                _frodoChecks["hero_bar_uses_source_layout"] = _heroBar.HeroIn(0) == _frodoId && _heroBar.HeroIn(1) < 0 && _heroBar.Portrait(0).Icon != null
                    && Math.Abs(rect.Position.X-(screen.X-(1-.9467f)*1024*scale)) < .5f && Math.Abs(rect.Position.Y-.2481f*768*scale) < .5f
                    && Math.Abs(rect.Size.Y-.0633f*768*scale) < .5f && Math.Abs(_heroBar.HealthWidth(0)-.0437f*1024) < .01f;
                _selected.Clear(); Press(Portrait()); break;
            case 761: Release(Portrait()); break;
            case 763:
                _frodoChecks["portrait_click_selects_frodo"] = _selected.SetEquals([_frodoId]) && _commands.DisplayedName == Shown("UNIT_G_HERO_FRODO","Frodo") && _commands.DisplayedHealth == "305/305";
                _frodoChecks["heroic_aura_shows_frodo_and_recipients"] = _auraEffects.ContainsKey((_frodoId,AuraEffect.Heroic,true))
                    && _auraEffects.Keys.Any(a => a.Unit != _frodoId && a.Effect == AuraEffect.Heroic && !a.Source);
                _frodoChecks["source_abilities_in_qwe"] = _commands.AbilityButton(0) is { Visible: true,Disabled: false,ButtonPressed: false,Icon: not null }
                    && _commands.AbilityButton(1) is { Visible: true,Disabled: false,Icon: not null } && _commands.AbilityButton(2) is { Visible: true,Disabled: true,Icon: not null }
                    && !_commands.AbilityButton(3).Visible && _commands.AbilityButton(0).TooltipText.StartsWith($"{ShownAbility(7,"The One Ring")}\n");
                _focus = _structureViews[10000].Root.GlobalPosition+new Vector3(-40,0,30); UpdateCamera();
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,Pressed = true,DoubleClick = true,Position = Portrait(),GlobalPosition = Portrait() },true); break;
            case 764: Release(Portrait()); break;
            case 765: PushKey(Key.Q);break;
            case 766:
                _frodoChecks["ring_purchase_costs_one_fate_without_toggling"] = world.Economies[0].Fate==1
                    && world.Units[_frodoId].AbilityPurchased(world.Units[_frodoId].Ability(0)!) && !world.Units[_frodoId].RingActive;
                var at = UnitPosition(world.Units[_frodoId]);
                _frodoChecks["portrait_double_click_centers_camera"] = new Vector2(_focus.X-at.X,_focus.Z-at.Z).Length() < 1 && _selected.SetEquals([_frodoId]);
                PushKey(Key.Q); break;
            case 768:
                _frodoRingTick = world.Tick;
                _frodoChecks["q_puts_on_the_ring"] = world.Units[_frodoId].RingActive && _commands.AbilityButton(0).ButtonPressed
                    && _heroBar.Portrait(0).TooltipText.Contains(GameText.T("hero.ring")) && world.Commands.Last() is { Kind: OrderKind.UseAbility,AbilitySlot: 0,Target: -1 };
                _frodoChecks["ring_bearer_drawn_translucent"] = Faded(true);
                _frodoChecks["invisible_hero_glow_is_hidden"] = !_views[_frodoId].HeroGlow!.Ground.Visible;
                _frodoChecks["ring_hides_frodos_emitter"] = !_auraEffects.ContainsKey((_frodoId,AuraEffect.Heroic,true));
                if (_capture != "") _ = CaptureFrame(false);
                break;
            case 808:
                var bearer = world.Units[_frodoId]; int worn = world.Tick-_frodoRingTick;
                _frodoChecks["ring_drains_health_on_hud"] = bearer.HealthMilli < 305_000 && bearer.HealthMilli >= 305_000-(worn+2)*100L
                    && _heroBar.HealthWidth(0) < .0437f*1024;
                PushKey(Key.Q); break;
            case 810:
                _frodoChecks["q_takes_the_ring_off"] = !world.Units[_frodoId].RingActive && !_commands.AbilityButton(0).ButtonPressed && Faded(false);
                _frodoChecks["visible_hero_glow_returns"] = _views[_frodoId].HeroGlow!.Ground.Visible;break;
            case 812:
                var report = new { build = BuildInfo.Label,checks = _frodoChecks,passed = _frodoChecks.Count == 18 && _frodoChecks.Values.All(v => v),
                    fixture = "Start bank credited 300 food and 300 ore at frame 2",
                    frodo = _frodoId < 0 ? null : new { world.Units[_frodoId].Id,world.Units[_frodoId].HealthMilli,world.Units[_frodoId].RingActive },
                    hero_portrait = _heroBar.Portrait(0).GetGlobalRect() is var r ? new[] { r.Position.X,r.Position.Y,r.Size.X,r.Size.Y } : null,
                    bank = new { world.Economies[0].FoodMilli,world.Economies[0].OreMilli },status = _status,digest = world.StateDigest() };
                System.IO.File.WriteAllText(GamePaths.Report("frodo-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented = true}));
                GD.Print("FRODO_SMOKE "+JsonSerializer.Serialize(report)); _ = FinishCombatAudio(report.passed ? 0 : 1); break;
        }
    }
}
