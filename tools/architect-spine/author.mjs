import fs from 'node:fs';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { SkeletonBinary, TextureAtlas, AtlasAttachmentLoader, Skeleton, Physics, MixBlend, MixDirection, MeshAttachment, RegionAttachment } from '@esotericsoftware/spine-core';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const pack = process.argv[2] ?? 'C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck';
const output = path.join(root, 'NinjaSlayer/animations/architect');
const name = 'ninjaslayer_soft_death';
const sha = bytes => createHash('sha256').update(bytes).digest('hex');

function readArchitect() {
    const handle = fs.openSync(pack, 'r');
    let position = 0;
    const bytes = length => { const b = Buffer.alloc(length); if (fs.readSync(handle, b, 0, length, position) !== length) throw Error('Truncated PCK'); position += length; return b; };
    const u32 = () => bytes(4).readUInt32LE();
    const u64 = () => Number(bytes(8).readBigUInt64LE());
    const found = {};
    try {
        if (u32() !== 0x43504447) throw Error('Not a Godot PCK');
        const version = u32(); position = 24;
        const base = u64();
        if (version === 3) position = u64(); else if (version === 2) position += 64; else throw Error('Unsupported PCK');
        const count = u32();
        for (let i = 0; i < count; i++) {
            const entry = bytes(u32()).toString('utf8').replace(/\0+$/, '');
            const offset = u64(), size = u64(); bytes(16);
            const flags = u32();
            const key = entry.match(/^\.godot\/imported\/architect\.(skel|atlas)-/u)?.[1];
            if (!key) continue;
            if (flags !== 0) throw Error('Encrypted/removed Architect resource');
            const resume = position; position = base + offset;
            found[key] = { entry, bytes: bytes(size) }; position = resume;
        }
    } finally { fs.closeSync(handle); }
    if (!found.skel || !found.atlas) throw Error('Architect resources are missing');
    return found;
}

const source = readArchitect();
const atlas = new TextureAtlas(JSON.parse(source.atlas.bytes.toString('utf8')).atlas_data);
class RecordingReader extends SkeletonBinary {
    starts = [];
    readAnimation(input, animationName, data) {
        this.strings = input.strings;
        this.starts.push({ name: animationName, start: input.index });
        const animation = super.readAnimation(input, animationName, data);
        this.end = input.index;
        return animation;
    }
}
const reader = new RecordingReader(new AtlasAttachmentLoader(atlas));
const data = reader.readSkeletonData(source.skel.bytes);
if (data.version !== '4.2.43' || reader.end !== source.skel.bytes.length || data.findAnimation(name)) throw Error('Unexpected source skeleton');
const skeleton = new Skeleton(data);
data.findAnimation('hurt').apply(skeleton, 0, 0.1, false, [], 1, MixBlend.replace, MixDirection.mixIn);
skeleton.updateWorldTransform(Physics.none);
// Bake the constrained impact pose once, then animate the same bones with IK and
// transform-constraint mixes at zero. No runtime bone solver is involved.
for (const bone of skeleton.bones) bone.updateAppliedTransform();
const initial = skeleton.bones.map(b => ({ x:b.ax, y:b.ay, rotation:b.arotation, scaleX:b.ascaleX, scaleY:b.ascaleY, shearX:b.ashearX, shearY:b.ashearY }));
for (const ik of skeleton.ikConstraints) ik.mix = 0;
for (const c of skeleton.transformConstraints) c.mixRotate = c.mixX = c.mixY = c.mixScaleX = c.mixScaleY = c.mixShearY = 0;
for (const c of skeleton.physicsConstraints) c.mix = 0;
const smooth = (time, from, to) => { const p = Math.max(0,Math.min(1,(time-from)/(to-from))); return p*p*(3-2*p); };
const bend = [
    ['body_lower', -68, .08, .52], ['body_upper', -30, .17, .60],
    ['neck', -18, .25, .66], ['head', -32, .31, .70],
    ['shoulder_b', 22, .13, .55], ['arm_upper_b', 42, .20, .61], ['arm_lower_b', 35, .31, .69], ['hand_b', -18, .43, .75],
    ['shoulder front', -12, .14, .52], ['arm_upper_f', -52, .23, .62], ['arm_lower_f', -34, .34, .70], ['hand_f', 22, .42, .76],
    ['leg_upper_f', 14, .02, .49], ['cog4', 62, .11, .58], ['foot_f1', 38, .29, .67],
    ['leg_upper_b', -46, .07, .53], ['leg_lower_b', 104, .19, .62], ['foot1_b', 43, .37, .71],
    ['hair_top', 16, .29, .69], ['hair_bottom', -24, .37, .76],
];
const frames = [];
const cloth = data.defaultSkin.getAttachments().filter(entry =>
    /sleeve|cape|sash/u.test(data.slots[entry.slotIndex].name)
    && entry.attachment instanceof MeshAttachment
    && skeleton.slots[entry.slotIndex].attachment === entry.attachment);
