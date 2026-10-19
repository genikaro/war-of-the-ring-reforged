import test from 'node:test';
import assert from 'node:assert/strict';
import {defaultChoice, visibleNodes, restoreAppearance, textureSlots} from './model-appearance.mjs';

function fixture() {
  const idle = {clip: 'Idle', weight: 86, meshes: [0], models: [0], meshTracks: [], modelTracks: []};
  const attack = {clip: 'Attack', weight: 100, meshes: [0], models: [1], meshTracks: [],
    modelTracks: [[], [], [[0, false], [.285, true], [.683, false], [1, false]]]};
  return {version: 1, config: {level: 1, blood: false, allAttachments: false},
    flags: {levels: [32, 64, 128, 262144], blood: 4194307}, hiddenNodes: [{node: 3, parent: null}],
    models: [{parent: null, attachment: null, parts: [0], partFlags: [0], defaultState: 35,
      attachments: [{shown: false, flags: 0}, {shown: true, flags: 0}, {shown: false, flags: 0}],
      states: [{id: 2, choices: [attack]}, {id: 35, choices: [idle]}]},
      ...[1, 2, 3].map((node, attachment) => ({parent: 0, attachment, parts: [node], partFlags: [0],
        defaultState: null, attachments: [], states: []}))]};
}

test('declared default wins over first clip; masks override attachment shown', () => {
  const d = fixture();
  assert.equal(defaultChoice(d.models[0]).clip, 'Idle');
  assert.deepEqual([...visibleNodes(d).values()], [true, true, false, false]);
});

test('arrow visibility follows normalized time and resets on backwards seeks/loops', () => {
  const d = fixture(), choice = d.models[0].states[0].choices[0];
  for (const [t, arrow] of [[0, false], [.284, false], [.285, true], [.682, true],
    [.683, false], [1, false], [0, false], [.5, true], [.1, false]]) {
    assert.deepEqual([...visibleNodes(d, choice, t).values()], [true, false, true, arrow]);
  }
});

test('mesh disappearance keys, including exact endpoint', () => {
  const d = fixture(), choice = d.models[0].states[0].choices[0];
  choice.meshTracks = [[[0, true], [.687, false], [1, false]]];
  assert.equal(visibleNodes(d, choice, .686).get(0), true);
  assert.equal(visibleNodes(d, choice, .687).get(0), false);
  assert.equal(visibleNodes(d, choice, 1).get(0), false);
  assert.equal(visibleNodes(d, choice, 0).get(0), true);
});

test('cloth visibility restores on backwards seeks and obeys the owner gate', async () => {
  const d = fixture(), choice = d.models[0].states[0].choices[0];
  d.models[0].cloths = [{node: 4, shown: false, flags: 0}];
  d.models[2].cloths = [{node: 5, shown: true, flags: 0}];
  choice.cloths = [0];
  choice.clothTracks = [[[0, true], [.5, false]]];
  const nodes = Array.from({length: 6}, () => ({visible: true}));
  const controller = await restoreAppearance({parser: {json: {extras: {wotrAppearance: d}},
    getDependency: async (_, i) => nodes[i]}, scene: {add() {}}});
  controller.apply(choice, .25);
  assert.equal(nodes[4].visible, true);
  assert.equal(nodes[5].visible, true);
  controller.apply(choice, .5);
  assert.equal(nodes[4].visible, false);
  controller.apply(choice, .1);
  assert.equal(nodes[4].visible, true);
  controller.apply(null, 0);
  assert.equal(nodes[5].visible, false);
});

test('level and blood selection uses named flags and permits masks with multiple levels', () => {
  const d = fixture();
  d.models[0].partFlags[0] = 1 | 32 | 64;
  assert.equal(visibleNodes(d).get(0), false);
  assert.equal(visibleNodes(d, null, 0, {...d.config, blood: true, level: 2}).get(0), true);
  assert.equal(visibleNodes(d, null, 0, {...d.config, blood: true, level: 3}).get(0), false);
});

test('hidden ancestor gates detached skinned descendants and debug exposes attachments', () => {
  const d = fixture();
  d.models[2].attachments = [{shown: true, flags: 0}];
  d.models.push({parent: 2, attachment: 0, parts: [4], partFlags: [0], states: [], defaultState: null, attachments: []});
  assert.equal(visibleNodes(d).get(4), false);
  assert.equal(visibleNodes(d, null, 0, {...d.config, allAttachments: true}).get(4), true);
});

test('restore hidden mesh leaves and update all nodes, including node zero', async () => {
  const d = fixture(), nodes = Array.from({length: 4}, () => ({visible: true}));
  const added = [];
  const gltf = {parser: {json: {extras: {wotrAppearance: d}}, getDependency: async (type, i) => nodes[i]},
    scene: {add: node => added.push(node)}};
  const controller = await restoreAppearance(gltf);
  assert.deepEqual(added, [nodes[3]]);
  controller.apply(d.models[0].states[0].choices[0], .5);
  assert.deepEqual(nodes.map(n => n.visible), [true, false, true, true]);
  controller.apply(null, 0);
  assert.deepEqual(nodes.map(n => n.visible), [true, true, false, false]);
  assert.equal(await restoreAppearance({parser: {json: {}}}), null);
});

test('texture keys swap per-instance material copies and fall back to defaults', async () => {
  const d = fixture(), [attack, idle] = d.models[0].states.map(s => s.choices[0]);
  idle.textureKeys = [[0, [1, 2]], [1, [-1, -1]]];          // damaged + mask; end key keeps
  attack.textureKeys = [];
  d.models[0].textures = [10, 11, 12];
  const shared = {map: 'default', emissiveMap: 'mask0', clone() { return {...this}; }};
  const mesh = {isMesh: true, material: shared, visible: true, traverse(f) { f(this); }};
  const nodes = [mesh, ...Array.from({length: 3}, () => ({visible: true}))];
  const textures = {10: {name: 'healthy'}, 11: {name: 'damaged'}, 12: {name: 'mask'}};
  const gltf = {parser: {json: {extras: {wotrAppearance: d}},
    getDependency: async (type, i) => type === 'texture' ? textures[i] : nodes[i]}, scene: {add() {}}};
  const controller = await restoreAppearance(gltf);
  assert.notEqual(mesh.material, shared);
  assert.deepEqual(textureSlots(idle, 1), [1, 2]);
  controller.apply(null, 0);
  assert.equal(mesh.material.map.name, 'damaged');
  assert.equal(mesh.material.emissiveMap.name, 'mask');
  controller.apply(attack, .5);
  assert.equal(mesh.material.map, 'default');
  assert.equal(mesh.material.emissiveMap, 'mask0');
  assert.equal(shared.map, 'default');
});
