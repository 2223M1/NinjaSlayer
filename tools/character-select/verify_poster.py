"""Static resource invariants plus fresh native-game smoke-test evidence."""
from pathlib import Path
import json,hashlib,re,copy,argparse
from scarf_geometry import resize_tail
import numpy as np
from PIL import Image
P=Path(__file__).resolve().parents[2];D=P/'NinjaSlayer/art/character_select'
parser=argparse.ArgumentParser()
parser.add_argument('--require-runtime',action='store_true')
parser.add_argument('--assembly',type=Path)
parser.add_argument('--pack',type=Path)
a=parser.parse_args()
if a.require_runtime and (a.assembly is None or a.pack is None):
    parser.error('--require-runtime needs the tested --assembly and --pack')
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
m=read(D/'SOURCE.json');sources={Path(s['path']).name:Path(s['path']) for s in m['sources']}
checks={}
checks['source_hash_errors']=sum(sha(Path(s['path']))!=s['sha256'] for s in m['sources'])
checks['output_hash_errors']=sum(sha(D/name)!=h for name,h in m['outputs'].items())
checks['main_scene_hash_errors']=int(sha(P/'NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn')!=m['main_scene_sha256'])
old=read(sources['front.spjson']);new=read(D/'front.spjson');expected=copy.deepcopy(old)
for k in expected['animations']['animation']['bones']['scarf']['rotate']:k['value']=round(k['value']*2.5,7)
resize_tail(expected,read(sources['rig-manifest.json'])['scarf_length_transform'],D/'front-scarf.png')
checks['rig_changes_other_than_scarf_amplitude_and_length']=int(new!=expected)
profile=read(sources['rig-manifest.json'])['scarf_length_transform']
mesh=new['skins'][0]['attachments']['tail']['tail']
uv=np.array(mesh['uvs']).reshape(-1,2)*[mesh['width'],mesh['height']]
points=np.array(mesh['vertices']).reshape(-1,5)[:,2:4]*[1,-1]+profile['anchor']
fixed=uv[:,0]<=568
checks['scarf_collar_contact_changed']=int(np.any(np.abs(points[fixed]-uv[fixed])>2e-6))
sx=np.unique(uv[:,0]);sy=np.unique(uv[:,1]);grid=points.reshape(len(sy),len(sx),2)
alpha=np.array(Image.open(D/'front-scarf.png'))[:,:,3]
px=np.array(profile['envelope_source_x']);raw=[]
for x in px.astype(int):
    rows=np.flatnonzero(alpha[:,x]>profile['envelope_alpha_threshold']);raw.append([rows[0]-.5,rows[-1]+.5])
raw=np.array(raw)
cols=np.arange(580,int(grid[0,-1,0])-1)
inv=np.interp(cols,grid[0,:,0],sx)
source_width=np.interp(inv,px,raw[:,1]-raw[:,0])
vertical_scale=np.interp(inv,sx,(grid[-1,:,1]-grid[0,:,1])/(sy[-1]-sy[0]))
desired=[]
for edge,slope in zip((profile['envelope_top'],profile['envelope_bottom']),profile['beyond_crop_edge_slopes']):
    desired.append(np.where(cols<=754,np.interp(cols,px,edge),edge[-1]+slope*(cols-754)))
