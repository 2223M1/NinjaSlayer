"""Check the cropped scarf end over a loop using the measured native UI transform."""
from pathlib import Path
import hashlib
import json
import math

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
QA = ROOT / 'build/select-poster-runtime/qa'
RIG = ROOT / 'NinjaSlayer/art/character_select/front.spjson'
rig = json.loads(RIG.read_text())
native_path = QA / 'composition-transform.json'
native = json.loads(native_path.read_text())
mesh = rig['skins'][0]['attachments']['tail']['tail']
timelines = rig['animations']['animation']['bones']
assert not any(rig.get(k) for k in ('ik', 'transform', 'path'))
assert set(rig['animations']['animation']) == {'bones'}
assert all('curve' not in key for bone in timelines.values()
           for keys in bone.values() for key in keys)

# Include two original texture pixels before the cut, and the entire vertical
# mesh boundary (even transparent vertices), so filtering cannot reveal it.
boundary = []
vertices = mesh['vertices']
offset = 0
for u in mesh['uvs'][::2]:
    count = vertices[offset]
    assert count == 1, 'Scarf bounds must be updated if skin weights change'
    bone, x, y, weight = vertices[offset + 1:offset + 5]
    assert weight == 1 and rig['bones'][bone]['name'] == 'scarf'
    if u * mesh['width'] >= 752:
        boundary.append([x, y, 1])
    offset += 1 + 4 * count
assert offset == len(vertices) and boundary
points = np.array(boundary).T


def sample(keys, field, time, default):
    if not keys:
        return default
    return float(np.interp(time, [k.get('time', 0) for k in keys],
                           [k.get(field, default) for k in keys]))


def matrices(time):
    result = {}
    for bone in rig['bones']:
        assert not bone.get('shearX') and not bone.get('shearY')
        assert bone.get('inherit', 'normal') == 'normal'
        tl = timelines.get(bone['name'], {})
        assert set(tl) <= {'rotate', 'translate', 'scale'}
        angle = math.radians(bone.get('rotation', 0) + sample(tl.get('rotate'), 'value', time, 0))
        sx = bone.get('scaleX', 1) * sample(tl.get('scale'), 'x', time, 1)
        sy = bone.get('scaleY', 1) * sample(tl.get('scale'), 'y', time, 1)
        x = bone.get('x', 0) + sample(tl.get('translate'), 'x', time, 0)
        y = bone.get('y', 0) + sample(tl.get('translate'), 'y', time, 0)
        c, s = math.cos(angle), math.sin(angle)
        local = np.array([[c*sx, -s*sy, x], [s*sx, c*sy, y], [0, 0, 1]])
        result[bone['name']] = result.get(bone.get('parent'), np.eye(3)) @ local
    return result


basis = np.array([native['basis_x'], native['basis_y']]).T
origin = np.array(native['origin'])[:, None]
samples = []
for time in np.linspace(0, 8, 961):
    world = (matrices(time)['scarf'] @ points)[:2]
    world[1] *= -1  # Spine y-up to Godot y-down.
    screen = basis @ world + origin
    samples.append((float(screen[0].min()), float(time)))
minimum, phase = min(samples)
report = {
    'status': 'pass' if minimum > 1922 else 'failed',
    'rig_sha256': hashlib.sha256(RIG.read_bytes()).hexdigest(),
    'native_transform_sha256': hashlib.sha256(native_path.read_bytes()).hexdigest(),
    'viewport': [1920, 1080], 'duration_seconds': 8, 'sample_count': len(samples),
    'boundary_vertex_count': len(boundary), 'boundary_source_x_min': 752,
    'minimum_cut_boundary_screen_x': minimum,
    'minimum_offscreen_margin_px': minimum - 1920,
    'worst_phase_seconds': phase,
    'visible_cutoff_samples': sum(x <= 1922 for x, _ in samples),
}
(QA / 'scarf-cutoff-audit.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
raise SystemExit(report['status'] != 'pass')
