from pathlib import Path
import zipfile,json
from build_popup import WORK,OUT,sha,save_json
from build_yukano import DEST,PREVIEW_NAME
assert json.loads((OUT/'audit.json').read_text())['passed']
assert json.loads((DEST/'audit.json').read_text())['passed']
target=WORK/'popup-frame-and-yukano-right-no-hold.zip'
mapping={}
for name in ['runtime-atlas.png','animation.json','hold.png','empty.png','popup_player.gd','README.md','audit.json','frame-ledger.json','manifest.json']:
    mapping['empty-frame/'+name]=OUT/name
for p in (OUT/'native-rgba').glob('*.png'):mapping['empty-frame/native-rgba/'+p.name]=p
for name in ['runtime-atlas.png','animation.json','interior-clip.png','last-frame.png','yukano_popup.gd','clip.gdshader','project.godot','YUKANO_README.md','test_yukano_player.gd','yukano-original.ogv','yukano-original.mp4','synced-original-audio.wav',PREVIEW_NAME,'preview-timeline.json','preview-still.png','manifest.json','frame-ledger.json','audit.json']:
    mapping['yukano-film/'+name]=DEST/name
hashes={k:sha(v) for k,v in mapping.items()}
with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
    for k,p in mapping.items():z.write(p,k)
    z.writestr('SHA256.json',json.dumps(hashes,indent=2))
with zipfile.ZipFile(target) as z:
    assert z.testzip() is None
    import hashlib
    assert all(hashlib.sha256(z.read(k)).hexdigest()==h for k,h in hashes.items())
save_json(WORK/'package-audit.json',{'zip':str(target),'sha256':sha(target),'files':len(mapping),'entry_hash_errors':0,'passed':True})
print(target, target.stat().st_size, 'bytes; archive hash verification passed')
