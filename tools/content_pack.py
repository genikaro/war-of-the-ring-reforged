"""The shared Good faction content pack: every model, rule table, HUD asset and AI table a skirmish map uses.

python tools/content_pack.py
python tools/content_pack.py --install "C:/Games/War of the Ring"

Map caches (prepare_prototype.py --skirmish) hold only the map and point here, so new units, buildings and research
reach every map by rebuilding this one pack. The pack also lists the collision body of every unit of both factions,
which the map caches precompute navigation for, so adding a unit never needs a map re-import.
"""
import argparse
import hashlib
import json
from pathlib import Path

import blood_effects
import combat_rules
import game_audio
import lmd
import lsk
import prototype_interface
import skirmish_ai
import skirmish_construction
import skirmish_economy
import skirmish_map
import skirmish_production
import skirmish_research
import places_of_power
import wor
from PIL import Image

PACK = wor.ROOT / 'converted/content/good'
FORMAT_VERSION = 1
CORRUPTION_TILES = wor.ROOT / 'extracted/Textures/TerrainSetTextures/Corruption'


def export_corruption_tiles(output, source=CORRUPTION_TILES):
    """The three dark ash tiles the source paints under corruption (build-136), as terrain/corruption_tile_NN.png."""
    target = Path(output) / 'terrain'
    target.mkdir(parents=True, exist_ok=True)
    for tile in sorted(Path(source).glob('Corruption_tile_*.dds')):
        with Image.open(tile) as image:
            image.convert('RGB').save(target / (tile.stem.lower() + '.png'))


def faction_bodies(resolver):
    """Distinct (radius, height) collision bodies of every Good and Evil unit whose model resolves."""
    bodies = set()
    for unit in resolver.db['Units'].records:
        if not unit.name.startswith(('UNIT_G_', 'UNIT_E_')):
            continue
        try:
            model = resolver.resolve(unit.name, recurse=False)
            skeleton = lsk.parse((wor.ROOT / model['skeletons'][0]['file']).read_bytes())
            record = resolver.db['Models'].get(unit['Model'])
            scaled = dict(model, scale=[record[k] for k in ['ScaleX', 'ScaleY', 'ScaleZ']])
            radius, height, _ = combat_rules.collision_profile(scaled, skeleton, lmd.parse((wor.ROOT / model['file']).read_bytes()).bounding_boxes)
        except (ValueError, KeyError, IndexError, FileNotFoundError):
            continue  # A unit whose model has no usable collision (one Evil troll) cannot be fielded either.
        bodies.add((radius, height))
    return bodies


START_UNITS = {'good': [], 'evil': [('UNIT_E_GOBLIN_SLAVE_MASTER', 1)]}


def faction_data(resolver, good_workers):
    """The sides a match can seat (build-133): each side's start building, worker, resource buildings and start units. The
    worker count and the Good population capacity come from the source start state; the Evil side starts with its
    workers beside the Goblin Hovel and one Slavemaster, whose 12 population is the only capacity it has (the source
    DefaultInitialPopulationCapacity is 0 and the Hovel adds none). The original's Evil start is not documented."""
    units = resolver.db['Units']
    result = {}
    for key, faction in skirmish_map.FACTIONS.items():
        worker_population = units.get(faction['worker'])['PopulationCost'] * skirmish_map.START_WORKERS
        result[key] = {'name': faction['name'], 'town_center': faction['town_center'], 'worker': faction['worker'], 'tech_root': faction['tech_root'],
                       'food_building': faction['food'], 'ore_building': faction['ore'],
                       'start_units': [{'unit': faction['worker'], 'count': skirmish_map.START_WORKERS}]
                                      + [{'unit': unit, 'count': count} for unit, count in START_UNITS[key]],
                       'start_population': worker_population}
    return result


