import fs from 'node:fs';
import { SkeletonBinary, TextureAtlas, AtlasAttachmentLoader, RegionAttachment } from '@esotericsoftware/spine-core';

export function readNative(pack) {
    const handle = fs.openSync(pack, 'r');
    let position = 0;
    const bytes = length => {
        const b = Buffer.alloc(length);
        if (fs.readSync(handle, b, 0, length, position) !== length) throw Error('Truncated PCK');
        position += length;
        return b;
    };
    const u32 = () => bytes(4).readUInt32LE();
    const u64 = () => Number(bytes(8).readBigUInt64LE());
    const found = {};
    try {
        if (u32() !== 0x43504447) throw Error('Not a Godot PCK');
        const version = u32(); position = 24;
        const base = u64();
        if (version === 3) position = u64();
        else if (version === 2) position += 64;
        else throw Error('Unsupported PCK');
        const count = u32();
        for (let i = 0; i < count; i++) {
            const entry = bytes(u32()).toString('utf8').replace(/\0+$/, '');
            const offset = u64(), size = u64(); bytes(16);
            const flags = u32();
            const key = entry.match(/^\.godot\/imported\/(architect|skulking_colony)\.(skel|atlas)-/u);
            if (!key) continue;
            if (flags !== 0) throw Error('Encrypted/removed Spine resource');
            const resume = position; position = base + offset;
            (found[key[1]] ??= {})[key[2]] = { entry, bytes: bytes(size) };
            position = resume;
        }
    } finally { fs.closeSync(handle); }
    for (const name of ['architect', 'skulking_colony'])
        if (!found[name]?.skel || !found[name]?.atlas) throw Error(`Missing ${name} resources`);
    return found;
}

export class RecordingReader extends SkeletonBinary {
    starts = [];
    attachments = [];
    readAttachment(input, data, skin, slotIndex, name, nonessential) {
        const start = input.index;
        const attachment = super.readAttachment(input, data, skin, slotIndex, name, nonessential);
        this.attachments.push({ start, end: input.index, skin, slotIndex, name, nonessential, attachment });
        return attachment;
    }
    readAnimation(input, name, data) {
        this.strings = input.strings;
        this.starts.push({ name, start: input.index });
        const animation = super.readAnimation(input, name, data);
        this.end = input.index;
        return animation;
    }
}

export function parseNative(source, bytes = source.skel.bytes) {
    const atlas = new TextureAtlas(JSON.parse(source.atlas.bytes.toString('utf8')).atlas_data);
    const reader = new RecordingReader(new AtlasAttachmentLoader(atlas));
    const data = reader.readSkeletonData(bytes);
    if (data.version !== '4.2.43' || reader.end !== bytes.length) throw Error('Unexpected Spine format');
    return { data, reader, atlas };
}

export class BinaryWriter {
    chunks = [];
    byte(v) { this.chunks.push(Buffer.from([v])); }
    int(v) { do { const n = v & 127; v >>>= 7; this.byte(n | (v ? 128 : 0)); } while (v); }
    float(v) { if (!Number.isFinite(v)) throw Error('Non-finite Spine value'); const b = Buffer.alloc(4); b.writeFloatBE(v); this.chunks.push(b); }
    string(v) { const b = Buffer.from(v); this.int(b.length + 1); this.chunks.push(b); }
    bytes() { return Buffer.concat(this.chunks); }
}

// Subdivide the existing textured quads without moving a pixel or changing UVs.
// Only the private death track deforms these meshes; all other tracks stay intact.
export function subdivideLimbs(source) {
    const { data, reader } = parseNative(source);
    const replacements = [];
    for (const record of reader.attachments) {
        const a = record.attachment;
        const slot = data.slots[record.slotIndex];
        if (!(a instanceof RegionAttachment) || !/leg|foot|knee|shin|r_arm|r_lower_arm/u.test(slot.name)) continue;
        if (a.sequence) throw Error(`Animated region cannot be subdivided: ${slot.name}`);
        const nx = 4, ny = 12;
        const points = [], indices = new Map();
        for (const boundary of [true, false])
            for (let y = 0; y <= ny; y++) for (let x = 0; x <= nx; x++) {
                if ((x === 0 || y === 0 || x === nx || y === ny) !== boundary) continue;
                indices.set(`${x},${y}`, points.length);
                points.push([x / nx, y / ny]);
            }
        const w = new BinaryWriter();
        w.byte(2 | 16 | 32 | (a.name !== record.name ? 8 : 0));
        if (a.name !== record.name) w.int(reader.strings.indexOf(a.name) + 1);
        const pathIndex = reader.strings.indexOf(a.path) + 1;
        if (!pathIndex) throw Error(`Missing atlas path: ${a.path}`);
        w.int(pathIndex);
        for (const channel of ['r', 'g', 'b', 'a']) w.byte(Math.round(a.color[channel] * 255));
        w.int(2 * (nx + ny));
        w.int(points.length);
        for (const [u, v] of points) {
            w.float(a.offset[0] + u * (a.offset[6] - a.offset[0]) + v * (a.offset[2] - a.offset[0]));
            w.float(a.offset[1] + u * (a.offset[7] - a.offset[1]) + v * (a.offset[3] - a.offset[1]));
        }
        for (const [u, v] of points) {
            w.float((a.region.offsetX + u * a.region.width) / a.region.originalWidth);
            w.float(1 - (a.region.offsetY + v * a.region.height) / a.region.originalHeight);
        }
        for (let y = 0; y < ny; y++) for (let x = 0; x < nx; x++) {
            const [bl, br, tl, tr] = [[x, y], [x + 1, y], [x, y + 1], [x + 1, y + 1]]
                .map(([a, b]) => indices.get(`${a},${b}`));
            for (const index of [bl, br, tl, br, tr, tl]) w.int(index);
        }
        if (record.nonessential) { w.int(0); w.float(a.width); w.float(a.height); }
        replacements.push({ ...record, bytes: w.bytes(), points });
    }
    let offset = 0;
    const chunks = [];
    for (const r of replacements) {
        chunks.push(source.skel.bytes.subarray(offset, r.start), r.bytes);
        offset = r.end;
    }
    chunks.push(source.skel.bytes.subarray(offset));
    const bytes = Buffer.concat(chunks);
    const converted = parseNative(source, bytes);
    for (const r of replacements) {
        const mesh = converted.data.skins[data.skins.indexOf(r.skin)].getAttachment(r.slotIndex, r.name);
        r.points.forEach(([u, v], i) => {
            const a = r.attachment;
            for (let axis = 0; axis < 2; axis++) {
                const expected = a.uvs[axis] + u * (a.uvs[6 + axis] - a.uvs[axis]) + v * (a.uvs[2 + axis] - a.uvs[axis]);
                if (Math.abs(mesh.uvs[i * 2 + axis] - expected) > 1e-6) throw Error(`UV mismatch: ${r.name}`);
            }
        });
    }
    return { ...converted, bytes, subdivided: replacements.map(r => data.slots[r.slotIndex].name) };
}
