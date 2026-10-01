"""Reproducible FMOD migration; backups and evidence live outside the source checkout."""
from pathlib import Path
import ast
import hashlib
import json
import shutil
import socket
import sys
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[2]
BASE = REPO.parent
PROJECT = BASE / 'STS2_FModProject_Minimal-main'
TASK = BASE / 'deliveries/FMOD-RESTRUCTURE-20260929'

def studio(code):
    with socket.create_connection(('127.0.0.1', 3663), 5) as sock:
        sock.settimeout(30)
        sock.sendall(('eval(' + json.dumps(code) + ');\n').encode('utf-8'))
        result = b''
        while b'out():' not in result and b'err():' not in result:
            chunk = sock.recv(65536)
            if not chunk:
                break
            result += chunk
    value = result.decode('utf-8')
    if 'err():' in value or 'out():' not in value:
        raise RuntimeError(value)
    return value.split('out():', 1)[1].split('\0')[0].strip()

def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')

def sha(path):
    with path.open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()

# Reuse only the read-only inventory functions, without loading capture side effects.
for node in ast.parse((BASE / 'deliveries/FMOD-LOUDNESS-20260928/calibrate.py').read_text(encoding='utf-8')).body:
    if isinstance(node, ast.FunctionDef) and node.name in ('prop', 'rel', 'inventory'):
        exec(compile(ast.Module(body=[node], type_ignores=[]), '<inventory>', 'exec'))

def snapshot():
    assert not (TASK / 'before').exists(), 'Do not overwrite the baseline'
    current = json.loads(studio('JSON.stringify({path:studio.project.filePath,modified:studio.project.isModified});'))
    assert Path(current['path']).resolve() == (PROJECT / 'STS2.fspro').resolve(), current
    shutil.copytree(PROJECT / 'Metadata', TASK / 'disk-before-save/Metadata')
    shutil.copy2(PROJECT / 'STS2.fspro', TASK / 'disk-before-save/STS2.fspro')
    save(TASK / 'open-project.json', current)
    assert studio('studio.project.save();') == 'true'
    shutil.copytree(PROJECT / 'Metadata', TASK / 'before/Metadata')
    shutil.copy2(PROJECT / 'STS2.fspro', TASK / 'before/STS2.fspro')
    shutil.copytree(REPO / 'NinjaSlayer/audio/fmod', TASK / 'before/runtime')
    for folder in ('Code', 'Content', 'Cards', 'Tests', 'tools/smoke-harness'):
        for path in (REPO / folder).rglob('*'):
            if path.suffix not in ('.cs', '.csproj', '.json', '.ps1') or any(part in ('bin', 'obj') for part in path.parts):
                continue
            dest = TASK / 'before/source' / path.relative_to(REPO)
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, dest)
    save(TASK / 'before-hashes.json', {str(p.relative_to(PROJECT)): sha(p)
         for folder in ('Assets', 'Metadata') for p in (PROJECT / folder).rglob('*') if p.is_file()})
    save(TASK / 'inventory-before.json', inventory(PROJECT))
    print(json.dumps({'snapshot': str(TASK), 'events': len(inventory(PROJECT)), 'open': current}))

def migrate():
    before = json.loads((TASK / 'inventory-before.json').read_text(encoding='utf-8'))
    assert inventory(PROJECT) == before, 'Project changed since the baseline'
    rows = []
    for event in before:
        name = event['name']
        old = event['path']
        new = 'event:/NinjaSlayerAudio/'
        if name in ('ninja_slayer_loop_spin_attack', 'ninja_slayer_outro_spin_attack'):
            rows.append(dict(guid=event['guid'], before=old, after=None, reason='merged into spin_attack'))
            continue
        if '/music/' in old:
            new += 'music/' + name
        elif name in ('ninja_slayer_select', 'ninja_slayer_transition', 'ninja_slayer_ninja_soul'):
            new += {'ninja_slayer_select': 'sfx/cinematics/character_select',
                    'ninja_slayer_transition': 'sfx/cinematics/run_transition',
                    'ninja_slayer_ninja_soul': 'sfx/combat/ninja_soul'}[name]
        else:
            character = old.split('/')[-2]
            leaf = name.removeprefix(character + '_')
            if name == 'ninja_slayer_intro_spin_attack':
                leaf = 'spin_attack'
            if leaf == 'kirisute_goumen':
                leaf = 'kirisute_gomen'
            new += ('sfx/narration/' if character == 'pangbai' else 'sfx/characters/' + character + '/') + leaf
        rows.append(dict(guid=event['guid'], before=old, after=new))
    save(TASK / 'path-migration.json', rows)
    script = (REPO / 'tools/fmod/restructure_20260929.js').read_text(encoding='utf-8')
    result = studio('(function(){var migration=' + json.dumps(rows) + ';' + script + '})()')
    save(TASK / 'authoring-result.json', json.loads(result))
    save(TASK / 'inventory-after.json', inventory(PROJECT))
    print(result)

if __name__ == '__main__':
    if sys.argv[1] == 'snapshot':
        snapshot()
    elif sys.argv[1] == 'migrate':
        migrate()
    elif sys.argv[1] == 'studio':
        print(studio(sys.stdin.read()))
