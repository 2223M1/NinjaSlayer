"""Locate the existing Naraku cel in episode 26; the old cutout is registration-only."""
import json
import cv2
import numpy as np
from PIL import Image, ImageDraw
from rebuild_one_soul import ROOT, VIDEO, read

work=ROOT/"build/source-reconstruction/full-naraku"
work.mkdir(parents=True,exist_ok=True)
reference=read(ROOT/"NinjaSlayer/images/characters/ninja_slayer/naraku.png")
mask=((reference[:,:,1].astype(int)-reference[:,:,0].astype(int)<50)&(reference[:,:,:3].min(2)<240)).astype(np.uint8)*255
sift=cv2.SIFT_create(nfeatures=6000,contrastThreshold=.015)
features=[]
for flip in (False,True):
    image=reference[:,::-1].copy() if flip else reference
    m=mask[:,::-1].copy() if flip else mask
    features.append(sift.detectAndCompute(cv2.cvtColor(image,cv2.COLOR_RGB2GRAY),m))
cap=cv2.VideoCapture(str(VIDEO)); results=[]
for time in np.arange(0,327,.5):
    cap.set(cv2.CAP_PROP_POS_MSEC,time*1000);ok,frame=cap.read()
    if not ok:break
    small=cv2.resize(frame,(960,540),interpolation=cv2.INTER_AREA)
    k,d=sift.detectAndCompute(cv2.cvtColor(small,cv2.COLOR_BGR2GRAY),None)
    if d is None:continue
    for flip,(kr,dr) in enumerate(features):
        good=[m for m,n in cv2.BFMatcher().knnMatch(dr,d,k=2) if m.distance < .7*n.distance]
        if len(good)<12:continue
        src=np.float32([kr[m.queryIdx].pt for m in good]);dst=np.float32([k[m.trainIdx].pt for m in good])
        affine,chosen=cv2.estimateAffinePartial2D(src,dst,method=cv2.RANSAC,ransacReprojThreshold=1.5,maxIters=5000)
        if affine is None or chosen.sum()<12:continue
        scale=float(np.linalg.norm(affine[:,0]))*2
        if scale<.15 or scale>5:continue
        record=dict(time=float(time),flip=bool(flip),count=int(chosen.sum()),scale=scale,reference_to_source=(affine*2).tolist())
        results.append(record)
        cv2.imwrite(str(work/f"ep26-{time:07.3f}.png"),frame)
        print(time,flip,record["count"],scale,flush=True)
cap.release()
(work/"matches.json").write_text(json.dumps(results,indent=2))
ranked=sorted(results,key=lambda m:m["scale"],reverse=True)
selected=[]
for m in ranked:
    if all(abs(m["time"]-a["time"])>1.1 for a in selected):selected.append(m)
    if len(selected)==30:break
panel=Image.new("RGB",(1920,6*210));draw=ImageDraw.Draw(panel)
for i,m in enumerate(selected):
    x,y=i%5*384,i//5*210
    panel.paste(Image.open(work/f"ep26-{m['time']:07.3f}.png").resize((384,180)),(x,y+30))
    draw.text((x+4,y+4),f"{m['time']:.2f}s scale={m['scale']:.2f} n={m['count']}",fill="white")
panel.save(work/"candidates.png")
