"""Import the approved ORIGINAL composition, not rejected A-D candidates.

Deterministic file/scene assembly only. Run from anywhere; no install or upload.
"""
from pathlib import Path
import argparse,json,hashlib,shutil,re
from scarf_geometry import resize_tail, EXTENSION
P=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser()
parser.add_argument('--production',type=Path,default=P.parent/'art-production/character-select-dual-poster-v1')
parser.add_argument('--vanilla',type=Path,default=P.parent/'Slay the Spire 2/Slay the Spire 2 v0.107.1/scenes/screens/char_select/char_select_bg_ironclad.tscn')
a=parser.parse_args();B=a.production.resolve();V=a.vanilla.resolve()
D=P/'NinjaSlayer/art/character_select';D.mkdir(parents=True,exist_ok=True)
prefix='res://NinjaSlayer/art/character_select/'
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def text(p,s):
    content=s.rstrip()+'\n'
    if not p.exists() or p.read_text(encoding='utf-8')!=content:
        p.write_text(content,encoding='utf-8',newline='\n')
sources=[]
def source(p,role):sources.append({'path':str(p),'sha256':sha(p),'role':role})
for name in ['front-body.png','front-scarf.png','background-base.png','background-smoke.png','background-copper.png','ember.png']:
    f=B/'assets'/name;shutil.copy2(f,D/name);source(f,'approved source texture')
for name in ['front','background']:
    f=B/'assets'/f'{name}.spjson';s=json.loads(f.read_text());source(f,'approved original rig')
    if name=='front':
        for k in s['animations']['animation']['bones']['scarf']['rotate']:k['value']=round(k['value']*2.5,7)
        profile_path=B/'rig-manifest.json'
        source(profile_path,'approved original canvas-space scarf width envelope')
        resize_tail(s,json.loads(profile_path.read_text())['scarf_length_transform'],D/'front-scarf.png')
    else:
        # Replace placeholder Spine specks with the native foreground particle systems.
        s['bones']=[b for b in s['bones'] if not b['name'].startswith('ember')]
        s['slots']=[b for b in s['slots'] if not b['name'].startswith('ember')]
        s['skins'][0]['attachments']={k:v for k,v in s['skins'][0]['attachments'].items() if not k.startswith('ember')}
        for domain in s['animations']['animation'].values():
            for key in list(domain):
                if key.startswith('ember'):del domain[key]
    text(D/f'{name}.spjson',json.dumps(s,separators=(',',':')))
    for ext in ['atlas','spatlas','tres']:
        f=B/'assets'/f'{name}.{ext}';text(D/f'{name}.{ext}',f.read_text().replace('res://assets/',prefix));source(f,'atlas/resource')