const initialDeforms = skeleton.slots.map(slot => [...slot.deform]);
const worldPoint = bone => ({x:bone.worldX,y:bone.worldY});
const initialCape = new Map(skeleton.bones.filter(b => /^cape [bf]\d/u.test(b.data.name)).map(b => [b.data.name,worldPoint(b)]));
function worldMove(bone,x,y) {
    const local = bone.parent.worldToLocal({x,y}); bone.x=local.x; bone.y=local.y;
}
let lowestSlot;
function lowest(excludeCloth = false) {
    let low = Infinity;
    for (const slot of skeleton.drawOrder) {
        if (/shadow|pen|flame|mask/u.test(slot.data.name) || slot.color.a === 0) continue;
        if (excludeCloth && /sleeve|cape|sash/u.test(slot.data.name)) continue;
        const a = slot.attachment;
        if (a instanceof MeshAttachment) {
            const v = new Float32Array(a.worldVerticesLength); a.computeWorldVertices(slot,0,v.length,v,0,2);
            for(let i=1;i<v.length;i+=2) if(v[i]<low) { low=v[i];lowestSlot=slot.data.name; }
        } else if (a instanceof RegionAttachment) {
            const v=new Float32Array(8); a.computeWorldVertices(slot,v,0,2);
            for(let i=1;i<8;i+=2) if(v[i]<low) { low=v[i];lowestSlot=slot.data.name; }
        }
    }
    return low;
}
function foldCloth(entry, time) {
    const slot = skeleton.slots[entry.slotIndex], a = entry.attachment;
    const vertices = new Float32Array(a.worldVerticesLength);
    a.computeWorldVertices(slot, 0, vertices.length, vertices, 0, 2);
    const deform = a.bones ? Array(a.vertices.length / 3 * 2).fill(0) : [...a.vertices];
    const establish = smooth(time, .10, .35);
    let influence = 0, deformIndex = 0;
    for (let i = 0; i < vertices.length; i += 2) {
        const y = vertices[i + 1], depth = 12 - y;
        // The long sleeves fold and spread on contact instead of supporting the
        // entire torso. Store these offsets as native weighted mesh keyframes.
        const contact = (depth + Math.sqrt(depth * depth + 16)) * .5 * establish;
        const dx = contact * .35, dy = contact + 30 * (1 - Math.exp(-contact / 100));
        const localOffset = bone => {
            const det = bone.a * bone.d - bone.b * bone.c;
            return [(bone.d * dx - bone.b * dy) / det, (bone.a * dy - bone.c * dx) / det];
        };
        if (a.bones) {
            const end = influence + 1 + a.bones[influence];
            for (influence++; influence < end; influence++, deformIndex += 2) {
                const [x, y] = localOffset(skeleton.bones[a.bones[influence]]);
                deform[deformIndex] = (initialDeforms[entry.slotIndex][deformIndex] ?? 0) + x;
                deform[deformIndex + 1] = (initialDeforms[entry.slotIndex][deformIndex + 1] ?? 0) + y;
            }
        } else {
            const [x, y] = localOffset(slot.bone);
            deform[i] = (initialDeforms[entry.slotIndex][i] ?? a.vertices[i]) + x;
            deform[i + 1] = (initialDeforms[entry.slotIndex][i + 1] ?? a.vertices[i + 1]) + y;
        }
    }
    slot.deform = deform;
    return deform;
}
const count = 61;
for (let i=0;i<count;i++) {
    const t=.9*i/(count-1);
    skeleton.bones.forEach((b,j)=>Object.assign(b,initial[j]));
    skeleton.slots.forEach((slot,j)=>slot.deform=[...initialDeforms[j]]);
    for (const [boneName,angle,from,to] of bend) {
        const b=skeleton.findBone(boneName);
        b.rotation += angle*smooth(t,from,to);
    }
    const cog=skeleton.findBone('cog');
    cog.x += 210*smooth(t,.05,.7);
    cog.y -= 620*smooth(t,0,.65);
    for(const b of skeleton.bones) {
        if (/^(sleeve_|sash_)/u.test(b.data.name)) {
            const index=Number(b.data.name.match(/\d/u)?.[0]??1);
            b.rotation += (-10+index*6)*smooth(t,.16+index*.035,.59+index*.03);
        }
    }
    skeleton.updateWorldTransform(Physics.none);
    // Existing weighted cape control bones settle progressively onto the floor.
    // This authors smooth mesh folding while leaving the weighted mesh unchanged.
    for(const [boneName,start] of initialCape) {
        const b=skeleton.findBone(boneName), rank=Number(boneName.match(/\d/u)[0]);
        const p=smooth(t,.16+rank*.025,.57+rank*.025);
        worldMove(b, start.x+(120+rank*38)*p, start.y+(38+rank*8-start.y)*p);
    }
    const pen=skeleton.findBone('pen');
    pen.x += 130*smooth(t,.13,.72);
    pen.y -= 660*smooth(t,.11,.7);
    pen.rotation -= 55*smooth(t,.17,.74);
    skeleton.updateWorldTransform(Physics.none);
    const floorCorrection=Math.max(0,-lowest(true));
    skeleton.findBone('root_adjust').y += floorCorrection;
    skeleton.updateWorldTransform(Physics.none);
    const deforms = cloth.map(entry => foldCloth(entry, t));
    frames.push({t,deforms,bones:skeleton.bones.map(b=>({x:b.x-b.data.x,y:b.y-b.data.y,rotation:b.rotation-b.data.rotation,scaleX:b.scaleX/b.data.scaleX,scaleY:b.scaleY/b.data.scaleY,shearX:b.shearX-b.data.shearX,shearY:b.shearY-b.data.shearY}))});
}

