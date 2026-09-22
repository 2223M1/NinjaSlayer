import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const directory = resolve(process.argv[2] ?? '');
const read = name => JSON.parse(readFileSync(join(directory, name), 'utf8').replace(/^\uFEFF/, ''));
const runtime = read('runtime.json');
const script = read('script.json');
const timeline = read('timeline.json');
const damage = read('damage.json');
const coverage = read('coverage.json');
const motion = read('motion.json');
const sync = read('audio-sync.json');
const frames = readFileSync(join(directory, 'video-frames.csv'), 'utf8').trim().split(/\r?\n/)
  .slice(1).map(line => line.split(',').map(Number));
const stop = read('recording-stop.json');
const media = JSON.parse(execFileSync('ffprobe', ['-v', 'error', '-show_streams', '-show_format',
  '-of', 'json', join(directory, 'theater.mp4')], { encoding: 'utf8' }));
const video = media.streams.find(stream => stream.codec_type === 'video');
assert.equal(video.width, 1920);
assert.equal(video.height, 1080);
assert.equal(video.r_frame_rate, '60/1');
assert(media.streams.some(stream => stream.codec_type === 'audio'), 'Missing synchronized audio.');
assert.equal(runtime.act, 3);
if ((script.purpose ?? 'promo') === 'promo') assert.equal(runtime.mode, 'Fast');
assert(sync.maximumResidualSeconds <= .04, 'Audio alignment exceeds 40ms.');
assert(sync.matches.filter(match => match.accepted).length >= 2, 'Too few measured audio cues.');

const full = runtime.fromCue === script.cues[0].id && runtime.toCue === script.cues.at(-1).id;
const count = name => coverage[name]?.filter(time => time >= runtime.captureStartSeconds).length ?? 0;
if (full && script.purpose === 'blood') {
  assert.equal(runtime.mode, 'Normal');
  for (const name of ['threshold-block-self-dot-dodge-single-instance', 'moving', 'semi',
    'hell', 'full', 'soul', 'mirror', 'pause-fast-instant-death-cleanup'])
    assert(count('blood-' + name) > 0, `Missing completed blood check: ${name}`);
}
if (full && (script.purpose ?? 'promo') === 'promo') {
  assert(count('backflip') >= 4);
  assert(count('shuriken-volley') >= 4);
  assert.equal(count('knife-round-trip'), 3);
  assert.equal(count('dark-iai-counter'), 2);
  assert.equal(count('friendly-fire'), 1);
  assert.equal(timeline[0].before.actors.sawatari.maxHp, 280);
  assert.equal(runtime.state.actors.dark.maxHp, 180);
  assert(!script.relics.includes('BigMushroom'));
  const hellEnd = timeline.find(cue => cue.id === 'hell_tornado').end;
  assert(coverage['backflip'].every(time => time <= hellEnd));
  assert(coverage['shuriken-volley'].every(time => time <= hellEnd));
  for (const name of ['form-normal', 'form-semi', 'form-full', 'form-soul', 'hell-tornado',
    'fast-attack', 'slow-attack', 'somersault-heavy', 'kick-overhead', 'kick-bs1260',
    'kick-roundhouse', 'kick-sweep', 'kick-flying', 'alabama-drop', 'tornado-normal',
    'tornado-empowered-b', 'koki-iai', 'apology-exit', 'companion-relocation',
    'dark-strike-entrance', 'dark-strike-combat', 'sawatari-kicked-offscreen', 'anti-air-combo', 'tomoe-throw'])
    assert(count(name) > 0, `Missing completed action: ${name}`);
  if (!runtime.rehearsal) {
    assert.equal(runtime.state.actors.dark.hp, 0, 'Final enemy survived.');
    assert(runtime.state.actors.ninja.hp > 0, 'Player died.');
    assert(damage.some(hit => hit.actor === 'dark' && hit.after === 0 && hit.cue === 'final_tornado'),
      'Enemy did not die in the final Tornado.');
  }
}

