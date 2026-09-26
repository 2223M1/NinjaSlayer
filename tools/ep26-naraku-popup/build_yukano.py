"""Original 36-frame film in a transparent comic frame; NO person segmentation."""
from pathlib import Path
import json,subprocess,shutil
import numpy as np
import cv2
from PIL import Image
from build_popup import WORK,OUT,FPS,png,save_json,sha,checker,composite

DEST=WORK/'yukano-in-popup'
VIDEO=Path('C:/Users/theon/Videos/[2015][Ninja Slayer][BDRIP][1080P][1-26Fin+SP]/[Ninja Slayer][15][BDRIP][1920x1080][H264_THDx2].mkv')
M=np.array([[.95,0,-430],[0,.95,-240]],np.float64)
DURATION=36/FPS
PREVIEW_NAME='preview-right-no-hold.mp4'
PLAYBACK='5 native entry frames; original 36-frame film+audio once; zero default hold; immediate 4 native exit frames; hide'

def opening():
    m=np.zeros((1080*4,1920*4),np.uint8)
    cv2.fillPoly(m,[np.array([[0,0],[730,0],[847,501],[0,779]],np.int32)*4],255)
    return cv2.resize(m,(1920,1080),interpolation=cv2.INTER_AREA)

def preview():
    bg=checker();border=np.array(Image.open(OUT/'hold.png'));mask=opening().astype(float)[:,:,None]/255
    seq=[composite(np.array(Image.open(OUT/'native-rgba'/f'{i:04}.png')),bg) for i in range(5)]
    for n in range(42,78):
        source=np.array(Image.open(WORK/'ep15-source'/f'{n:04}.png').convert('RGB'))
        video=cv2.warpAffine(source,M,(1920,1080),flags=cv2.INTER_LINEAR)
        display=np.floor(video*mask+bg*(1-mask)+.5).astype(np.uint8)
        seq.append(composite(border,display))
    seq += [composite(np.array(Image.open(OUT/'native-rgba'/f'{i:04}.png')),bg) for i in range(141,145)]
    assert len(seq)==45
    cmd=['ffmpeg','-y','-v','error','-f','rawvideo','-pix_fmt','rgb24','-s','1280x720','-r','24000/1001','-i','pipe:0','-itsoffset',str(5/FPS),'-i',str(DEST/'synced-original-audio.wav'),'-map','0:v','-map','1:a','-c:v','libx264','-crf','16','-pix_fmt','yuv420p','-c:a','aac','-b:a','192k','-af','apad','-t',str(len(seq)/FPS),'-movflags','+faststart',str(DEST/PREVIEW_NAME)]
    proc=subprocess.Popen(cmd,stdin=subprocess.PIPE)
    for a in seq:proc.stdin.write(cv2.resize(a,(1280,720),interpolation=cv2.INTER_AREA).tobytes())
    proc.stdin.close();assert proc.wait()==0
    png(DEST/'preview-still.png',seq[23])
    save_json(DEST/'preview-timeline.json',{'fps':'24000/1001','frames':45,'duration_seconds':45/FPS,
        'entry_frames':[0,4],'film_frames':[5,40],'exit_frames':[41,44],
        'added_hold_frames':0,'trailing_blank_frames':0,'file':PREVIEW_NAME})

