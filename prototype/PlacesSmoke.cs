using Godot;
using Reforged.Simulation;
using System.Text.Json;

public partial class Main
{
    private bool _placesSmoke;
    private readonly Dictionary<string,bool> _placesChecks = [];
    private readonly List<int> _placesGallery = [];
    private int _placesEnemy,_placesWillow;
    private Vector3 _placesAviaryTarget;

    private void PlacesSmokeInput()
    {
        var w = _world!;
        static string Title(string name)
        {
            int split = Enumerable.Range(0,name.Length).Where(i=>name[i]==' ').OrderBy(i=>Math.Abs(i-name.Length/2)).FirstOrDefault();
            return split>0 ? name[..split]+"\n"+name[(split+1)..] : name;
        }
        foreach (var entry in _structureViews.Values.Where(s => !_placesGallery.Contains(s.Id))) entry.Root.Hide();
        foreach (var view in _views) view.Value.Root.Visible = _frames >= 7 && view.Key == _placesEnemy;
        void CapturePlaces(string stage)
        {
            if (_capture != "") _ = CaptureFrame(false,System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_capture)!,
                System.IO.Path.GetFileNameWithoutExtension(_capture)+"-"+stage+".png"));
        }
        switch (_frames)
        {
            case 2:
                w.AiPlayers.Clear(); _sandboxMap = false;
                _placesChecks["three_landmarks_excluded_from_skirmish"] = w.ExcludedSkirmishPlaces.SetEquals(new[] {
                    "OBJECT_PLACE_OF_POWER_STEED_OF_ROHAN","OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY","OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW" });
                _placesChecks["narsil_hero_attack_rule_loaded"] = w.PlaceRules.Values.Single(r=>r.Name=="PLACE_OF_POWER_MONUMENT_OF_NARSIL")
                    is {HeroesOnly:true,AllUnits:false,Modifier:SimWorld.DamageModifier,FloatPpm:1_250_000,Long:0};
                _placesChecks["loaded_skirmish_places_obey_exclusions"] = w.PlacesOfPower.All(p=>!w.ExcludedSkirmishPlaces.Contains(p.Type));
                if (OS.GetCmdlineUserArgs().Contains("--places-map-only"))
                {
                    _placesSmoke=false;
                    _placesChecks["map_has_place_objectives"] = w.PlacesOfPower.Count>0;
                    var mapReport=new {build=BuildInfo.Label,checks=_placesChecks,passed=_placesChecks.Values.All(v=>v),
                        places=w.PlacesOfPower.Select(p=>new {p.Id,p.Type,p.Name})};
                    System.IO.File.WriteAllText(GamePaths.Report("places-map-smoke-report.json"),JsonSerializer.Serialize(mapReport,new JsonSerializerOptions {WriteIndented=true}));
                    GD.Print("PLACES_MAP_SMOKE "+JsonSerializer.Serialize(mapReport)); _=FinishCombatAudio(mapReport.passed?0:1); break;
                }
                // This developer gallery also exercises the original landmarks excluded from normal skirmishes.
                w.ExcludedSkirmishPlaces.Clear();
                foreach (var u in w.Units) w.Enqueue(u.Team,[u.Id],OrderKind.HoldGround);
                SetPaused(true);
                _hud.Hide();
                var types = _placeModels.Where(p => p.Key != "OBJECT_PLACE_OF_POWER_RANDOM").GroupBy(p => p.Value)
                    .Select(g => g.OrderBy(p => p.Key,StringComparer.Ordinal).First().Key).Order(StringComparer.Ordinal).ToArray();
                int cx=w.Navigation.Width*w.Navigation.CellSize/2,cz=w.Navigation.Height*w.Navigation.CellSize/2;
                for (int index=0;index<types.Length;index++)
                {
                    int x=cx+(index%5-2)*23000,z=cz+(index/5-1)*25000,id=80000+index;
                    var root = new Node3D {Name="PlaceGallery_"+index,Position=PositionOf(x,z)}; AddChild(root);
                    AddPlaceOfPower(id,types[index],root); RegisterStructure(id,-1,types[index],root); _placesGallery.Add(id);
                    var label = new Label3D {Text=Title(PlaceAt(id)!.Name),FontSize=48,PixelSize=.035f,Position=Vector3.Up*13,
                        Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true,OutlineSize=10}; root.AddChild(label);
                }
                _placesChecks["fifteen_source_models_render"] = types.Length==15 && _placesGallery.All(id=>_structureViews[id].Meshes.Length>0);
                _placesChecks["control_zones_do_not_intercept_picking"] = _placesGallery.All(id=>_structureViews[id].Meshes.All(m=>!m.Mesh.HasMeta("place_control_zone")));
                _placesChecks["concrete_titles"] = w.PlacesOfPower.All(p=>!p.Name.Contains("Random Place") && !p.Name.Contains("Untitled"));
                _focus=PositionOf(cx,cz); _distance=105; UpdateCamera();
                break;
            case 5: CapturePlaces("models"); break;
            case 7:
                _hud.Show();
                _placesWillow = w.PlacesOfPower.First(p=>p.Type=="OBJECT_PLACE_OF_POWER_ENCHANTED_WILLOW").Id;
                var willow=PlaceAt(_placesWillow)!; willow.Owner=PlayerTeam;
                var definition=w.Rules.Units["UNIT_G_GONDOR_SWORDSMAN"] with {Passive=true};
                var nav=w.Navigation.ForBody(definition.BodyRadiusMm,definition.BodyHeightMm);
                var enemy=w.Spawn(1,nav.Nearest(nav.Cell(willow.X+8000,willow.Z)),definition); AddUnitView(enemy); _placesEnemy=enemy.Id;
                _focus=PositionOf(willow.X,willow.Z); _distance=48; UpdateCamera(); SelectStructure(willow.Id);
                break;
            case 9:
                _placesChecks["source_power_icon_loaded"] = _commands.PlacePowerButton.Icon!=null && !_commands.PlacePowerButton.Disabled;
                GetViewport().PushInput(new InputEventKey {Keycode=Key.Q,PhysicalKeycode=Key.Q,Pressed=true},true);
                HoverAbilityPoint(_camera.UnprojectPosition(UnitPosition(w.Units[_placesEnemy]))); break;
            case 11:
                _placesChecks["place_hotkey_targets_without_units"]=_pendingPlacePower==_placesWillow && _orders.Pending==OrderKind.UsePlacePower && _selected.Count==0;
                _placesChecks["original_cursor_and_area"]=_commands.DisplayedCursorName==w.PlacePowers[PlaceAt(_placesWillow)!.Type].Cursor && _abilityAreaPreview?.Visible==true;
                CapturePlaces("willow-target");
                Click(_camera.UnprojectPosition(UnitPosition(w.Units[_placesEnemy]))); SetPaused(false); break;
            case 15:
                SetPaused(true);
                _placesChecks["willow_real_effect_and_cooldown"]=w.Units[_placesEnemy].TimeScalePpm==500_000 && !w.CanUsePlacePower(PlayerTeam,_placesWillow,out _);
                _placesChecks["place_button_shows_recharge"]=_commands.PlacePowerButton.Disabled && _commands.PlacePowerButton.TooltipText.Contains("Recharging");
                CapturePlaces("willow-cast"); break;
            case 19:
                var aviary=w.PlacesOfPower.First(p=>p.Type=="OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY"); aviary.Owner=PlayerTeam;
                _focus=PositionOf(aviary.X,aviary.Z); UpdateCamera(); SelectStructure(aviary.Id);
                _placesAviaryTarget=Enumerable.Range(0,w.Navigation.GroundCount).Where(w.Navigation.CanStand)
                    .Select(n=>w.Navigation.Center(n)).OrderBy(p=>SimWorld.DistanceSquared(p.X,p.Z,aviary.X,aviary.Z))
                    .Take(1000).Select(p=>PositionOf(p.X,p.Z)).First(p=>GlobalGroundAt(_camera.UnprojectPosition(p))!=null);
                break;
            case 21:
                _placesChecks["aviary_original_icon_loaded"]=_commands.PlacePowerButton.Icon!=null && !_commands.PlacePowerButton.Disabled;
                GetViewport().PushInput(new InputEventKey {Keycode=Key.Q,PhysicalKeycode=Key.Q,Pressed=true},true);
                HoverAbilityPoint(_camera.UnprojectPosition(_placesAviaryTarget)); break;
            case 23:
                _placesChecks["aviary_original_cursor"]=_commands.DisplayedCursorName==w.PlacePowers[PlaceAt(_pendingPlacePower)!.Type].Cursor;
                Click(_camera.UnprojectPosition(_placesAviaryTarget)); SetPaused(false); break;
            case 27:
                SetPaused(true);
                _placesChecks["aviary_real_scouting_area"]=w.PlacePowerAreas.Any(a=>a.Type=="OBJECT_PLACE_OF_POWER_RADAGHAST_AVIARY");
                CapturePlaces("aviary-cast"); break;
            case 31:
                var report=new {build=BuildInfo.Label,checks=_placesChecks,passed=_placesChecks.Values.All(v=>v),
                    places=w.PlacesOfPower.Select(p=>new {p.Id,p.Name,p.Type,model=_placeModels[p.Type],rules=p.Rules.Select(r=>r.Id)})};
                System.IO.File.WriteAllText(GamePaths.Report("places-smoke-report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
                GD.Print("PLACES_SMOKE "+JsonSerializer.Serialize(report)); _=FinishCombatAudio(report.passed?0:1); break;
        }
    }
}
