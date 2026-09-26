import json,subprocess,wave
import numpy as np
import cv2
from PIL import Image
from build_yukano import DEST,VIDEO,DURATION,opening,M,PREVIEW_NAME,FPS
from build_popup import sha,save_json,OUT,checker,composite

rows=json.loads((DEST/'frame-ledger.json').read_text())
m=json.loads((DEST/'manifest.json').read_text())
assert np.array_equal(np.array(m['source_to_panel']),M)
assert 'film.position = Vector2(-430, -240)' in (DEST/'yukano_popup.gd').read_text()
assert m['default_extra_hold_seconds']==0
probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-count_frames','-show_streams','-of','json',str(DEST/'yukano-original.ogv')]))['streams']
v=next(s for s in probe if s['codec_type']=='video');a=next(s for s in probe if s['codec_type']=='audio')
assert v['r_frame_rate']=='24000/1001' and int(v['nb_read_frames'])==36
assert abs(float(v['duration'])-DURATION)<1e-6 and abs(float(a['duration'])-DURATION)<1e-6
assert a['start_time']==v['start_time']=='0.000000'
assert int(a['sample_rate'])==48000 and int(a['channels'])==2
assert len(rows)==36 and rows[-1]['source_frame_zero_based']-rows[0]['source_frame_zero_based']==35
errors=sum(sha(r['source_png'])!=r['source_sha256'] for r in rows)
errors+=int(sha(VIDEO)!=m['source_video_sha256'])
assert errors==0
with wave.open(str(DEST/'synced-original-audio.wav'),'rb') as w:
    assert (w.getnframes(),w.getframerate(),w.getnchannels())==(72072,48000,2)
    pcm=w.readframes(w.getnframes())
reference=subprocess.check_output(['ffmpeg','-v','error','-ss',str(m['first_pts_seconds']),'-i',str(VIDEO),'-map','0:1','-vn','-af','atrim=end_sample=72072,asetpts=PTS-STARTPTS','-f','s16le','pipe:1'])
assert pcm==reference
data=subprocess.check_output(['ffmpeg','-v','error','-i',str(DEST/'yukano-original.ogv'),'-map','0:v','-f','rawvideo','-pix_fmt','rgb24','pipe:1'])
decoded=np.frombuffer(data,np.uint8).reshape(36,1080,1920,3)
maes=[]
for i,r in enumerate(rows):
    src=np.array(Image.open(r['source_png']).convert('RGB'))
    maes.append(float(np.abs(decoded[i].astype(float)-src).mean()))
assert max(maes)<3
mask=np.array(Image.open(DEST/'interior-clip.png'))
assert np.array_equal(mask,opening())
preview_probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-count_frames','-select_streams','v:0','-show_streams','-of','json',str(DEST/PREVIEW_NAME)]))['streams'][0]
assert int(preview_probe['nb_read_frames'])==45
assert preview_probe['r_frame_rate']=='24000/1001'
assert abs(float(preview_probe['duration'])-45/FPS)<1e-6
preview_raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(DEST/PREVIEW_NAME),'-map','0:v','-f','rawvideo','-pix_fmt','rgb24','pipe:1'])
preview_rgb=np.frombuffer(preview_raw,np.uint8).reshape(45,720,1280,3)
bg=checker();exit_errors=[]
for i in range(4):
    expected=composite(np.array(Image.open(OUT/'native-rgba'/f'{141+i:04}.png')),bg)
    expected=cv2.resize(expected,(1280,720),interpolation=cv2.INTER_AREA)
    exit_errors.append(float(np.abs(preview_rgb[41+i].astype(float)-expected).mean()))
assert max(exit_errors)<3, 'Exit must start immediately at frame 41, not after a repeated last frame'
assert np.abs(preview_rgb[41].astype(float)-preview_rgb[40]).mean()>5
report={'passed':True,'source_hash_errors':errors,'frame_count':36,'native_fps':'24000/1001','duration_seconds':DURATION,
    'first_source_frame':rows[0]['source_frame_zero_based'],'last_source_frame':rows[-1]['source_frame_zero_based'],
    'audio_source_pcm_mismatch_samples':0,'audio_sample_frames':72072,'audio_video_start_delta_seconds':0,'audio_video_duration_delta_seconds':0,
    'encoded_rgb_mae_mean':float(np.mean(maes)),'encoded_rgb_mae_max_frame':max(maes),'person_segmentation_used':False,
    'preview_frames':45,'preview_duration_seconds':45/FPS,'preview_added_hold_frames':0,
    'preview_exit_start_frame':41,'preview_exit_frame_mae_max':max(exit_errors)}
save_json(DEST/'audit.json',report)
files={p.name:sha(p) for p in DEST.iterdir() if p.is_file() and p.suffix not in ['.import','.uid'] and p.name!='SHA256.json'}
save_json(DEST/'SHA256.json',files)
print(json.dumps(report,indent=2))
