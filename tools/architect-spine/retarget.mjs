import { Skeleton, Physics, MixBlend, MixDirection, MeshAttachment, RegionAttachment } from '@esotericsoftware/spine-core';

const add = (a, b) => ({ x: a.x + b.x, y: a.y + b.y });
const sub = (a, b) => ({ x: a.x - b.x, y: a.y - b.y });
const mul = (a, s) => ({ x: a.x * s, y: a.y * s });
const dot = (a, b) => a.x * b.x + a.y * b.y;
const length = a => Math.hypot(a.x, a.y);
const mix = (a, b, p) => add(mul(a, 1 - p), mul(b, p));
const point = bone => ({ x: bone.worldX, y: bone.worldY });
const clamp = (p, lo = 0, hi = 1) => Math.max(lo, Math.min(hi, p));
const matrix = b => ({ x: b.worldX, y: b.worldY, a: b.a, b: b.b, c: b.c, d: b.d });
const inverseVector = (b, p) => {
    const det = b.a * b.d - b.b * b.c;
    if (Math.abs(det) < 1e-8) throw Error('Singular retarget basis');
    return { x: (b.d * p.x - b.b * p.y) / det, y: (b.a * p.y - b.c * p.x) / det };
};
const vector = (b, p) => ({ x: b.a * p.x + b.b * p.y, y: b.c * p.x + b.d * p.y });

function vertices(slot) {
    const a = slot.attachment;
    if (a instanceof MeshAttachment) {
        const v = new Float32Array(a.worldVerticesLength);
        a.computeWorldVertices(slot, 0, v.length, v, 0, 2);
        return Array.from({ length: v.length / 2 }, (_, i) => ({ x: v[i * 2], y: v[i * 2 + 1] }));
    }
    if (a instanceof RegionAttachment) {
        const v = new Float32Array(8); a.computeWorldVertices(slot, v, 0, 2);
        return Array.from({ length: 4 }, (_, i) => ({ x: v[i * 2], y: v[i * 2 + 1] }));
    }
    return [];
}

function curve(points) {
    const distances = [0];
    for (let i = 1; i < points.length; i++) distances.push(distances.at(-1) + length(sub(points[i], points[i - 1])));
    return { points, distances, length: distances.at(-1) };
}
function sample(c, u) {
    const distance = clamp(u) * c.length;
    let i = 1;
    while (i < c.points.length - 1 && c.distances[i] < distance) i++;
    const span = c.distances[i] - c.distances[i - 1];
    const p = span > 1e-6 ? (distance - c.distances[i - 1]) / span : 0;
    return mix(c.points[i - 1], c.points[i], p);
}
function tangent(c, u) {
    const v = sub(sample(c, Math.min(1, u + .005)), sample(c, Math.max(0, u - .005)));
    return mul(v, 1 / Math.max(1e-6, length(v)));
}
function project(c, p) {
    let best = null;
    for (let i = 1; i < c.points.length; i++) {
        const a = c.points[i - 1], delta = sub(c.points[i], a);
        const q = clamp(dot(sub(p, a), delta) / Math.max(1e-6, dot(delta, delta)));
        const anchor = add(a, mul(delta, q)), error = length(sub(p, anchor));
        if (!best || error < best.error) best = { error, u: (c.distances[i - 1] + q * length(delta)) / c.length, anchor };
    }
    return best;
}

