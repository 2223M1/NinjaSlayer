"""Episode-26 source-pixel Naraku master; the old image supplies registration only."""
import hashlib
import json
import subprocess
import cv2
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from rebuild_one_soul import ROOT, VIDEO, read, matrix3

WORK = ROOT / "build/source-reconstruction/full-naraku"
REFERENCE = ROOT / "NinjaSlayer/images/characters/ninja_slayer/naraku.png"
TIMES = [10., 193.30, 193.25, 193.20, 193.15, 193.10, 193., 189.5, 33.5]


def main():
    reference = read(REFERENCE)
    oldmask = ((reference[:,:,1].astype(int)-reference[:,:,0].astype(int)<50)
               & (reference.min(2)<240)).astype(np.uint8)*255
    sift = cv2.SIFT_create(nfeatures=24000, contrastThreshold=.012, edgeThreshold=20)
    features = [sift.detectAndCompute(cv2.cvtColor(a,cv2.COLOR_RGB2GRAY),m)
                for a,m in ((reference,oldmask),(reference[:,::-1].copy(),oldmask[:,::-1].copy()))]
    size = (2508,2508)
    canvas=np.zeros((2508,2508,4),np.float32)
    owner=np.zeros((2508,2508),np.uint16)
    records=[]
    for time in TIMES:
        path=WORK/f"ep26-{time:07.3f}.png"
        if not path.exists():
            subprocess.run(["ffmpeg","-v","error","-ss",str(time),"-i",str(VIDEO),"-frames:v","1","-y",str(path)],check=True)
        rgb=read(path)
        k,d=sift.detectAndCompute(cv2.cvtColor(rgb,cv2.COLOR_RGB2GRAY),None)
        fits=[]
        for flip,(kr,dr) in enumerate(features):
            good=[m for m,n in cv2.BFMatcher().knnMatch(dr,d,k=2) if m.distance < .7*n.distance]
            if len(good)<10: continue
            src=np.float32([kr[m.queryIdx].pt for m in good]); dst=np.float32([k[m.trainIdx].pt for m in good])
            affine,selected=cv2.estimateAffinePartial2D(src,dst,method=cv2.RANSAC,ransacReprojThreshold=2,maxIters=20000)
            if affine is None: continue
            error=np.linalg.norm(src@affine[:,:2].T+affine[:,2]-dst,axis=1)
            fits.append((int(selected.sum()),flip,matrix3(affine),float(np.median(error[selected.ravel()>0]))))
        count,flip,transform,error=max(fits,key=lambda f:f[0])
        if flip: transform=transform@np.array([[-1,0,1253],[0,1,0],[0,0,1]])
        inverse=np.diag([2,2,1])@np.linalg.inv(transform)
        expected=cv2.warpAffine(oldmask,transform[:2],(1920,1080),flags=cv2.INTER_NEAREST)
        region=cv2.dilate(expected,np.ones((17,17),np.uint8))>0
        hsv=cv2.cvtColor(rgb,cv2.COLOR_RGB2HSV)
        background=(hsv[:,:,0]>95)&(hsv[:,:,0]<175)&(hsv[:,:,1]>35)
        binary=region & ~background
        # Retain separate flames, but reject isolated backdrop grain.
        labels,n=ndimage.label(binary)
        sizes=np.bincount(labels.ravel()); keep=sizes>12; keep[0]=False
        binary=keep[labels]
        inside=cv2.erode(binary.astype(np.uint8),np.ones((3,3),np.uint8))>0
        outside=cv2.dilate(binary.astype(np.uint8),np.ones((3,3),np.uint8))==0
        _,fi=ndimage.distance_transform_edt(~inside,return_indices=True)
        _,bi=ndimage.distance_transform_edt(~outside,return_indices=True)
        fg=rgb[tuple(fi)].astype(np.float32); bg=rgb[tuple(bi)].astype(np.float32)
        diff=fg-bg
        alpha=np.clip(((rgb-bg)*diff).sum(2)/np.maximum((diff*diff).sum(2),1),0,1)
        alpha[inside]=1;alpha[outside]=0
        color=rgb.astype(np.float32)/255
        edge=(~inside)&(~outside)&(alpha>.01)
        color[edge]=np.clip((color[edge]-bg[edge]/255*(1-alpha[edge,None]))/alpha[edge,None],0,1)
        rgba=np.dstack((color*alpha[:,:,None],alpha))
        conf=np.ones((1080,1920),np.float32)
        conf[:24]=0;conf[-24:]=0;conf[:,:24]=0;conf[:,-24:]=0
        conf=cv2.GaussianBlur(conf,(25,25),0)
        layer=cv2.warpAffine(rgba,inverse[:2],size,flags=cv2.INTER_LINEAR)
        confidence=cv2.warpAffine(conf,inverse[:2],size,flags=cv2.INTER_LINEAR)
        weight=confidence*(layer[:,:,3]>.02)
        weight*=np.clip(layer[:,:,3]/np.maximum(canvas[:,:,3],.01),0,1)
        canvas=canvas*(1-weight[:,:,None])+layer*weight[:,:,None]
        owner[weight>.5]=round(time*100)
        straight=layer.copy();straight[:,:,:3]/=np.maximum(straight[:,:,3:4],1/255)
        Image.fromarray(np.uint8(np.clip(straight,0,1)*255)).save(WORK/f"registered-{time:07.3f}.png")
        records.append(dict(time=time,whole_cel_mirrored=bool(flip),inliers=count,median_px=error,
                            source_to_master=inverse.tolist(),sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        print(time,count,error,flush=True)
    canvas[:,:,:3]/=np.maximum(canvas[:,:,3:4],1/255)
    rgba=np.uint8(np.clip(canvas,0,1)*255);rgba[rgba[:,:,3]==0,:3]=0
    result=Image.fromarray(rgba)
    result.save(WORK/"naraku-master.png")
    result.resize((1254,1254),Image.Resampling.LANCZOS).save(WORK/"naraku.png")
    result.getchannel("A").save(WORK/"alpha.png")
    Image.fromarray(owner).save(WORK/"pixel-source-map.png")
    for name,color in (("white","white"),("black","black")):
        bg=Image.new("RGB",result.size,color);bg.paste(result,mask=result.getchannel("A"))
        bg.resize((1000,1000)).save(WORK/f"check-{name}.png")
    (WORK/"provenance.json").write_text(json.dumps(dict(video=str(VIDEO),reference_role="Registration and search region only; no source pixels",frames=records,
        master_sha256=hashlib.sha256((WORK/"naraku-master.png").read_bytes()).hexdigest()),indent=2))


if __name__=="__main__":main()
