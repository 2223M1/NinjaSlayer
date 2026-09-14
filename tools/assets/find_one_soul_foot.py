"""Search source shots for the SAME boot drawing at higher source resolution."""
import json
from pathlib import Path
import cv2
import numpy as np
from PIL import Image, ImageDraw
from rebuild_one_soul import WORK, VIDEO, read

work = WORK / "intermediate-foot-search"
work.mkdir(exist_ok=True)
reference = read(WORK / "ep26-332.100.png")
mask = np.zeros(reference.shape[:2], np.uint8)
mask[480:790, 665:880] = 255
sift = cv2.SIFT_create(nfeatures=4500, contrastThreshold=.01, edgeThreshold=20)
kr, dr = sift.detectAndCompute(cv2.cvtColor(reference, cv2.COLOR_RGB2GRAY), mask)
cap = cv2.VideoCapture(str(VIDEO))
fps = cap.get(cv2.CAP_PROP_FPS)
matches = []
for index, time in enumerate(np.arange(336, 704, .5)):
    cap.set(cv2.CAP_PROP_POS_MSEC, time*1000)
    ok, frame = cap.read()
    if not ok:
        break
    small = cv2.resize(frame, (960,540), interpolation=cv2.INTER_AREA)
    k, d = sift.detectAndCompute(cv2.cvtColor(small,cv2.COLOR_BGR2GRAY), None)
    if d is None:
        continue
    good = [a for a,b in cv2.BFMatcher().knnMatch(dr,d,k=2) if a.distance < .76*b.distance]
    if len(good) < 6:
        continue
    src = np.float32([kr[m.queryIdx].pt for m in good]); dst = np.float32([k[m.trainIdx].pt for m in good])
    transform, inliers = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=1.5,maxIters=3000)
    if transform is None:
        continue
    chosen = inliers.ravel().astype(bool)
    count = int(chosen.sum())
    scale = float(np.linalg.norm(transform[:,0])) * 2
    spread = np.linalg.norm(np.ptp(src[chosen],axis=0))
    if count < 5 or scale < .10 or scale > 5 or spread < 90:
        continue
    px = (src @ transform[:,:2].T + transform[:,2] - dst)[chosen]
    error = float(np.median(np.linalg.norm(px,axis=1))) * 2
    record = dict(time=float(time), matches=count, scale=scale,error=error, transform=(transform*2).tolist())
    matches.append(record)
    print(record,flush=True)
    cv2.imwrite(str(work / f"source-{time:07.3f}.png"), frame)
cap.release()
(work / "matches.json").write_text(json.dumps(matches,indent=2))
ranked = sorted(matches,key=lambda m:(m["scale"],m["matches"]),reverse=True)
selected=[]
for m in ranked:
    if all(abs(m["time"]-a["time"])>1 for a in selected):
        selected.append(m)
    if len(selected)==24:
        break
panel=Image.new("RGB",(4*480,6*300),(32,32,32)); draw=ImageDraw.Draw(panel)
for i,m in enumerate(selected):
    source=Image.open(work/f"source-{m['time']:07.3f}.png")
    matrix=np.asarray(m["transform"])
    corners=np.array([[640,450],[940,450],[940,1020],[640,1020]])@matrix[:,:2].T+matrix[:,2]
    crop=(int(corners[:,0].min())-20,int(corners[:,1].min())-20,int(corners[:,0].max())+20,int(corners[:,1].max())+20)
    tile=source.crop(crop); tile.thumbnail((240,260))
    x=i%4*480;y=i//4*300
    panel.paste(source.resize((240,135)),(x,y+30)); panel.paste(tile,(x+240,y+30))
    draw.text((x+4,y+4),f"{m['time']:.3f}s scale={m['scale']:.2f} n={m['matches']} err={m['error']:.2f}",fill="white")
panel.save(work/"candidates.png")
