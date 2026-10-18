// extras.wotrAppearance v1. Pure evaluation keeps seeking and loop resets exact.
export function defaultChoice(model) {
  const state = model.states.find(s => s.id === model.defaultState);
  return state?.choices.find(c => c.weight > 0) ?? null;
}

function configured(mask, flags, config) {
  if ((mask & flags.blood) && !config.blood) return false;
  return !(mask & flags.levels.reduce((a, b) => a | b, 0)) ||
    Boolean(mask & flags.levels[config.level - 1]);
}

function shownAt(initial, tracks, slot, time) {
  let shown = initial;
  for (const [t, value] of tracks[slot] ?? []) if (t <= time) shown = value;
  return shown;
}

export function visibleNodes(data, choice = null, time = 0, config = data.config) {
  const result = new Map(), gates = [];
  for (const [i, model] of data.models.entries()) {
    const current = i === 0 && choice ? choice : defaultChoice(model);
    const t = i === 0 ? time : 0;
    const enabled = model.parent === null || gates[model.parent][model.attachment];
    model.parts.forEach((node, slot) => {
      if (node === null) return;
      const shown = current ? shownAt(current.meshes[slot] !== -1, current.meshTracks, slot, t) : true;
      result.set(node, enabled && shown && configured(model.partFlags[slot], data.flags, config));
    });
    gates.push(model.attachments.map((a, slot) => {
      const shown = current ? shownAt(current.models.includes(slot), current.modelTracks, slot, t) : a.shown;
      return enabled && (config.allAttachments || (shown && configured(a.flags, data.flags, config)));
    }));
  }
  return result;
}

export async function restoreAppearance(gltf) {
  const data = gltf.parser.json.extras?.wotrAppearance;
  if (!data || data.version !== 1) return null;
  // Hidden geometry is stored outside the default scene so ordinary glTF
  // consumers see correct idle gear. Restore only mesh leaves, never reparent
  // bones or use zero scales (which do not hide glTF skins reliably).
  for (const {node, parent} of data.hiddenNodes) {
    const object = await gltf.parser.getDependency('node', node);
    const anchor = parent === null ? gltf.scene : await gltf.parser.getDependency('node', parent);
    object.visible = false;
    anchor.add(object);
  }
  const nodes = new Map();
  for (const model of data.models) {
    for (const node of model.parts) {
      if (node !== null) nodes.set(node, await gltf.parser.getDependency('node', node));
    }
  }
  return {data, apply(choice, time, config) {
    for (const [node, shown] of visibleNodes(data, choice, time, config)) nodes.get(node).visible = shown;
  }};
}
