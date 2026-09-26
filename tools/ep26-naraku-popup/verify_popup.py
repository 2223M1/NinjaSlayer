from pathlib import Path
import json
import cv2
import numpy as np
from PIL import Image
from build_popup import OUT, WORK, VIDEO, FPS, sha, extract, save_json

rows=json.loads((OUT/'frame-ledger.json').read_text())
spec=json.loads((OUT/'animation.json').read_text())
manifest=json.loads((OUT/'manifest.json').read_text())
report={k:0 for k in ['source_hash_errors','output_hash_errors','reconstruction_mismatch','core_rgb_mismatch','outside_edge_rgb_changes','hidden_rgb_pixels','alpha_core_edge_mismatch','atlas_reconstruction_mismatch','face_interior_visible_pixels','runtime_hold_mismatch']}
report['source_hash_errors']+=int(sha(VIDEO)!=manifest['source_video_sha256'])
opening=np.zeros((1080,1920),np.uint8)
cv2.fillPoly(opening,[np.array([[0,0],[711,0],[824,487],[0,758]],np.int32)],255)
for row in rows:
    i=row['output_index'];s=np.array(Image.open(row['source_png']).convert('RGB'))
    a=np.array(Image.open(OUT/row['rgba']));c=np.array(Image.open(OUT/'core'/f'{i:04}.png'))>0;e=np.array(Image.open(OUT/'edge-band'/f'{i:04}.png'))>0
    expected,ec,ee=extract(s)
    report['source_hash_errors']+=int(sha(row['source_png'])!=row['source_png_sha256'])
    report['output_hash_errors']+=int(sha(OUT/row['rgba'])!=row['rgba_sha256'])
    report['reconstruction_mismatch']+=int(np.any(expected!=a,axis=2).sum())
    report['core_rgb_mismatch']+=int(np.any(a[:,:,:3][c]!=s[c],axis=1).sum())
    report['outside_edge_rgb_changes']+=int((np.any(a[:,:,:3]!=s,axis=2)&(a[:,:,3]>0)&~e).sum())
    report['hidden_rgb_pixels']+=int(((a[:,:,3]==0)&np.any(a[:,:,:3]!=0,axis=2)).sum())
    report['alpha_core_edge_mismatch']+=int(((c&e)|((c|e)!=(a[:,:,3]>0))|(c&(a[:,:,3]!=255))|(c!=(ec>0))|(e!=(ee>0))).sum())
    assert np.array_equal(np.array(Image.open(OUT/'alpha'/f'{i:04}.png')),a[:,:,3])
    if row['phase']=='hold':report['face_interior_visible_pixels']+=int(((opening>0)&(a[:,:,3]>0)).sum())
    if i%24==0:print('verified',i+1,flush=True)
atlas=np.array(Image.open(OUT/spec['atlas']))
for ent in spec['entries']:
    original=np.array(Image.open(OUT/'native-rgba'/f'{ent["native_index"]:04}.png'))
    restored=np.zeros_like(original);x,y,w,h=ent['atlas_rect'];l,t=ent['source_origin']
    restored[t:t+h,l:l+w]=atlas[y:y+h,x:x+w]
    report['atlas_reconstruction_mismatch']+=int(np.any(original!=restored,axis=2).sum())
report['runtime_hold_mismatch']=int(np.any(np.array(Image.open(OUT/'hold.png'))!=np.array(Image.open(OUT/'native-rgba/0016.png')),axis=2).sum())
assert len(rows)==145 and [x['source_frame_zero_based'] for x in rows]==list(range(8327,8472))
assert [x['phase'] for x in rows]==['enter']*5+['hold']*136+['exit']*4
assert not np.array(Image.open(OUT/'empty.png')).any()
report['passed']=all(x==0 for x in report.values())
report['native_frames']=len(rows);report['native_fps']='24000/1001';report['duration_seconds']=len(rows)/FPS
save_json(OUT/'audit.json',report);print(json.dumps(report,indent=2));assert report['passed']