const chunks=[];
const byte=v=>chunks.push(Buffer.from([v]));
const vint=v=>{ do { const n=v&127; v>>>=7; byte(n|(v?128:0)); } while(v); };
const float=v=>{const b=Buffer.alloc(4);b.writeFloatBE(v);chunks.push(b);};
const string=v=>{const b=Buffer.from(v);vint(b.length+1);chunks.push(b);};
function timeline(type,index,keys) {
    byte(type); vint(frames.length); vint((frames.length-1)*keys.length);
    const writeFrame=i=>{float(frames[i].t);for(const k of keys)float(frames[i].bones[index][k]);};
    writeFrame(0);
    for(let f=1;f<frames.length;f++) {
        writeFrame(f); byte(2);
        const left=frames[f-1],right=frames[f],dt=right.t-left.t;
        for(const k of keys) {
            const a=left.bones[index][k],b=right.bones[index][k];
            const tangent = frame => {
                if (frame === 0 || frame === frames.length - 1) return 0;
                const before = (frames[frame].bones[index][k] - frames[frame - 1].bones[index][k]) / dt;
                const after = (frames[frame + 1].bones[index][k] - frames[frame].bones[index][k]) / dt;
                return before * after <= 0 ? 0 : 2 * before * after / (before + after);
            };
            // Continuous monotone tangents avoid both contact overshoot and a
            // small stop at every baked key when rendering above 60 fps.
            float(left.t+dt/3);float(a+tangent(f-1)*dt/3);
            float(right.t-dt/3);float(b-tangent(f)*dt/3);
        }
    }
}
string(name);
vint(data.bones.length*4+data.ikConstraints.length+data.transformConstraints.length+data.physicsConstraints.length+cloth.length);
vint(0); // slots
vint(data.bones.length);
for(let i=0;i<data.bones.length;i++) { vint(i);vint(4);timeline(0,i,['rotation']);timeline(1,i,['x','y']);timeline(4,i,['scaleX','scaleY']);timeline(7,i,['shearX','shearY']); }
vint(data.ikConstraints.length);
for(let i=0;i<data.ikConstraints.length;i++) {vint(i);vint(1);vint(0);byte(0);float(0);}
vint(data.transformConstraints.length);
for(let i=0;i<data.transformConstraints.length;i++) {vint(i);vint(1);vint(0);for(let j=0;j<7;j++)float(0);}
vint(0); // path
vint(data.physicsConstraints.length);
for(let i=0;i<data.physicsConstraints.length;i++) {vint(i+1);vint(1);byte(7);vint(1);vint(0);float(0);float(0);}
vint(1);vint(data.skins.indexOf(data.defaultSkin));vint(cloth.length);
for (let c=0;c<cloth.length;c++) {
    const entry=cloth[c], ref=reader.strings.indexOf(entry.name)+1;
    if (!ref) throw Error(`Missing attachment string: ${entry.name}`);
    vint(entry.slotIndex);vint(1);vint(ref);byte(0);vint(frames.length);vint(0);
    float(frames[0].t);
    for(let f=0;f<frames.length;f++) {
        const deform=frames[f].deforms[c];vint(deform.length);vint(0);
        deform.forEach((v,i)=>float(entry.attachment.bones ? v : v-entry.attachment.vertices[i]));
        if(f+1<frames.length) {float(frames[f+1].t);byte(0);}
    }
}
vint(0);vint(0); // draw order, events
const animation=Buffer.concat(chunks);
// Original source is byte-for-byte intact except its one-byte animation count.
const first=reader.starts[0];
const countOffset=first.start-Buffer.byteLength(first.name)-2;
if(data.animations.length>=127 || source.skel.bytes[countOffset]!==data.animations.length) throw Error('Animation directory mismatch');
const result=Buffer.concat([source.skel.bytes,animation]);result[countOffset]++;
const verified=new SkeletonBinary(new AtlasAttachmentLoader(atlas)).readSkeletonData(result);
if(verified.animations.length!==data.animations.length+1 || Math.abs(verified.findAnimation(name).duration-.9)>1e-5) throw Error('Export validation failed');
const replay = new Skeleton(verified);
for (const frame of frames) {
    replay.setToSetupPose();
    verified.findAnimation(name).apply(replay, 0, frame.t, false, [], 1, MixBlend.replace, MixDirection.mixIn);
    replay.updateWorldTransform(Physics.none);
    cloth.forEach((entry, c) => {
        const actual = replay.slots[entry.slotIndex].deform, expected = frame.deforms[c];
        if (actual.length !== expected.length || expected.some((v, i) => Math.abs(v - actual[i]) > .01))
            throw Error(`Cloth export mismatch: ${entry.name} at ${frame.t}`);
    });
}
if (replay.findBone('head').worldY > 230 || replay.findBone('body_lower').worldY > 250)
    throw Error('Architect torso is not settled near the floor');
