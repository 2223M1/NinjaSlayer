// Executed by the companion Python migration through the running Studio instance.
function required(value, name) {
    if (!value) throw new Error('Missing ' + name);
    return value;
}
function folder(path) {
    var existing = studio.project.lookup('event:/' + path);
    if (existing) return existing;
    var parts = path.split('/');
    var name = parts.pop();
    var parent = parts.length ? folder(parts.join('/')) : studio.project.workspace.masterEventFolder;
    var created = studio.project.create('EventFolder');
    created.name = name;
    created.folder = parent;
    return created;
}
var event = required(studio.project.lookup('{09550113-0b1f-430c-9f3e-d8be1fc650ea}'), 'spin intro');
if (event.name !== 'ninja_slayer_intro_spin_attack') throw new Error('Migration already applied');
var introSeconds = 1.3699773242630386;
var loopSeconds = 1.18421768707483;
var outroSeconds = 1.1145578231292517;
var outroStart = introSeconds + loopSeconds;
var originalGain = event.mixer.masterBus.volume;
['{a9bee68e-cb5d-427e-8248-e8a063343b0e}', '{191e4ae4-25f9-41f7-9571-639313302897}'].forEach(function(id) {
    if (Math.abs(studio.project.lookup(id).mixer.masterBus.volume - originalGain) > 0.0001)
        throw new Error('Spin stages no longer have equal gain');
});
migration.forEach(function(row) {
    var item = required(studio.project.lookup(row.guid), row.before);
    if (!row.after) return;
    var parts = row.after.replace('event:/', '').split('/');
    item.name = parts.pop();
    item.folder = folder(parts.join('/'));
});
// The old intro was an asynchronous multi-instrument; all stages now share a timeline.
event.groupTracks.slice().forEach(function(track) { studio.project.deleteObject(track); });
event.masterTrack.modules.slice().forEach(function(module) { studio.project.deleteObject(module); });
event.timeline.isProxyEnabled = true;
var track = event.addGroupTrack('Spin');
function sound(asset, position, seconds) {
    var s = track.addSound(event.timeline, 'SingleSound', position, seconds);
    s.audioFile = required(studio.project.workspace.masterAssetFolder.getAsset(asset), asset);
    return s;
}
sound('ninja_slayer_spin_intro.wav', 0, introSeconds);
sound('ninja_slayer_spin_loop.wav', introSeconds, loopSeconds);
sound('ninja_slayer_spin_outro.wav', outroStart, outroSeconds);
function parameter(name) {
    var preset = event.addGameParameter({name:name, type:studio.project.parameterType.UserDiscrete, min:0, max:1}).preset;
    preset.initialValue = 0;
    preset.cursorPosition = 0;
    preset.isGlobal = false;
    return preset;
}
var sustain = parameter('sustain');
var finish = parameter('finish');
var markers = event.markerTracks.length ? event.markerTracks[0] : event.addMarkerTrack();
var outro = markers.addNamedMarker('Outro', outroStart);
markers.addRegion(introSeconds, loopSeconds, 'Sustain', studio.project.regionLoopMode.Looping);
// A region checks immediately on entry, so short mode skips the loop without a gap.
markers.addTransitionRegion(introSeconds, loopSeconds, outro).addParameterCondition(sustain, 0, 0);
// Covers the intro too: an early kill must stop it and jump directly to the outro.
markers.addTransitionRegion(0, outroStart, outro).addParameterCondition(finish, 1, 1);
event.mixer.masterBus.volume = originalGain + 2;
migration.filter(function(row) { return !row.after; }).forEach(function(row) {
    studio.project.deleteObject(studio.project.lookup(row.guid));
});
// Remove only now-empty folders in our two old roots, never source assets or other projects.
function folderPath(f) {
    return f.folder ? folderPath(f.folder) + '/' + f.name : 'event:';
}
var folders = studio.project.model.EventFolder.findInstances().filter(function(f) {
    var path = folderPath(f);
    return path.indexOf('event:/NinjaSlayerAudio/') === 0 || path.indexOf('event:/NinjaSlayerEventMusic') === 0;
}).sort(function(a,b) { return folderPath(b).length - folderPath(a).length; });
folders.forEach(function(f) {
    if (!f.items.length) studio.project.deleteObject(f);
});
studio.project.save();
return JSON.stringify({migrated:migration.length, spinGuid:event.id, beforeGain:originalGain,
    afterGain:event.mixer.masterBus.volume, introSeconds:introSeconds, loopSeconds:loopSeconds,
    outroSeconds:outroSeconds, sustain:sustain.id, finish:finish.id});
