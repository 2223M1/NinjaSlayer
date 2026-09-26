import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { Skeleton, Physics, MixBlend, MixDirection } from '@esotericsoftware/spine-core';
import { readNative, parseNative, subdivideLimbs, BinaryWriter } from './native.mjs';
import { bakeDeath } from './retarget.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const pack = process.argv.find((v, i) => i > 1 && !v.startsWith('--'))
    ?? 'C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck';
const output = path.join(root, 'NinjaSlayer/animations/architect');
const name = 'ninjaslayer_soft_death';
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const sources = readNative(pack);
const original = parseNative(sources.architect);
const { data, reader, bytes, subdivided } = subdivideLimbs(sources.architect);
if (data.findAnimation(name)) throw Error('Source already includes private death animation');
const reference = parseNative(sources.skulking_colony);
const { frames, meshes, duration, measurements, sizeRatio, mapping, sourcePhysicsConstraints, sourcePathConstraints } = bakeDeath(data, reference.data);
const w = new BinaryWriter();
function timeline(type, index, keys) {
    w.byte(type); w.int(frames.length); w.int((frames.length - 1) * keys.length);
    const write = i => { w.float(frames[i].t); for (const k of keys) w.float(frames[i].bones[index][k]); };
    const tangent = (frame, key) => {
        if (frame === 0 || frame === frames.length - 1) return 0;
        const a = (frames[frame].bones[index][key] - frames[frame - 1].bones[index][key]) / (frames[frame].t - frames[frame - 1].t);
        const b = (frames[frame + 1].bones[index][key] - frames[frame].bones[index][key]) / (frames[frame + 1].t - frames[frame].t);
        return a * b <= 0 ? 0 : 2 * a * b / (a + b);
    };
    write(0);
    for (let f = 1; f < frames.length; f++) {
        write(f); w.byte(2);
        const left = frames[f - 1], right = frames[f], dt = right.t - left.t;
        for (const k of keys) {
            w.float(left.t + dt / 3); w.float(left.bones[index][k] + tangent(f - 1, k) * dt / 3);
            w.float(right.t - dt / 3); w.float(right.bones[index][k] - tangent(f, k) * dt / 3);
        }
    }
}
w.string(name);
w.int(data.bones.length * 4 + data.ikConstraints.length + data.transformConstraints.length + data.physicsConstraints.length + meshes.length);
w.int(0);
w.int(data.bones.length);
for (let i = 0; i < data.bones.length; i++) {
    w.int(i); w.int(4);
    timeline(0, i, ['rotation']); timeline(1, i, ['x', 'y']);
    timeline(4, i, ['scaleX', 'scaleY']); timeline(7, i, ['shearX', 'shearY']);
}
w.int(data.ikConstraints.length);
for (let i = 0; i < data.ikConstraints.length; i++) { w.int(i); w.int(1); w.int(0); w.byte(0); w.float(0); }
w.int(data.transformConstraints.length);
for (let i = 0; i < data.transformConstraints.length; i++) { w.int(i); w.int(1); w.int(0); for (let j = 0; j < 7; j++) w.float(0); }
w.int(0);
w.int(data.physicsConstraints.length);
for (let i = 0; i < data.physicsConstraints.length; i++) { w.int(i + 1); w.int(1); w.byte(7); w.int(1); w.int(0); w.float(0); w.float(0); }
w.int(1); w.int(data.skins.indexOf(data.defaultSkin)); w.int(meshes.length);
for (let c = 0; c < meshes.length; c++) {
    const entry = meshes[c], ref = reader.strings.indexOf(entry.name) + 1;
    if (!ref) throw Error(`Missing attachment string: ${entry.name}`);
    w.int(entry.slotIndex); w.int(1); w.int(ref); w.byte(0); w.int(frames.length); w.int(0);
    w.float(frames[0].t);
    for (let f = 0; f < frames.length; f++) {
        const deform = frames[f].deforms[c]; w.int(deform.length); w.int(0);
        deform.forEach((v, i) => w.float(entry.attachment.bones ? v : v - entry.attachment.vertices[i]));
        if (f + 1 < frames.length) { w.float(frames[f + 1].t); w.byte(0); }
    }
}
w.int(0); w.int(0);
const first = reader.starts[0];
const countOffset = first.start - Buffer.byteLength(first.name) - 2;
if (data.animations.length >= 127 || bytes[countOffset] !== data.animations.length) throw Error('Animation directory mismatch');
const result = Buffer.concat([bytes, w.bytes()]); result[countOffset]++;
const verified = parseNative(sources.architect, result);
if (verified.data.animations.length !== data.animations.length + 1
    || Math.abs(verified.data.findAnimation(name).duration - duration) > 1e-5) throw Error('Export validation failed');
// Existing animation bytes, names and atlas bindings must survive unchanged.
const oldStart = original.reader.starts[0].start;
if (!sources.architect.skel.bytes.subarray(oldStart).equals(bytes.subarray(first.start)))
    throw Error('An original animation changed');
const replay = new Skeleton(verified.data);
let maximumVertexError = 0;
let exportMismatch;
for (let f = 0; f < frames.length; f++) {
    const frame = frames[f];
    replay.setToSetupPose();
    verified.data.findAnimation(name).apply(replay, 0, frame.t, false, [], 1, MixBlend.replace, MixDirection.mixIn);
    replay.updateWorldTransform(Physics.none);
    meshes.forEach((entry, c) => {
        const actual = replay.slots[entry.slotIndex].deform, expected = frame.deforms[c];
        if (actual.length !== expected.length) throw Error(`Deform length mismatch: ${entry.name}`);
        expected.forEach((v, i) => {
            const error = Math.abs(v - actual[i]);
            if (error > maximumVertexError) {
                maximumVertexError = error;
                exportMismatch = { frame: f, mesh: entry.name, slot: entry.slotIndex, index: i, expected: v, actual: actual[i], weighted: !!entry.attachment.bones };
            }
        });
    });
}
if (maximumVertexError > .01) throw Error(`Export precision loss: ${JSON.stringify(exportMismatch)}`);
const report = {
    spineVersion: data.version, runtimePackage: '@esotericsoftware/spine-core@4.2.43',
    source: sources.architect.skel.entry, sourceSha256: sha(sources.architect.skel.bytes),
    motionSource: sources.skulking_colony.skel.entry, motionSourceSha256: sha(sources.skulking_colony.skel.bytes),
    motionAnimation: 'die', sourcePhysicsConstraints, sourcePathConstraints,
    originalAnimations: data.animations.map(a => a.name), originalAnimationBytesPreserved: true,
    addedAnimation: name, duration, sampleHz: 120, frames: frames.length, sizeRatio,
    subdividedRegions: subdivided, deformMeshes: meshes.length, mapping,
    maximumExportError: maximumVertexError, outputSha256: sha(result), measurements
};
if (!process.argv.includes('--check')) {
    fs.mkdirSync(output, { recursive: true });
    fs.writeFileSync(path.join(output, 'architect.spskel'), result);
    fs.writeFileSync(path.join(output, 'SOURCE.json'), JSON.stringify(report, null, 2) + '\n');
}
console.log(JSON.stringify({ duration, frames: frames.length, bones: data.bones.length,
    subdividedRegions: subdivided.length, deformMeshes: meshes.length, maximumVertexError,
    outputBytes: result.length, end: measurements.at(-1), wrote: !process.argv.includes('--check') }, null, 2));
