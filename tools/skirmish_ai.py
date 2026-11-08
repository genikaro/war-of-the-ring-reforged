"""Skirmish AI rules: the shipped .rai personalities and the source AI columns of the Tier 1 roster.

python tools/skirmish_ai.py --cache converted/skirmish

The .rai files are INI text whose 0-100 knobs Rings.exe turns into 0-1 profile alphas
(BattleScriptInterface::AIProfileSetAggressivenessAlpha and friends). The decision code that consumes
these numbers lives in Rings.exe and is not recovered; the runtime AI is an adapter built on them.
The files name no encoding: the names are in the ANSI code page of the install's language (cp1251
"Арагорн" in the Russian release, cp1252 "Grishnákh" in the English one; build-187).
"""
import argparse
import hashlib
import json
import os
from pathlib import Path

import game_text
import lqr
import wor

AI_PLAYERS = Path(lqr.INSTALL) / 'AIPlayers'
KNOBS = ['Difficulty', 'Aggressiveness', 'Tactics', 'Scouting', 'Harassment', 'UsesSpecialAbilities', 'UsesHeroes']
# The captured session used the built-in "Default medium AI", whose values live in Rings.exe: medium
# difficulty with every knob at the midpoint is an owner-approved adapter (2026-10-21).
DEFAULT_PERSONALITY = {'id': 'Default', 'display_name': 'Default (medium)', 'file': None, 'difficulty': 1,
                       'aggressiveness': 50, 'tactics': 50, 'scouting': 50, 'harassment': 50,
                       'uses_special_abilities': True, 'uses_heroes': True}
BUILDING_CLASSES = ['MILITARY', 'DEFENSIVE', 'ECONOMY', 'UPGRADE', 'SPECIAL']


def parse_rai(data, code_page='cp1251'):
    """Key = value pairs of the [AIPlayerSettings] section; # lines are comments. `code_page` is the install's
    (game_text.code_page of its language)."""
    values = {}
    for line in data.decode(code_page).splitlines():
        line = line.strip()
        if '=' in line and not line.startswith('#'):
            key, value = (part.strip() for part in line.split('=', 1))
            values[key] = value
    return values


def personalities(install=None, language=None):
    """The default personality and the install's .rai files (`install` defaults to lqr.INSTALL). `language` is the
    install's text language, detected from its text when not given."""
    folder = Path(install) / 'AIPlayers' if install else AI_PLAYERS
    paths = sorted(folder.glob('*.rai'))
    if paths and language is None:
        language = game_text.install_language(folder.parent)
    result = [dict(DEFAULT_PERSONALITY)]
    for path in paths:
        values = parse_rai(path.read_bytes(), game_text.code_page(language))
        missing = [k for k in KNOBS + ['Name'] if k not in values]
        if missing:
            raise ValueError('%s lacks %s' % (path.name, ', '.join(missing)))
        source = Path(os.path.abspath(path))  # an install linked into the repository keeps its name there
        result.append({'id': path.stem.replace(' ', ''), 'display_name': values['Name'],
                       'file': (source.relative_to(wor.ROOT) if source.is_relative_to(wor.ROOT) else Path(folder.name, path.name)).as_posix(),
                       'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                       'difficulty': int(values['Difficulty']), 'aggressiveness': int(values['Aggressiveness']),
                       'tactics': int(values['Tactics']), 'scouting': int(values['Scouting']),
                       'harassment': int(values['Harassment']),
                       'uses_special_abilities': values['UsesSpecialAbilities'] == '1', 'uses_heroes': values['UsesHeroes'] == '1'})
    return result


def member_kind(resolver, member_class):
    """A war-party member class wants melee or missile users, read from its descriptor list."""
    record = resolver.db['AIWarPartyMemberClasses'].get(member_class)
    for i in range(1, 11):
        descriptor = record['Descriptor%d' % i]
        if descriptor < 0:
            continue
        name = resolver.db['AIUnitDescriptors'].get(descriptor).name
        modifier = resolver.db['AIUnitDescriptorModifiers'].get(record['Descriptor%dModifier' % i]).name
        if modifier.endswith(('DESIRED', 'REQUIRED')) and not modifier.endswith('UNDESIRED'):
            if name.endswith('MELEE_USER'):
                return record['Name'], 'melee'
            if name.endswith('MISSILE_USER'):
                return record['Name'], 'missile'
    return record['Name'], 'any'


