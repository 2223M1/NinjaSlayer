from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2] / 'build/ep26-naraku-popup'
files = sorted((ROOT/'source').glob('*.png'))
stats=[]
for i,p in enumerate(files):
    a=np.array(Image.open(p)).astype(np.int16)
    m=(a[:,:,0]>180)&(a[:,:,1]>180)&(a[:,:,0]-a[:,:,2]>35)&(a[:,:,1]-a[:,:,2]>35)&(abs(a[:,:,0]-a[:,:,1])<40)
    yy,xx=np.where(m)
    stats.append({'index':i+1,'pixels':len(xx),'bbox':[int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1)] if len(xx) else None})
(ROOT/'discovery.json').write_text(json.dumps(stats,indent=2))
for name,indices in [('entry',range(1,33)),('exit',range(130,169)),('hold',[33,45,57,69,81,93,105,117])]:
    indices=list(indices)
    sheet=Image.new('RGB',(4*480,((len(indices)+3)//4)*294))
    d=ImageDraw.Draw(sheet)
    for k,n in enumerate(indices):
        im=Image.open(files[n-1]);im.thumbnail((480,270))
        xy=((k%4)*480,(k//4)*294)
        sheet.paste(im,(xy[0],xy[1]+24));d.text(xy,f'{n:04d}',fill='white')
    sheet.save(ROOT/f'{name}-native.jpg',quality=92)
print(json.dumps(stats[:30] + stats[130:],indent=2))