checks['scarf_width_error_over_1_5px_columns']=int(np.count_nonzero(np.abs(source_width*vertical_scale-(desired[1]-desired[0]))>1.5))
front=(D/'portrait_front.tscn').read_text()
pos=list(map(float,re.search(r'position = Vector2\(([^)]+)\)',front)[1].split(',')))
scale=list(map(float,re.search(r'scale = Vector2\(([^)]+)\)',front)[1].split(',')))
checks['bottom_anchored_shrink_transform_errors']=int(abs(pos[0]-1052.18409092)>1e-6 or abs(pos[1]-36.76727273)>1e-6 or scale!=[.956175,.956175])
oldm=sources['current-poster.tscn'].read_text();newm=(D/'portrait_manga.tscn').read_text()
animations=lambda s:re.findall(r'\[sub_resource type="Animation[^\n]*\][\s\S]*?(?=\n\[|\Z)',s)
checks['manga_animation_changed']=int([s.strip() for s in animations(oldm)]!=[s.strip() for s in animations(newm)])
for name in ['CharacterRoot','MotionRoot','Body','Hand','AnimationPlayer']:
    block=lambda s:re.search(r'\[node name="'+name+r'"[^\n]*\][\s\S]*?(?=\n\[|\Z)',s)[0].strip()
    checks['manga_'+name+'_changed']=int(block(oldm)!=block(newm))
main=(P/'NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn').read_text()
vanilla=sources['char_select_bg_ironclad.tscn'].read_text()
for name in ['ash2','ash3']:
    block=lambda s:re.search(r'\[node name="'+name+r'"[^\n]*\][\s\S]*?(?=\n\[|\Z)',s)[0].strip()
    v=block(vanilla).replace('ExtResource("4_cg5cs")','ExtResource("rice")').replace('ExtResource("5_fxeui")','ExtResource("dark")')
    checks['vanilla_'+name+'_parameter_changes']=int(v!=block(main))
checks['missing_local_resource_paths']=0
for f in list(D.glob('*.tscn'))+list(D.glob('*.tres'))+[P/'NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn']:
    for path in re.findall(r'path="res://([^"]+)"',f.read_text()):checks['missing_local_resource_paths']+=int(not(P/path).exists())
checks['localization_key_errors']=0
for lang in ['zhs','eng']:
    loc=read(P/f'NinjaSlayer/localization/{lang}/settings_ui.json')
    for key in ['APPEARANCE_TITLE','MANGA_SELECT_TITLE','MANGA_SELECT_DESCRIPTION']:
        checks['localization_key_errors']+=int(not loc.get('NINJA_SLAYER_SETTINGS_'+key))
if a.require_runtime:
    proof=read(P/'build/select-poster-runtime/qa/result.json')
    checks['native_smoke_failure']=int(proof['status']!='pass' or proof['default_manga'] or not proof['settings_navigation'] or not proof['background_preserved'])
    checks['native_bottom_anchor_error']=int(read(P/'build/select-poster-runtime/qa/composition-transform.json')['bottom_anchor_error_px']>.001)
    cutoff=read(P/'build/select-poster-runtime/qa/scarf-cutoff-audit.json')
    checks['scarf_cutoff_exposure']=int(cutoff['status']!='pass' or cutoff['visible_cutoff_samples']!=0)
    checks['scarf_cutoff_stale_evidence']=int(cutoff['rig_sha256']!=sha(D/'front.spjson') or cutoff['native_transform_sha256']!=sha(P/'build/select-poster-runtime/qa/composition-transform.json'))
    checks['staged_dll_diff']=int(sha(a.assembly)!=sha(P/'build/select-poster-runtime/isolated-game/mods/NinjaSlayer/NinjaSlayer.dll'))
    checks['staged_pack_diff']=int(sha(a.pack)!=sha(P/'build/select-poster-runtime/isolated-game/mods/NinjaSlayer/NinjaSlayer.pck'))
    prior=P.parent/'art-production/character-select-dual-poster-v1/composition-review-v2/baseline-hashes.json'
    checks['installed_game_files_changed']=sum(sha(Path(p))!=h for p,h in read(prior).items() if '/steamapps/' in p.replace('\\','/'))
out={'status':'pass' if not any(checks.values()) else 'failed','checks':checks,'runtime_tested':a.require_runtime,'installed':False,'uploaded':False}
dest=P/'build/select-poster-runtime/verification.json';dest.parent.mkdir(parents=True,exist_ok=True);dest.write_text(json.dumps(out,indent=2))
print(json.dumps(out,indent=2));raise SystemExit(int(any(checks.values())))
