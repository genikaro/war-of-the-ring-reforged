"""The content pack's rule sections the game builds from the install at startup (build-194), as the Python import
builds them, for tests/formats --python-rules to check the C# port against.

python tools/rules_fingerprint.py "C:/Games/War of the Ring" rules.json

Writes {section path: value} for the sections built from the database (and the AI players) alone: factions,
team_colors, skirmish.start_state, skirmish.production, skirmish.research, skirmish.places_of_power and skirmish.ai,
exactly as content_pack.build() puts them into content.json. No model is exported and the install is only read.
"""
import argparse
import json
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


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('install')
    parser.add_argument('out')
    args = parser.parse_args()
    Path(args.out).write_text(json.dumps(sections(args.install), ensure_ascii=False, allow_nan=False, indent=1) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