def build():
    DEST.mkdir(parents=True,exist_ok=True)
    frames=json.loads(subprocess.check_output(['ffprobe','-v','error','-select_streams','v:0','-read_intervals','307%+7','-show_entries','frame=best_effort_timestamp_time','-of','json',str(VIDEO)]))['frames']
    pts=[float(x['best_effort_timestamp_time']) for x in frames if 307.5<=float(x['best_effort_timestamp_time'])<310.8]
    rows=[]
    for n in range(42,78):
        s=WORK/'ep15-source'/f'{n:04}.png'
        rows.append({'index':n-42,'source_index':n,'source_frame_zero_based':round(pts[n-1]*FPS),'source_pts_seconds':pts[n-1],
            'source_png':str(s),'source_sha256':sha(s)})
    save_json(DEST/'frame-ledger.json',rows);png(DEST/'interior-clip.png',opening())
    start=pts[41];samples=36*2002
    subprocess.run(['ffmpeg','-y','-v','error','-ss',f'{start:.6f}','-i',str(VIDEO),'-map','0:1','-vn','-af',f'atrim=end_sample={samples},asetpts=PTS-STARTPTS','-c:a','pcm_s16le',str(DEST/'synced-original-audio.wav')],check=True)
    base=['ffmpeg','-y','-v','error','-framerate','24000/1001','-start_number','42','-i',str(WORK/'ep15-source/%04d.png'),'-i',str(DEST/'synced-original-audio.wav'),'-map','0:v','-map','1:a','-frames:v','36','-t',str(DURATION)]
    subprocess.run(base+['-c:v','libtheora','-q:v','10','-pix_fmt','yuv420p','-c:a','libvorbis','-q:a','8',str(DEST/'yukano-original.ogv')],check=True)
    subprocess.run(base+['-c:v','libx264','-crf','14','-pix_fmt','yuv420p','-c:a','aac','-b:a','256k','-movflags','+faststart',str(DEST/'yukano-original.mp4')],check=True)
    shutil.copyfile(WORK/'ep15-source/0077.png',DEST/'last-frame.png')
    for f in ['runtime-atlas.png','animation.json']:shutil.copyfile(OUT/f,DEST/f)
    for f in ['yukano_popup.gd','test_yukano_player.gd','clip.gdshader','project.godot','YUKANO_README.md']:
        shutil.copyfile(Path(__file__).parent/f,DEST/f)
    save_json(DEST/'manifest.json',{'schema_version':1,'status':'candidate-user-review','source_video':str(VIDEO),'source_video_sha256':sha(VIDEO),
        'first_pts_seconds':start,'last_pts_seconds':pts[76],'cut_to_next_shot_pts_seconds':pts[77],'frames':36,'fps':'24000/1001',
        'duration_seconds':DURATION,'requested_duration_seconds':1.5,'duration_rounding':'36 native frames = 1.5015 seconds',
        'source_to_panel':M.tolist(),'person_segmentation':False,'source_background':'retained as requested','focus':'chest and drawing hand',
        'audio':{'file':'synced-original-audio.wav','stream':1,'title':'TrueHD 2.0','language':'jpn','commentary':False,'sample_rate':48000,'sample_frames':samples,
            'offset_in_content_seconds':0,'offset_from_popup_start_seconds':5/FPS,'play_once':True,'sha256':sha(DEST/'synced-original-audio.wav')},
        'playback':PLAYBACK,'default_extra_hold_seconds':0,
        'layer_order':['source film clipped by interior-clip.png','original yellow popup frame'],
        'game_install_modified':False,'imagegen':False,'video_sha256':sha(DEST/'yukano-original.ogv')})
    preview()

def layout():
    before=sha(DEST/'yukano-original.ogv')
    for f in ['yukano_popup.gd','YUKANO_README.md','test_yukano_player.gd','visual_yukano.gd']:
        shutil.copyfile(Path(__file__).parent/f,DEST/f)
    manifest=json.loads((DEST/'manifest.json').read_text())
    manifest['source_to_panel']=M.tolist()
    manifest['layout_revision']='camera-right-105.26-source-pixels-no-hold-20260926'
    manifest['playback']=PLAYBACK
    manifest['default_extra_hold_seconds']=0
    manifest['preview_file']=PREVIEW_NAME
    save_json(DEST/'manifest.json',manifest)
    preview()
    assert before==sha(DEST/'yukano-original.ogv')

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser();p.add_argument('phase',nargs='?',default='build',choices=['build','layout'])
    globals()[p.parse_args().phase]()
