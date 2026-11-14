"""The content pack's rule sections the game builds from the install at startup (build-194), as the Python import
builds them, for tests/formats --python-rules to check the C# port against.

python tools/rules_fingerprint.py "C:/Games/War of the Ring" rules.json

Writes {section path: value} for the sections built from the database (and the AI players) alone: factions,
team_colors, skirmish.start_state, skirmish.production, skirmish.research, skirmish.places_of_power and skirmish.ai,
exactly as content_pack.build() puts them into content.json. Build-199 adds those that need the models too: the pack's
models list (each roster unit exported as export_units does, in a pool of processes), skirmish.economy and
skirmish.construction without its defenses, key by key. Models come from a layout of the install
(resolve_fingerprint.lay_out) in a temporary folder; the install is only read.
"""
import argparse
import json
import tempfile
from concurrent.futures import ProcessPoolExecutor
from pathlib import Path

import content_pack
import game_text
import lqr
import places_of_power
import prepare_prototype
import skirmish_ai
import skirmish_construction
import skirmish_map
import skirmish_production
import skirmish_research


class Install:
    """What the rule builders use of a resolver: the install's database."""

    def __init__(self, install):
        self.db = lqr.Database(str(Path(install) / 'Database'))


def sections(install):
    resolver = Install(install)
    db = resolver.db
    language = game_text.install_language(install)
    roster = prepare_prototype.SKIRMISH_ROSTER
    workers = sum(db['Units'].get('UNIT_G_HUMAN_WORKER')['PopulationCost'] for _ in range(skirmish_map.START_WORKERS))
    variables = db['WorldVariables'].records[0]
    citadel = db['Buildings'].get(skirmish_map.CITADEL)
    production = skirmish_production.production_data(resolver, roster)
    return {
        'factions': content_pack.faction_data(resolver, workers),
        'team_colors': prepare_prototype.team_colors(resolver),
        'skirmish.start_state': {'food': variables['DefaultInitialFood'], 'ore': variables['DefaultInitialOre'], 'population': workers,
                                 'population_cap': citadel['AdditionalPopAdded'] + variables['DefaultInitialPopulationCapacity']},
        'skirmish.production': production,
        'skirmish.research': skirmish_research.research_data(resolver),
        'skirmish.places_of_power': places_of_power.export(resolver),
        'skirmish.ai': skirmish_ai.export(resolver, skirmish_construction.CONSTRUCTION_ORDER + skirmish_construction.EVIL_CONSTRUCTION_ORDER
                                          + [skirmish_map.EVIL_WARPOST], list(production['units']), install=install, language=language),
    }


# The construction keys the game builds from the install (build-199); its defenses still come from the content pack.
CONSTRUCTION_KEYS = ['definitions', 'faction_definitions', 'buildings', 'order', 'palettes', 'corruption', 'placeable', 'upgrades',
                     'placement_radius_mm', 'placement_adapter']

_resolver = None
_root = None


def _start(install, root):
    """Points the modules that read extracted/ at the layout under root."""
    global _resolver, _root
    import lms2gltf
    import resolve_fingerprint
    import skirmish_economy
    import wor
    resolve_fingerprint.READ = ('Models', 'Skeletons', 'Cloths', 'UVAs', 'Meshes', 'Weights', 'Textures')
    resolve_fingerprint.LISTED = ('Effects',)
    _resolver = resolve_fingerprint.resolver_for(install, root)
    _root = Path(root)
    lms2gltf.ROOT = root
    wor.ROOT = _root
    skirmish_economy.ROOT = _root


def _unit(unit):
    """export_units for one roster unit: its entry of the pack's models list."""
    output = _root / 'pack' / unit
    output.mkdir(parents=True, exist_ok=True)
    return prepare_prototype.export_units(output, _resolver, [unit])[0]


def model_sections(install, workers=None):
    import resolve_fingerprint
    import skirmish_economy
    with tempfile.TemporaryDirectory(prefix='reforged-rules-') as root:
        _start(install, root)  # The layout's READ and LISTED first, then the resolver over the laid-out files.
        resolve_fingerprint.lay_out(install, root)
        _start(install, root)
        construction, _ = skirmish_construction.construction_data(_resolver, [])
        del construction['sites']
        construction.update(skirmish_construction.placement_data(_resolver))
        if set(construction) != set(CONSTRUCTION_KEYS):
            raise ValueError('construction keys changed: %s' % sorted(construction))
        result = {'skirmish.economy': skirmish_economy.economy_data(_resolver)}
        result.update({'skirmish.construction.' + key: construction[key] for key in CONSTRUCTION_KEYS})
        with ProcessPoolExecutor(workers, initializer=_start, initargs=(install, root)) as pool:
            result['models'] = list(pool.map(_unit, prepare_prototype.SKIRMISH_ROSTER))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    parser.add_argument('--workers', type=int)
    args = parser.parse_args()
    result = sections(args.install)
    result.update(model_sections(args.install, args.workers))
    Path(args.out).write_text(json.dumps(result, ensure_ascii=False, allow_nan=False, indent=1) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