text(D/'play_spine.gd',(B/'scripts/play_spine.gd').read_text());source(B/'scripts/play_spine.gd','native Spine autoplay')
front=(B/'scenes/poster_front.tscn').read_text();source(B/'scenes/poster_front.tscn','approved ORIGINAL composition')
root=re.search(r'\[node name="NinjaSlayerFrontPoster" type="Control"\]([\s\S]*?)(?=\[node name="Background")',front)[1]
character=front[front.index('[node name="Character"'):]
# Measured native 1920x1080 AnimatedBg -> poster transform: 1.1x, (-164,-55.3).
# Uniform 10% enlargement about the SCREEN's right-center, not the source crop.
factor=1.1
anchor_local=[(1920+164)/1.1,(540+55.3)/1.1]
position=[round(anchor_local[i]+factor*(p-anchor_local[i]),8) for i,p in enumerate([1135,35])]
position[0]=round(position[0]-50/1.1,8)  # Additional 50 screen px left at 1080p.
scale=round(.915*factor,8)
bottom_anchor_local=[(960+164)/1.1,(1080+55.3)/1.1]
position=[round(a+.95*(p-a),8) for a,p in zip(bottom_anchor_local,position)]
scale=round(scale*.95,8)
position[0]=round(position[0]+42/1.1,8)  # Keep unchanged scarf cut outside the viewport.
character=character.replace('Vector2(1135, 35)',f'Vector2({position[0]}, {position[1]})').replace('Vector2(0.915, 0.915)',f'Vector2({scale}, {scale})')
text(D/'portrait_front.tscn',f'''[gd_scene load_steps=3 format=3]
[ext_resource type="SpineSkeletonDataResource" path="{prefix}front.tres" id="front"]
[ext_resource type="Script" path="{prefix}play_spine.gd" id="play"]
[node name="OfficialFrontPortrait" type="Node2D"]
{character}''')
old=(B/'sources/current-poster.tscn').read_text();source(B/'sources/current-poster.tscn','unchanged manga pose and hand animation')
anim=old[old.index('[sub_resource type="Animation"'):old.index('[sub_resource type="Gradient"')]
nodes=old[old.index('[node name="CharacterRoot"'):old.index('[node name="ash"')]
player=old[old.index('[node name="AnimationPlayer"'):]
bodyext='\n'.join(line for line in old.splitlines() if line.startswith('[ext_resource') and ('id="2_body"' in line or 'id="3_hand"' in line))
text(D/'portrait_manga.tscn',f'[gd_scene load_steps=5 format=3]\n{bodyext}\n{anim}\n[node name="MangaPortrait" type="Node2D"]\n{nodes}{player}')
vanilla=V.read_text();source(V,'vanilla Ironclad foreground ash2 + ash3; identical parameters and gradients')
def block(s,header):return re.search(re.escape(header)+r'[\s\S]*?(?=\n\[|\Z)',s)[0]
grad='\n\n'.join(block(vanilla,f'[sub_resource type="Gradient" id="{n}"]') for n in ['Gradient_fnicy','Gradient_w0doc'])
particles='\n\n'.join(block(vanilla,f'[node name="{n}" type="CPUParticles2D" parent="."]') for n in ['ash2','ash3'])
particles=particles.replace('ExtResource("4_cg5cs")','ExtResource("rice")').replace('ExtResource("5_fxeui")','ExtResource("dark")')
background=block(front,'[node name="Background" type="SpineSprite" parent="."]')
main=f'''[gd_scene load_steps=8 format=3]
[ext_resource type="Script" path="res://Code/Nodes/NinjaSlayerSelectBackground.cs" id="controller"]
[ext_resource type="SpineSkeletonDataResource" path="{prefix}background.tres" id="bg"]
[ext_resource type="Script" path="{prefix}play_spine.gd" id="play"]
[ext_resource type="Texture2D" path="res://NinjaSlayer/images/vfx/char_select/short_grain_rice_particle.png" id="rice"]
[ext_resource type="Texture2D" path="res://NinjaSlayer/images/vfx/char_select/short_rice_no_glow_particle.png" id="dark"]
{grad}
[node name="NinjaSlayerBg" type="Control"]
{root}script = ExtResource("controller")

{background}

{particles}
'''
text(P/'NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn',main)
manifest={'schema_version':1,'choice':'original composition enlarged 10% about screen right-center; A/B/C/D rejected','front_position':position,'front_scale':[scale,scale],'composition':{'factor':factor,'anchor_screen':[1920,540],'reference_canvas':[1920,1080],'base_position':[1135,35],'base_scale':.915,'measured_parent_scale':1.1,'measured_parent_origin':[-164,-55.3]},'scarf_animation_amplitude_multiplier':2.5,'scarf_period_seconds':8,'default':'front','manga_setting_default':False,'particle_source':'vanilla Ironclad ash2 and ash3','particle_layer':'foreground, as vanilla','sources':sources,'outputs':{p.name:sha(p) for p in D.iterdir() if p.is_file() and p.suffix not in ['.uid','.import'] and p.name!='SOURCE.json'},'main_scene_sha256':sha(P/'NinjaSlayer/scenes/char_select/char_select_bg_ninja_slayer.tscn')}
manifest['composition']['post_scale_screen_translation']=[-50,0]
manifest['composition']['final_scale_about_screen_bottom']={'factor':.95,'anchor':[960,1080]}
manifest['composition']['final_screen_translation']=[42,0]
manifest['scarf_extension_x']=EXTENSION
manifest['scarf_width_contract']='original canvas-space upper/lower envelope; fixed collar through x568; unchanged UVs, weights, topology and animation'
manifest['choice']='previous left50 composition reduced 5% about screen bottom-center, then moved right 42 screen px to preserve unchanged scarf length; A/B/C/D rejected'
text(D/'SOURCE.json',json.dumps(manifest,ensure_ascii=False,indent=2))
print(D)