export function bakeDeath(data, referenceData) {
    const skeleton = new Skeleton(data), source = new Skeleton(referenceData);
    source.setSkinByName('normal');
    const die = referenceData.findAnimation('die');
    const duration = die.duration;
    source.setToSetupPose();
    die.apply(source, 0, 0, false, [], 1, MixBlend.replace, MixDirection.mixIn);
    source.updateWorldTransform(Physics.reset);
    data.findAnimation('hurt').apply(skeleton, 0, .1, false, [], 1, MixBlend.replace, MixDirection.mixIn);
    skeleton.updateWorldTransform(Physics.none);
    for (const b of skeleton.bones) b.updateAppliedTransform();
    const initial = skeleton.bones.map(matrix);
    const sourceInitial = new Map(source.bones.map(b => [b.data.name, matrix(b)]));
    const at = n => skeleton.findBone(n);
    const src = n => source.findBone(n);
    const sizeRatio = at('cog').worldY / src('cog').worldY;
    const originX = at('cog').worldX - src('cog').worldX * sizeRatio;
    const hipHeight = src('cog').worldY;
    const endReference = new Skeleton(referenceData);
    die.apply(endReference, 0, duration, false, [], 1, MixBlend.replace, MixDirection.mixIn);
    endReference.updateWorldTransform(Physics.none);
    const settledHipHeight = endReference.findBone('cog').worldY;
    const initialDeforms = skeleton.slots.map(s => [...s.deform]);
    const meshes = data.defaultSkin.getAttachments().filter(e => e.attachment instanceof MeshAttachment
        && skeleton.slots[e.slotIndex].attachment === e.attachment);
    const initialVertices = new Map(skeleton.slots.map(s => [s.data.index, vertices(s)]));
    for (const ik of skeleton.ikConstraints) ik.mix = 0;
    for (const c of skeleton.transformConstraints) c.mixRotate = c.mixX = c.mixY = c.mixScaleX = c.mixScaleY = c.mixShearY = 0;
    for (const c of skeleton.physicsConstraints) c.mix = 0;

    const arms = {};
    for (const side of ['f', 'b']) {
        const slot = source.findSlot(side === 'f' ? 'arm_right' : 'arm_left');
        const base = point(src(`arm_${side}`)), v = vertices(slot);
        const mean = mul(v.reduce(add, { x: 0, y: 0 }), 1 / v.length);
        const axis = mul(sub(mean, base), 1 / length(sub(mean, base)));
        const distances = v.map(p => dot(sub(p, base), axis)), end = Math.max(...distances);
        // Track sections of the actual weighted arm mesh, including native deform/physics.
        const sections = Array.from({ length: 8 }, (_, j) => {
            const d = end * (j + 1) / 8;
            const weights = distances.map(x => Math.exp(-Math.pow((x - d) / (end * .13), 2)));
            const total = weights.reduce((a, b) => a + b, 0);
            return weights.map(w => w / total);
        });
        arms[side] = () => {
            const current = vertices(slot);
            return curve([point(src(side === 'f' ? 'shoulder_r' : 'body1')), point(src(`arm_${side}`)), ...sections.map(weights =>
                current.reduce((sum, p, i) => add(sum, mul(p, weights[i])), { x: 0, y: 0 }))]);
        };
    }
    const sourceCurves = {
        leg_f: () => curve(Array.from({ length: 20 }, (_, i) => point(src(`leg_f_path${20 - i}`))).concat(point(src('foot_f')))),
        leg_b: () => curve(Array.from({ length: 20 }, (_, i) => point(src(`leg_b_path${i + 1}`))).concat(point(src('foot_b')))),
        arm_f: arms.f, arm_b: arms.b,
        drape: () => curve([point(src('body2')), point(src('cog')),
            ...Array.from({ length: 10 }, (_, i) => mix(point(src(`leg_f_path${20 - i * 2}`)), point(src(`leg_b_path${1 + i * 2}`)), .5))])
    };
    const targetNames = {
        leg_f: ['leg_upper_f', 'cog4', 'foot_f1'], leg_b: ['leg_upper_b', 'leg_lower_b', 'foot1_b'],
        arm_f: ['shoulder front', 'arm_upper_f', 'arm_lower_f', 'hand_f'],
        arm_b: ['shoulder_b', 'arm_upper_b', 'arm_lower_b', 'hand_b'],
        drape: ['body_upper', 'cog', 'cape b2', 'cape b4']
    };
    const curves = Object.fromEntries(Object.entries(targetNames).map(([name, names]) => {
        const target = curve(names.map(n => point(at(n)))), native = sourceCurves[name]();
        return [name, { target, native, scale: target.length / native.length, current: target }];
    }));
    const primary = {
        cog: 'cog', body_lower: 'body1', body_upper: 'body2', neck: 'body3', head: 'head_base',
        shadow: 'shadow'
    };
    function group(bone) {
        for (let b = bone; b; b = b.parent) {
            const name = b.data.name;
            if (name === 'head') return 'head';
            if (/^sleeve_b/u.test(name)) return 'arm_b';
            if (/^sleeve_f/u.test(name)) return 'arm_f';
            if (/^cape|^sash/u.test(name)) return 'drape';
            if (name === 'leg_upper_f' || name === 'leg_base_f') return 'leg_f';
            if (name === 'leg_upper_b' || name === 'leg_base_b') return 'leg_b';
            if (name === 'arm_upper_f' || name === 'shoulder front') return 'arm_f';
            if (name === 'arm_upper_b' || name === 'shoulder_b') return 'arm_b';
            if (name in primary) return name;
            if (name === 'pen') return 'pen';
        }
        return 'root';
    }
    let retention = 1;
    function anchor(native, start) {
        return { x: originX + native.x * sizeRatio + (start.x - originX - native.initial.x * sizeRatio) * retention,
            y: native.y * sizeRatio + (start.y - native.initial.y * sizeRatio) * retention };
    }
    function nativePoint(name) {
        return { ...point(src(name)), initial: sourceInitial.get(name) };
    }
    function warpRigid(p, targetName, sourceName) {
        const initialBone = initial[at(targetName).data.index], old = sourceInitial.get(sourceName), current = src(sourceName);
        return add(anchor(nativePoint(sourceName), initialBone), vector(current, inverseVector(old, sub(p, initialBone))));
    }
    function warpCurve(p, name) {
        const c = curves[name], projected = project(c.target, p);
        const before = tangent(c.target, projected.u), after = tangent(c.current, projected.u);
        const offset = sub(p, projected.anchor);
        const along = dot(offset, before), across = before.x * offset.y - before.y * offset.x;
        return add(sample(c.current, projected.u), { x: after.x * along - after.y * across, y: after.y * along + after.x * across });
    }
    function warp(p, name) {
        if (curves[name]) return warpCurve(p, name);
        if (name === 'root') return p;
        if (name === 'pen') {
            const base = initial[at('hand_b').data.index], current = warpCurve(base, 'arm_b');
            const a = tangent(curves.arm_b.target, 1), b = tangent(curves.arm_b.current, 1);
            const v = sub(p, base), along = dot(v, a), across = a.x * v.y - a.y * v.x;
            return add(current, { x: b.x * along - b.y * across, y: b.y * along + b.x * across });
        }
        return warpRigid(p, name, primary[name]);
    }
    function turn(p, name) {
        if (curves[name] || name === 'pen') {
            const c = curves[name === 'pen' ? 'arm_b' : name];
            const u = name === 'pen' ? 1 : project(c.target, p).u;
            const a = tangent(c.target, u), b = tangent(c.current, u);
            return Math.atan2(b.y, b.x) - Math.atan2(a.y, a.x);
        }
        if (name === 'root') return 0;
        const a = sourceInitial.get(primary[name]), b = src(primary[name]);
        return Math.atan2(b.c, b.a) - Math.atan2(a.c, a.a);
    }
    const frames = [], measurements = [];
    const count = Math.round(duration * 120) + 1;
    for (let i = 0; i < count; i++) {
        const t = duration * i / (count - 1);
        if (i > 0) {
            const dt = t - frames.at(-1).t;
            source.setToSetupPose();
            die.apply(source, frames.at(-1).t, t, false, [], 1, MixBlend.replace, MixDirection.mixIn);
            source.update(dt);
            source.updateWorldTransform(Physics.update);
        }
        // Preserve Architect's initial proportions, releasing pose offsets as the
        // source loses height. Timing comes from the source hip trajectory.
        retention = clamp((src('cog').worldY - settledHipHeight) / (hipHeight - settledHipHeight));
        for (const [name, c] of Object.entries(curves)) {
            const current = sourceCurves[name](), start = current.points[0];
            const oldStart = c.native.points[0];
            const targetStart = c.target.points[0];
            const root = anchor({ ...start, initial: oldStart }, targetStart);
            c.current = curve(Array.from({ length: 33 }, (_, j) => {
                const u = j / 32;
                const offset = sub(sample(c.target, u), add(targetStart, mul(sub(sample(c.native, u), oldStart), c.scale)));
                return add(add(root, mul(sub(sample(current, u), start), c.scale)), mul(offset, retention));
            }));
        }
        skeleton.slots.forEach((slot, j) => slot.deform = [...initialDeforms[j]]);
        for (const bone of skeleton.bones) {
            const start = initial[bone.data.index], name = group(bone);
            const p = warp(start, name);
            const angle = turn(start, name), cos = Math.cos(angle), sin = Math.sin(angle);
            let x = { x: cos * start.a - sin * start.c, y: sin * start.a + cos * start.c };
            let y = { x: cos * start.b - sin * start.d, y: sin * start.b + cos * start.d };
            if (primary[name]) {
                const old = sourceInitial.get(primary[name]), current = src(primary[name]);
                x = vector(current, inverseVector(old, { x: start.a, y: start.c }));
                y = vector(current, inverseVector(old, { x: start.b, y: start.d }));
            }
            Object.assign(bone, { worldX: p.x, worldY: p.y, a: x.x, c: x.y, b: y.x, d: y.y });
            bone.updateAppliedTransform();
            Object.assign(bone, { x: bone.ax, y: bone.ay, rotation: bone.arotation,
                scaleX: bone.ascaleX, scaleY: bone.ascaleY, shearX: bone.ashearX, shearY: bone.ashearY });
        }
        skeleton.updateWorldTransform(Physics.none);
        const deforms = meshes.map(entry => {
            const slot = skeleton.slots[entry.slotIndex], a = entry.attachment;
            const start = initialVertices.get(entry.slotIndex), name = group(slot.bone);
            const target = start.map(p => {
                const q = warp(p, /cape|sash/u.test(slot.data.name) ? 'drape' : name);
                // Contact correction is per vertex, never an upward translation
                // of the whole character that could undo the source collapse.
                if (q.y < 0 && !/pen/u.test(slot.data.name)) return { x: q.x - q.y * .25, y: 0 };
                return q;
            });
            const result = [];
            if (!a.bones) {
                for (const p of target) { const local = slot.bone.worldToLocal({ ...p }); result.push(local.x, local.y); }
            } else {
                let influence = 0, vertex = 0;
                for (const p of target) {
                    const count = a.bones[influence++];
                    for (let j = 0; j < count; j++, vertex += 3) {
                        const bone = skeleton.bones[a.bones[influence++]];
                        const local = bone.worldToLocal({ ...p });
                        result.push(local.x - a.vertices[vertex], local.y - a.vertices[vertex + 1]);
                    }
                }
            }
            slot.deform = result;
            return result;
        });
        const bones = skeleton.bones.map(b => ({ x: b.x - b.data.x, y: b.y - b.data.y,
            rotation: b.rotation - b.data.rotation, scaleX: b.scaleX / b.data.scaleX,
            scaleY: b.scaleY / b.data.scaleY, shearX: b.shearX - b.data.shearX, shearY: b.shearY - b.data.shearY }));
        if (i > 0) for (let j = 0; j < bones.length; j++) for (const key of ['rotation', 'shearX', 'shearY']) {
            const previous = frames.at(-1).bones[j][key];
            while (bones[j][key] - previous > 180) bones[j][key] -= 360;
            while (bones[j][key] - previous < -180) bones[j][key] += 360;
        }
        frames.push({ t, bones, deforms });
        if (i % 20 === 0 || i === count - 1)
            measurements.push({ time: t, sourceHip: point(src('cog')), sourceHead: point(src('head_base')),
                targetHip: point(at('cog')), targetHead: point(at('head')) });
    }
    return { frames, meshes, duration, measurements, sizeRatio,
        mapping: { ...primary, ...targetNames }, sourcePhysicsConstraints: source.physicsConstraints.length,
        sourcePathConstraints: source.pathConstraints.length };
}