def build(output=PACK, install=None):
    import prepare_prototype
    output = Path(output).resolve()
    resolver = prepare_prototype.open_resolver(output, install)
    output.mkdir(parents=True, exist_ok=True)
    roster = prepare_prototype.SKIRMISH_ROSTER
    models = prepare_prototype.export_units(output, resolver, roster)
    building_files = skirmish_construction.export_buildings(output, resolver)
    production = skirmish_production.export(output, resolver, roster)
    construction, _ = skirmish_construction.construction_data(resolver, [])
    del construction['sites']
    construction.update(skirmish_construction.placement_data(resolver))
    construction['defenses'] = skirmish_construction.defense_data(output, resolver)
    prototype_interface.export(output, resolver)
    export_corruption_tiles(output)
    combat_rules.export(output, resolver, roster=roster)
    blood_effects.export(output, resolver, roster)
    game_audio.export(output, resolver, roster=roster)
    places = places_of_power.export(resolver)
    places_of_power.export_models(output, resolver, places)
    rules = json.loads((output / 'combat/rules.json').read_text())
    # The combat rules may tune a body (Aragorn); every body a unit can have gets a navigation profile.
    bodies = faction_bodies(resolver) | {(u['BodyRadiusMm'], u['BodyHeightMm']) for u in rules['Units']}
    variables = resolver.db['WorldVariables'].records[0]
    citadel = resolver.db['Buildings'].get(skirmish_map.CITADEL)
    workers = sum(resolver.db['Units'].get('UNIT_G_HUMAN_WORKER')['PopulationCost'] for _ in range(skirmish_map.START_WORKERS))
    files = [p for d in ['units', 'buildings', 'objects', 'combat', 'interface', 'audio', 'terrain'] for p in sorted((output / d).rglob('*')) if p.is_file()]
    pack = {'format_version': FORMAT_VERSION, 'faction': 'good', 'factions': faction_data(resolver, workers),
            'team_colors': prepare_prototype.team_colors(resolver), 'models': models,
            'combat_rules': 'combat/rules.json', 'blood': 'combat/blood.json', 'interface': 'interface/index.json', 'audio': 'audio/index.json',
            'bodies': [{'BodyRadiusMm': r, 'BodyHeightMm': h} for r, h in sorted(bodies)],
            'rules': 'Imported combat stats, construction, upgrades, training and research of the Good faction.',
            'skirmish': {'start_state': {'food': variables['DefaultInitialFood'], 'ore': variables['DefaultInitialOre'], 'population': workers,
                                         'population_cap': citadel['AdditionalPopAdded'] + variables['DefaultInitialPopulationCapacity']},
                         'building_files': building_files, 'economy': skirmish_economy.economy_data(resolver),
                         'construction': construction, 'production': production,
                         'research': skirmish_research.research_data(resolver),
                         'places_of_power': places,
                         'ai': skirmish_ai.export(resolver, skirmish_construction.CONSTRUCTION_ORDER + skirmish_construction.EVIL_CONSTRUCTION_ORDER
                                                  + [skirmish_map.EVIL_WARPOST], list(production['units']))},
            'files': {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
    (output / 'content.json').write_text(json.dumps(pack, indent=2) + '\n')
    return pack


def load(pack=PACK):
    path = Path(pack) / 'content.json'
    if not path.is_file():
        raise ValueError('no content pack at %s; build it with python tools/content_pack.py' % pack)
    return json.loads(path.read_text())


def reject_slim_cache(manifest):
    """The per-section refresh scripts edit self-contained caches; a map cache using the pack has nothing to refresh."""
    if 'content' in manifest:
        raise ValueError('this map cache uses the shared content pack; rebuild it with python tools/content_pack.py')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=PACK)
    parser.add_argument('--install', type=Path)
    args = parser.parse_args()
    pack = build(args.out, args.install)
    print('Content pack: %d units, %d buildings, %d techniques, %d bodies -> %s' % (len(pack['models']),
          len(set(pack['skirmish']['building_files'])), len(pack['skirmish']['research']['techniques']), len(pack['bodies']), args.out))
