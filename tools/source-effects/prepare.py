"""Reproducible source-frame extraction. No generated/repainted effect pixels."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess

import cv2
import numpy as np
from PIL import Image, ImageDraw

FPS = 24000 / 1001
ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('C:/Users/theon/Videos/[2015][Ninja Slayer][BDRIP][1080P][1-26Fin+SP]')
OUT = ROOT / 'build/source-effects-20260922'
ASSETS = ROOT / 'NinjaSlayer/images/vfx/low_health_blood'


def episode(number):
    return next(p for p in SOURCE.glob('*.mkv') if f'[{number:02}]' in p.name)


def frames(path, start, count, width=1920, fps=FPS):
    command = ['ffmpeg', '-v', 'error', '-ss', str(start), '-i', str(path),
               '-an', '-vf', f'fps={fps},scale={width}:-1', '-frames:v', str(count),
               '-f', 'rawvideo', '-pix_fmt', 'rgb24', 'pipe:1']
    process = subprocess.Popen(command, stdout=subprocess.PIPE)
    size = width * (width * 9 // 16) * 3
    try:
        for _ in range(count):
            data = process.stdout.read(size)
            if len(data) != size:
                raise RuntimeError('Incomplete source decode')
            yield np.frombuffer(data, np.uint8).reshape(-1, width, 3)
    finally:
        process.stdout.close()
        if process.wait() != 0:
            raise RuntimeError('Source decoder failed')


def sha(path):
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()


def rgba_matte(rgb, background, polygon):
    observed = rgb.astype(np.float32)
    bg = background.astype(np.float32)
    # The preceding shot is useful for edge color, not alpha: its character has
    # moved. Solving alpha against that silhouette prints a ghost into the plume.
    redness = observed[:, :, 0] - np.maximum(observed[:, :, 1], observed[:, :, 2])
    background_redness = bg[:,:,0] - np.maximum(bg[:,:,1],bg[:,:,2])
    alpha = np.clip((redness - 28) / 180, 0, 1) * np.clip((observed[:,:,0] - 120) / 70, 0, 1)
    alpha *= np.clip((redness - background_redness - 12) / 45,0,1)
    mask = np.zeros(alpha.shape, np.uint8)
    cv2.fillPoly(mask, [np.array(polygon, np.int32)], 255)
    alpha *= mask / 255
    alpha[alpha < .025] = 0
    solid = (alpha > .85) & (observed[:,:,0] > 190)
    if not solid.any():
        return np.zeros((*alpha.shape,4),np.uint8)
    # Propagate colors from actual opaque plume pixels into AA edges. The old
    # silver head must never enter foreground RGB through an unmatte operation.
    _,labels = cv2.distanceTransformWithLabels((~solid).astype(np.uint8),cv2.DIST_L2,5,labelType=cv2.DIST_LABEL_PIXEL)
    clean = rgb[solid][labels-1].copy()
    clean[alpha == 0] = 0
    return np.dstack((clean, alpha*255)).astype(np.uint8)


def blood():
    OUT.mkdir(parents=True, exist_ok=True)
    ASSETS.mkdir(parents=True, exist_ok=True)
    source = episode(1)
    background = next(frames(source, 215.0, 1))
    specs = [
        dict(name='head_0335', start=215.46525, end=218.927,
             polygon=[(995,0),(1265,0),(1400,647),(1313,647)], origin=(1348,643), direction=(-.35,-.937)),
        dict(name='arm_0450', start=288.55, end=294.1,
             polygon=[(0,630),(810,682),(823,752),(0,967)], origin=(808,721), direction=(-.988,.153)),
        dict(name='head_0450', start=288.55, end=292.5,
             polygon=[(875,665),(899,701),(1090,584),(1920,65),(1920,0),(1550,0),(1040,490)],
             origin=(909,678), direction=(.816,-.578)),
    ]
    manifest = {'source': str(source), 'source_sha256': sha(source), 'fps': '24000/1001',
                'method': 'Local polygon + temporal red-chroma matte; AA colors propagated from nearest opaque source plume pixel. '
                          'Source plumes extend out of frame; missing ends are not reconstructed.', 'clips': []}
    runtime = []
    for spec in specs:
        folder = OUT / spec['name']
        folder.mkdir(exist_ok=True)
        count = int((spec['end']-spec['start'])*FPS)
        bounds = cv2.boundingRect(np.array(spec['polygon'], np.int32))
        for index, rgb in enumerate(frames(source, spec['start'], count)):
            rgba = rgba_matte(rgb, background, spec['polygon'])
            x,y,w,h = bounds
            path = folder / f'{index:04}.png'
            Image.fromarray(rgba[y:y+h,x:x+w]).save(path)
            if spec['name'] == 'head_0335' and index < 24:
                dx,dy = spec['direction']
                ox,oy = spec['origin']
                matrix = np.array([[dx,dy,8-dx*ox-dy*oy],[-dy,dx,96+dy*ox-dx*oy]], np.float32)
                # Resample premultiplied color to avoid dark transparent fringes.
                premul = rgba.astype(np.float32) / 255
                premul[:,:,:3] *= premul[:,:,3:4]
                mapped = cv2.warpAffine(premul, matrix, (768,192), flags=cv2.INTER_LINEAR)
                # The original plume exits the frame. Feather only that missing
                # far boundary; do not fabricate an unseen cap or explosion.
                xx = np.arange(768)[None,:]
                yy = np.arange(192)[:,None]
                end = 590 + 30*np.sin(yy*.12) + 18*np.sin(yy*.31)
                taper = np.clip((end-xx)/130,0,1)
                taper = taper*taper*(3-2*taper)
                mapped *= taper[:,:,None]
                mapped[:,:,:3] /= np.maximum(mapped[:,:,3:4], 1/255)
                runtime.append(Image.fromarray(np.uint8(np.clip(mapped,0,1)*255)))
        manifest['clips'].append({**spec, 'frames': count, 'crop_xywh': list(bounds),
                                  'files': {p.name: sha(p) for p in folder.glob('*.png')}})
        print(spec['name'], count, flush=True)
    atlas = Image.new('RGBA', (768*4,192*6))
    for index, frame in enumerate(runtime):
        atlas.paste(frame, ((index%4)*768,(index//4)*192))
    atlas.save(ASSETS/'jet.png')
    # Only detached fringe pixels become droplets; do not copy the opaque jet core.
    fringe = runtime[12].copy()
    array = np.array(fringe)
    array[55:135,:,3] = 0
    array[:,:160,3] = 0
    pieces = [(190,10,270,50),(300,140,410,187),(440,0,550,50),(560,140,700,192)]
    splashes = Image.new('RGBA',(128*4,64))
    for index, box in enumerate(pieces):
        piece = Image.fromarray(array).crop(box)
        piece.thumbnail((128,64))
        splashes.paste(piece,(index*128,0))
    splashes.save(ASSETS/'spray.png')
    manifest['runtime'] = {'jet': sha(ASSETS/'jet.png'), 'spray': sha(ASSETS/'spray.png'),
                           'frame_size':[768,192], 'atlas_grid':[4,6], 'origin':[8,96],
                           'start':specs[0]['start'], 'frames':24,
                           'detached_fringe_frame':12, 'fringe_regions':pieces}
    (ASSETS/'SOURCE.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (OUT/'SOURCE.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    check = Image.new('RGB',(1536,768),'white')
    draw = ImageDraw.Draw(check)
    for row,index in enumerate([0,5,12,23]):
        draw.rectangle((768,row*192,1535,(row+1)*192),fill=(24,24,24))
        check.paste(runtime[index],(0,row*192),runtime[index])
        check.paste(runtime[index],(768,row*192),runtime[index])
    check.save(OUT/'blood-edge-check.png')


def explosion_index():
    folder = OUT/'explosion-scan'
    folder.mkdir(parents=True,exist_ok=True)
    candidates = []
    for subtitle in sorted(SOURCE.glob('*.ass')):
        for line in subtitle.read_text(encoding='utf-8-sig').splitlines():
            if not line.startswith('Dialogue:') or not re.search('撒[哟有呦由]',line):
                continue
            fields = line.split(',',9)
            h,m,s = fields[1].split(':')
            start = max(0,int(h)*3600+int(m)*60+float(s)-1)
            number = int(re.search(r'\[(\d\d)\]',subtitle.name)[1])
            key = f'ep{number:02}-{start:.2f}'
            sample = list(frames(episode(number),start,16,480,2))
            sheet = Image.new('RGB',(480*4,270*4))
            best = (0,0)
            for i,rgb in enumerate(sample):
                sheet.paste(Image.fromarray(rgb),((i%4)*480,(i//4)*270))
                r,g,b = rgb[:,:,0].astype(float),rgb[:,:,1].astype(float),rgb[:,:,2].astype(float)
                hot = ((r>200)&(g>110)&(r>b*1.35)).mean()
                if hot>best[0]: best=(hot,i)
            sheet.save(folder/(key+'.jpg'),quality=88)
            candidates.append({'id':key,'episode':number,'start':start,'score':best[0],
                               'peak_time':start+best[1]/2,'peak_index':best[1]})
            Image.fromarray(sample[best[1]]).save(folder/(key+'-peak.jpg'),quality=90)
            print(key,round(best[0],3),flush=True)
    candidates.sort(key=lambda item:item['score'],reverse=True)
    (folder/'index.json').write_text(json.dumps(candidates,indent=2),encoding='utf-8')
    summary = Image.new('RGB',(480*4,300*math.ceil(len(candidates)/4)))
    draw = ImageDraw.Draw(summary)
    for i,item in enumerate(candidates):
        x,y=i%4*480,i//4*300
        summary.paste(Image.open(folder/(item['id']+'-peak.jpg')),(x,y))
        draw.text((x+8,y+274),item['id']+f" peak {item['peak_time']:.2f}",fill='white')
    summary.save(folder/'overview.jpg',quality=90)


if __name__ == '__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('mode',choices=['blood','explosion-index'])
    args=parser.parse_args()
    blood() if args.mode=='blood' else explosion_index()
