"""Decoded model appearance projected onto exported glTF mesh nodes.

glTF 2.0 has no visibility animation. The default scene contains only visible
mesh nodes; extras.wotrAppearance retains their bindings and state timelines
so the viewer (and a future engine importer) can restore and switch them.
"""


def flag_policy(records):
    """Named ModelFlags IDs are bit positions, not animation state IDs."""
    flags = {r.name: 1 << r.id for r in records}
    return {
        'levels': [flags.get('MODEL_FLAG_LEVEL_%d' % n, 0) for n in range(1, 5)],
        'blood': sum(flags.get('MODEL_FLAG_' + name, 0) for name in
                     ('WEAPON_BLOOD_RIGHT', 'WEAPON_BLOOD_LEFT', 'CRITICAL_DEATH_BLOOD')),
    }


def configuration_visible(mask, policy, config):
    """Preview policy: selected gear level, optional blood; leave other tags alone.

    A mask tags resources for gameplay flag operations. It is NOT a set of
    requirements to AND together (one resource can have all four level tags).
    Full runtime flag operations remain the responsibility of the game rules.
    """
    levels = policy['levels']
    if mask & policy['blood'] and not config['blood']:
        return False
    return not mask & sum(levels) or bool(mask & levels[config['level'] - 1])


def visibility_tracks(tracks):
    # The first property flag enables a change to "shown". Parameter-only
    # keys must not change visibility. Keep stable ordering for equal times.
    return [[[key['time'], key['shown']] for key in track if key['flags'][0]]
            for track in tracks]


def instance(res, parent=None, attachment=None, clip_prefix=''):
    """One model instance, retaining source slot indices even for missing files."""
    attachments = [a for a in res.get('attachments', []) if a['kind'] == 'model']
    states = []
    for state in res.get('animation_states', []):
        choices = []
        for choice in state['choices']:
            name = choice.get('clip_name')
            choices.append({
                'clip': clip_prefix + name if name else None,
                'weight': choice['weight'],
                'meshes': choice['mesh_selections'],
                'models': choice['models']['indices'],
                'meshTracks': visibility_tracks(choice['mesh_tracks']),
                'modelTracks': visibility_tracks(choice['model_tracks']),
            })
        states.append({'id': state['id'], 'name': state['name'],
                       'totalWeight': state['total_weight'], 'choices': choices})
    default = next((s for s in states if s['name'] == res.get('default_animation_state')), None)
    # A declared class default is sometimes absent from this model. Retain the
    # declaration and make the deterministic fallback explicit in the metadata.
    fallback = next((s for s in states if any(c['weight'] for c in s['choices'])), None)
    return {
        'model': res.get('model'), 'parent': parent, 'attachment': attachment,
        'declaredDefaultState': res.get('default_animation_state'),
        'defaultState': (default or fallback)['id'] if default or fallback else None,
        'parts': [None] * len(res['parts']),
        'partFlags': [p.get('state_mask', 0) for p in res['parts']],
        'attachments': [{'shown': a['shown'], 'flags': a['state_mask']} for a in attachments],
        'states': states,
    }


def default_choice(model):
    state = next((s for s in model['states'] if s['id'] == model['defaultState']), None)
    return next((c for c in state['choices'] if c['weight']), None) if state else None


def shown_at(initial, tracks, index, time):
    shown = initial
    if index < len(tracks):
        for key_time, value in tracks[index]:
            if key_time <= time:
                shown = value
    return shown


def visible_nodes(data, choice=None, time=0):
    """Evaluate from the initial mask every time, including backwards seeks/loops.

    The supplied choice/time drives the root. Nested models use their declared
    default state until independent attachment animation is implemented.
    """
    visible, attachment_gates = {}, []
    config, policy = data['config'], data['flags']
    for i, model in enumerate(data['models']):
        current = choice if i == 0 and choice is not None else default_choice(model)
        t = time if i == 0 else 0
        parent = model['parent']
        enabled = True if parent is None else attachment_gates[parent][model['attachment']]
        for slot, node in enumerate(model['parts']):
            if node is None:
                continue
            shown = current['meshes'][slot] != -1 if current else True
            if current:
                shown = shown_at(shown, current['meshTracks'], slot, t)
            visible[node] = enabled and shown and configuration_visible(model['partFlags'][slot], policy, config)
        gates = []
        for slot, attachment in enumerate(model['attachments']):
            shown = slot in current['models'] if current else attachment['shown']
            if current:
                shown = shown_at(shown, current['modelTracks'], slot, t)
            shown = shown and configuration_visible(attachment['flags'], policy, config)
            gates.append(enabled and (config['allAttachments'] or shown))
        attachment_gates.append(gates)
    return visible


def default_scene(exporter, data):
    """Disconnect hidden mesh leaves, preserving joints and all animation targets."""
    visible = visible_nodes(data)
    parents = {child: parent for parent, node in enumerate(exporter.nodes)
               for child in node.get('children', [])}
    hidden = []
    for node, shown in visible.items():
        if shown:
            continue
        parent = parents.get(node)
        children = exporter.scene if parent is None else exporter.nodes[parent]['children']
        children.remove(node)
        if parent is not None and not children:
            del exporter.nodes[parent]['children']
        hidden.append({'node': node, 'parent': parent})
    data['hiddenNodes'] = hidden