def war_party(resolver, name):
    record = resolver.db['AIWarParties'].get(name)
    members = []
    for i in range(1, 6):
        if record['MemberClass%d' % i] >= 0:
            label, kind = member_kind(resolver, record['MemberClass%d' % i])
            members.append({'class': label, 'kind': kind, 'share_ppm': round(record['MemberClass%dPercentage' % i]*1_000_000)})
    return {'min_size': record['MinSize'], 'max_size': record['MaxSize'], 'members': members}


def export(resolver, buildings, units, install=None, language=None):
    """AI columns for the given Tier 1 buildings and trainable units, plus the Assault war party; the personalities come
    from `install` in its `language` (personalities)."""
    building_rules = {}
    for name in buildings:
        record = resolver.db['Buildings'].get(name)
        building_rules[name] = {'class': BUILDING_CLASSES[record['AIBuildingClass']], 'priority': record['AIBuildingPriority'],
            'must_have_first_pass': bool(record['AIMustHaveFirstPass']), 'max_default': record['AIMaxDefault'],
            'lower_priority_if_exists': bool(record['AILowerPriorityIfExists']), 'limit_multiples': bool(record['AILimitMultiples']),
            'defensive_score': record['AIDefensiveScore'], 'offensive_score': record['AIOffensiveScore']}
    unit_rules = {}
    for name in units:
        record = resolver.db['Units'].get(name)
        origin = resolver.db['Buildings'].get(record['AIBuildingOfOrigin'])
        unit_rules[name] = {'origin': origin.name if origin else None, 'never_add': bool(record['AINeverAddUnit']),
            'always_add': bool(record['AIAlwaysAddUnit']), 'assault': record['AIAssaultEffectiveness'],
            'raiding': record['AIRaidingEffectiveness'],
            'max_per_party': record['AIMaxUnitsOfTypePerWarParty'], 'max_per_party_easy': record['AIMaxUnitsOfTypePerWarPartyEasy']}
    return {'format_version': 1, 'personalities': personalities(install, language), 'default_personality': DEFAULT_PERSONALITY['id'],
            'buildings': building_rules, 'units': unit_rules,
            'war_parties': {'ASSAULT': war_party(resolver, 'AIWARPARTY_ASSAULT'), 'RAIDING': war_party(resolver, 'AIWARPARTY_RAIDING'),
                            'PLACEOFPOWERSCOUT': war_party(resolver, 'AIWARPARTY_PLACEOFPOWERSCOUT')},
            'policy': 'Source data: .rai knobs, Buildings AI columns, Units AIAssaultEffectiveness/AIRaidingEffectiveness/AINeverAddUnit and '
                      'the AIWARPARTY_ASSAULT, AIWARPARTY_RAIDING and AIWARPARTY_PLACEOFPOWERSCOUT sizes and member shares. Decision '
                      'logic is an adapter; see prototype/SKIRMISH.md build-27, build-53 and build-54.'}


def refresh(output):
    import prepare_prototype
    import skirmish_construction
    output = output.resolve()
    resolver = prepare_prototype.open_resolver(output)
    path = output / 'manifest.json'
    manifest = json.loads(path.read_text())
    if manifest.get('mode') != 'skirmish':
        raise ValueError('AI refresh requires a skirmish cache')
    import content_pack
    content_pack.reject_slim_cache(manifest)
    data = manifest['skirmish']
    data['ai'] = export(resolver, skirmish_construction.CONSTRUCTION_ORDER, list(data['production']['units']))
    path.write_text(json.dumps(manifest, indent=2)+'\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path, required=True)
    refresh(parser.parse_args().cache)