fs.mkdirSync(output,{recursive:true});
fs.writeFileSync(path.join(output,'architect.spskel'),result);
fs.writeFileSync(path.join(output,'SOURCE.json'),JSON.stringify({spineVersion:data.version,runtimePackage:'@esotericsoftware/spine-core@4.2.43',source:source.skel.entry,sourceSha256:sha(source.skel.bytes),sourceBytes:source.skel.bytes.length,animationCountOffset:countOffset,originalAnimations:data.animations.map(a=>a.name),addedAnimation:name,duration:.9,outputSha256:sha(result)},null,2)+'\n');
console.log(`Architect Spine: ${data.bones.length} bones, ${data.animations.length} original animations preserved; ${name} 0.9s; ${result.length} bytes.`);
if (process.argv.includes('--inspect')) {
    console.log(`Ground support slot: ${lowestSlot}`);
    for (const slot of skeleton.slots.filter(s => /sleeve|cape|sash/u.test(s.data.name)))
        console.log(JSON.stringify({slot:slot.data.name,attachment:slot.attachment?.name,type:slot.attachment?.constructor.name,weighted:!!slot.attachment?.bones,bone:slot.bone.data.name}));
    for (const bone of skeleton.bones.filter(b => /^(cog|root|body_|leg_|foot|head|cape)/u.test(b.data.name)))
        console.log(JSON.stringify({ name:bone.data.name,parent:bone.parent?.data.name,x:bone.worldX,y:bone.worldY,rotation:bone.rotation,start:initial[bone.data.index] }));
}
