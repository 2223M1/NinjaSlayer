"""Deterministic material-aware extraction of episode 26's yellow popup frame.

No generated pixels, guessed geometry, retiming, or background/face content.
Interior RGB is untouched. Only the saved 3px transition band is decontaminated.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import subprocess
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt

ROOT=Path(__file__).resolve().parents[2]
WORK=ROOT/'build/ep26-naraku-popup'
OUT=WORK/'delivery'
VIDEO=Path('C:/Users/theon/Videos/[2015][Ninja Slayer][BDRIP][1080P][1-26Fin+SP]/[Ninja Slayer][26][BDRIP][1920x1080][H264_THD].mkv')
FPS=24000/1001
FIRST=14
LAST=158

def sha(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()

def save_json(p,v):
    p.parent.mkdir(parents=True,exist_ok=True)
    t=p.with_suffix(p.suffix+'.tmp')
    t.write_text(json.dumps(v,ensure_ascii=False,indent=2),encoding='utf-8');t.replace(p)

def png(p,a):
    p.parent.mkdir(parents=True,exist_ok=True)
    Image.fromarray(a).save(p)

def extract(rgb):
    f=rgb.astype(np.float32)
    r,g,b=np.moveaxis(f,-1,0)
    # This hue exists only on the popup graphic in the scoped 145 frames.
    # It seeds a trimap, not a binary alpha/key. White face and red body fail it.
    seed=((r>200)&(g>200)&(r-b>40)&(g-b>45)&(abs(r-g)<25)).astype(np.uint8)
    n,lab,stats,_=cv2.connectedComponentsWithStats(seed,8)
    good=np.zeros(n,np.uint8);good[1:]=(stats[1:,cv2.CC_STAT_AREA]>=6)
    seed=good[lab].astype(bool)
    inside=distance_transform_edt(seed)
    core=inside>=2.5
    outside=distance_transform_edt(~seed)
    band=(outside<=3)&~core
    # Nearest reliable samples are used only to estimate edge alpha/color.
    _,fi=distance_transform_edt(~core,return_indices=True)
    fg=f[fi[0],fi[1]]
    bg_known=outside>3
    _,bi=distance_transform_edt(~bg_known,return_indices=True)
    bg=f[bi[0],bi[1]]
    v=fg-bg
    a=np.clip(np.sum((f-bg)*v,axis=2)/np.maximum(np.sum(v*v,axis=2),1),0,1)
    # Chroma subsampling breaks a strict RGB line-fit at antialiased edges.
    # Reject non-yellow surroundings without punching holes in that AA band.
    yellow_evidence=(r-b>3)&(g-b>3)
    a[~(seed|(yellow_evidence&(outside<=2.5)))]=0
    a[a<.035]=0
    a[core]=1
    a[~(core|band)]=0
    alpha=np.floor(a*255+.5).astype(np.uint8)
    # Never replace opaque Core pixels. Only remove background tint in Edge.
    color=rgb.copy()
    edge=(alpha>0)&~core
    recovered=(f-bg*(1-a[:,:,None]))/np.maximum(a[:,:,None],.001)
    # Very low coverage is ill-conditioned: bound by its observed core color.
    recovered=np.clip(recovered,fg-8,fg+8)
    color[edge]=np.floor(np.clip(recovered[edge],0,255)+.5).astype(np.uint8)
    color[alpha==0]=0
    return np.dstack([color,alpha]),core.astype(np.uint8)*255,edge.astype(np.uint8)*255

def composite(a,bg):
    al=a[:,:,3:4].astype(np.float32)/255
    return np.floor(a[:,:,:3]*al+np.asarray(bg)*(1-al)+.5).astype(np.uint8)

def checker(w=1920,h=1080):
    y,x=np.indices((h,w));c=np.where((x//32+y//32)%2,86,55).astype(np.uint8)
    return np.repeat(c[:,:,None],3,axis=2)

def trial():
    for i in [14,16,30,155,157,158]:
        src=np.array(Image.open(WORK/'source'/f'{i:04}.png').convert('RGB'))
        rgba,c,e=extract(src)
        png(WORK/'trials'/f'{i:04}.png',rgba)
        png(WORK/'trials'/f'{i:04}-checker.png',composite(rgba,checker()))
        for name,bg in [('white',[255,255,255]),('black',[0,0,0]),('magenta',[170,0,160])]:
            png(WORK/'trials'/f'{i:04}-{name}.png',composite(rgba,bg))
        print(i,int((rgba[:,:,3]>0).sum()),flush=True)

def build():
    OUT.mkdir(parents=True,exist_ok=True)
    pts=json.loads(subprocess.check_output(['ffprobe','-v','error','-select_streams','v:0','-read_intervals','346%+12','-show_entries','frame=best_effort_timestamp_time','-of','json',str(VIDEO)]))['frames']
    pts=[float(x['best_effort_timestamp_time']) for x in pts if 346.75<=float(x['best_effort_timestamp_time'])<353.75]
    assert len(pts)==168,len(pts)
    rows=[]
    for n in range(FIRST,LAST+1):
        p=WORK/'source'/f'{n:04}.png';s=np.array(Image.open(p).convert('RGB'))
        a,c,e=extract(s);idx=n-FIRST
        rgba=OUT/'native-rgba'/f'{idx:04}.png'
        png(rgba,a);png(OUT/'alpha'/f'{idx:04}.png',a[:,:,3])
        png(OUT/'core'/f'{idx:04}.png',c);png(OUT/'edge-band'/f'{idx:04}.png',e)
        yy,xx=np.where(a[:,:,3]>0)
        bbox=[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)]
        assert np.array_equal(a[:,:,:3][c>0],s[c>0])
        rows.append({'output_index':idx,'source_index':n,'source_png':str(p),'source_png_sha256':sha(p),'source_frame_zero_based':round(pts[n-1]*FPS),'pts_seconds':pts[n-1],
                     'phase':'enter' if n<19 else 'hold' if n<155 else 'exit','rgba':str(rgba.relative_to(OUT)),'rgba_sha256':sha(rgba),
                     'bbox_xyxy':bbox,'visible_pixels':len(xx),'core_pixels':int((c>0).sum()),'edge_pixels':int((e>0).sum())})
        if idx%16==0: print(f'extracted {idx+1}/145',flush=True)
    save_json(OUT/'frame-ledger.json',rows)
    # Keep the source cadence; the runtime hold is a single audited native frame.
    hold_source=30
    blank=np.zeros((1080,1920,4),np.uint8);png(OUT/'empty.png',blank)
    from shutil import copyfile
    copyfile(OUT/'native-rgba'/f'{hold_source-FIRST:04}.png',OUT/'hold.png')
    runtime_indices=list(range(5))+[hold_source-FIRST]+list(range(141,145))
    atlas=Image.new('RGBA',(4096,4096));x=y=row_h=0;entries=[]
    for k,idx in enumerate(runtime_indices):
        a=Image.open(OUT/'native-rgba'/f'{idx:04}.png')
        l,t,r,b=rows[idx]['bbox_xyxy'];l=max(0,l-4);t=max(0,t-4);r=min(1920,r+4);b=min(1080,b+4)
        w,h=r-l,b-t
        if x+w+4>4096: x=0;y+=row_h+8;row_h=0
        assert y+h+4<=4096
        atlas.paste(a.crop((l,t,r,b)),(x,y))
        entries.append({'native_index':idx,'atlas_rect':[x,y,w,h],'source_origin':[l,t],'phase':rows[idx]['phase']})
        x+=w+8;row_h=max(row_h,h)
    atlas=atlas.crop((0,0,4096,y+row_h+4));atlas.save(OUT/'runtime-atlas.png')
    save_json(OUT/'animation.json',{'schema_version':1,'canvas_size':[1920,1080],'fps_numerator':24000,'fps_denominator':1001,
        'native_frames':145,'native_duration_seconds':145/FPS,'enter_frames':5,'exit_frames':4,
        'default_hold_seconds':136/FPS,'hold_mode':'freeze-audited-native-frame','hold_source_index':hold_source,
        'atlas':'runtime-atlas.png','entries':entries,'playback':'enter[0..4] at native fps; hold[5] for requested seconds; exit[6..9] at native fps; hide',
        'note':'No interpolation or fabricated inbetween frames. Offscreen edges preserve original framing. Next lower-right popup is excluded.'})
    save_json(OUT/'manifest.json',{'schema_version':1,'status':'candidate-user-review','source_video':str(VIDEO),'source_video_sha256':sha(VIDEO),
        'source_range':{'first_zero_based':rows[0]['source_frame_zero_based'],'last_zero_based':rows[-1]['source_frame_zero_based'],
                        'first_pts_seconds':rows[0]['pts_seconds'],'last_pts_seconds':rows[-1]['pts_seconds']},
        'method':'yellow material seed, connected components, 3px local two-color alpha solve; source Core unchanged; Edge-only deterministic inverse compositing',
        'source_face_smoke_and_background':'removed; Alpha zero; no opaque black interior',
        'transition_graphics':'Original yellow entry/exit wipes and flourishes retained, including round yellow flourish in exit; not a black-white face',
        'background_generation':False,'imagegen':False,'canvas':[1920,1080],'original_crop':'top/left cut by original video, no invented extension',
        'native_frames':145,'fps':'24000/1001','hold_source_index':hold_source,'game_install_modified':False,
        'rebuild_script':str(Path(__file__).resolve()),'versions':{'opencv':cv2.__version__,'numpy':np.__version__}})
    preview()

def preview():
    rows=json.loads((OUT/'frame-ledger.json').read_text())
    # Shorter default demonstration, not a retimed master.
    seq=list(range(5))+[16]*48+list(range(141,145))+[-1]*16
    frames=[]
    backgrounds=[checker(),np.full((1080,1920,3),255,np.uint8),np.full((1080,1920,3),[15,32,70],np.uint8)]
    for k,idx in enumerate(seq):
        a=np.zeros((1080,1920,4),np.uint8) if idx<0 else np.array(Image.open(OUT/'native-rgba'/f'{idx:04}.png'))
        im=Image.fromarray(composite(a,backgrounds[0])).resize((960,540),Image.Resampling.LANCZOS)
        frames.append(np.asarray(im))
    cmd=['ffmpeg','-y','-v','error','-f','rawvideo','-pix_fmt','rgb24','-s','960x540','-r','24000/1001','-i','pipe:0','-an','-c:v','libx264','-crf','16','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'preview-2s-hold.mp4')]
    p=subprocess.Popen(cmd,stdin=subprocess.PIPE)
    for a in frames: p.stdin.write(a.tobytes())
    p.stdin.close();assert p.wait()==0
    hold=np.array(Image.open(OUT/'hold.png'))
    sheet=Image.new('RGB',(1440,810));d=ImageDraw.Draw(sheet)
    for k,(name,bg) in enumerate([('checker / transparent opening',backgrounds[0]),('white',backgrounds[1]),('blue',backgrounds[2]),('magenta',np.full((1080,1920,3),[160,0,150],np.uint8))]):
        im=Image.fromarray(composite(hold,bg));im.thumbnail((720,405));sheet.paste(im,((k%2)*720,(k//2)*405));d.text(((k%2)*720+8,(k//2)*405+8),name,fill='red')
    sheet.save(OUT/'qa-hold-four-backgrounds.jpg',quality=95)
    png(OUT/'preview-hold-checker.png',composite(hold,backgrounds[0]))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('phase',choices=['trial','build','preview'])
    globals()[p.parse_args().phase]()