const stabs = motion.filter(row => row.stab?.bladeVisible && !row.stab.fullBodyVisible);
if (stabs.length) {
  assert(stabs.every(row => row.stab.scaleX > 0), 'Sawatari stab was not mirrored.');
  assert(stabs.every(row => row.stabTarget.z > row.stab.z && row.stabTarget.z < row.stab.bladeZ),
    'Target must be above attacker and below blade.');
  const impact = damage.find(hit => hit.cue === 'dark_strike_closeup' && hit.actor === 'sawatari'
    && hit.after > 0 && hit.after < hit.before);
  assert(impact, 'Missing real stab damage.');
  assert.equal(impact.after, 1, 'Dark Strike did not leave Sawatari at one HP.');
  assert(damage.some(hit => hit.actor === 'sawatari' && hit.before === 1 && hit.after === 0),
    'The kick did not remove the last HP.');
  const hold = stabs.filter(row => row.seconds >= impact.seconds);
  assert(hold.length >= 2, 'Missing impact hold frames.');
  const first = hold[0].stabTarget;
  assert(hold.every(row => Math.abs(row.stabTarget.rotation - first.rotation) < .001
    && Math.hypot(row.stabTarget.x - first.x, row.stabTarget.y - first.y) < .5
    && Math.hypot(row.sawatari.coreX - hold[0].sawatari.coreX,
      row.sawatari.coreY - hold[0].sawatari.coreY) < .5),
  'Sawatari recoiled before the stab hold finished.');
}

let comparison;
if (process.argv[3]) {
  const reference = JSON.parse(readFileSync(join(resolve(process.argv[3]), 'timeline.json'), 'utf8'));
  comparison = [];
  const start = timeline.findIndex(cue => cue.id === runtime.fromCue);
  for (const cue of timeline.slice(start)) {
    const previous = reference.find(row => row.id === cue.id);
    assert(previous, `Reference is missing ${cue.id}.`);
    assert.deepEqual(cue.before, previous.before, `Initial state changed at ${cue.id}.`);
    assert.deepEqual(cue.after, previous.after, `Final state changed at ${cue.id}.`);
    comparison.push(cue.id);
  }
}
const intervals = motion.slice(1).map((row, i) => row.seconds - motion[i].seconds).sort((a, b) => a - b);
const report = {
  duration: Number(media.format.duration), resolution: '1920x1080', encodedFps: 60,
  capturedFrames: frames.length, encodedFrames: stop.frames,
  repeatedFramePercent: 100 * (stop.frames - frames.length) / stop.frames,
  renderFps: (motion.length - 1) / (motion.at(-1).seconds - motion[0].seconds),
  renderFrameP95Ms: 1000 * intervals[Math.floor(intervals.length * .95)],
  audioResidualMs: sync.maximumResidualSeconds * 1000,
  audioMatchedCues: sync.matches.filter(match => match.accepted).length,
  backflips: count('backflip'), volleys: count('shuriken-volley'), knifeRounds: count('knife-round-trip'),
  counters: count('dark-iai-counter'), misfires: count('friendly-fire'),
  mirroredStabFrames: stabs.length, comparedCues: comparison ?? [], full, rehearsal: runtime.rehearsal
};
writeFileSync(join(directory, 'verification.json'), JSON.stringify(report, null, 2) + '\n');
const time = seconds => (seconds - runtime.captureStartSeconds).toFixed(3);
const rows = timeline.filter(cue => cue.end >= runtime.captureStartSeconds);
writeFileSync(join(directory, 'timeline.md'), '# Recorded Timeline\n\nSeconds are relative to this video.\n\n'
  + '| Cue | Start | End |\n| --- | ---: | ---: |\n'
  + rows.map(cue => `| ${cue.id} | ${time(cue.start)} | ${time(cue.end)} |`).join('\n') + '\n');
writeFileSync(join(directory, 'coverage.md'), '# Completed Actions\n\n'
  + '| Action | Times (video seconds) |\n| --- | --- |\n'
  + Object.entries(coverage).sort(([a], [b]) => a.localeCompare(b))
    .map(([name, times]) => [name, times.filter(value => value >= runtime.captureStartSeconds)])
    .filter(([, times]) => times.length > 0)
    .map(([name, times]) => `| ${name} | ${times.map(time).join(', ')} |`).join('\n') + '\n');
console.log(JSON.stringify(report, null, 2));
