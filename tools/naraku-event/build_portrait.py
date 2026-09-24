"""Rebuild Naraku's silent event film from declared episode 2/6 raster sources."""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "build/naraku-event"
VIDEOS = Path(r"C:/Users/theon/Videos/[2015][Ninja Slayer][BDRIP][1080P][1-26Fin+SP]")
FPS = 24000 / 1001
SHOTS = {"ep02": (565.482, 200), "ep06": (515.473, 240)}


def run(args):
    return subprocess.run([str(x) for x in args], check=True, capture_output=True).stdout


def save_json(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(obj, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read(episode, index):
    return np.array(Image.open(OUT / "sources" / episode / f"{index:04}.png").convert("RGB"))


def prepare():
    ledger = {}
    for ep, (start, count) in SHOTS.items():
        source = next(p for p in VIDEOS.iterdir() if f"[{ep[2:]}]" in p.name and p.suffix == ".mkv")
        folder = OUT / "sources" / ep
        folder.mkdir(parents=True, exist_ok=True)
        if len(list(folder.glob("*.png"))) != count:
            run(["ffmpeg", "-v", "error", "-y", "-ss", start, "-i", source,
                 "-frames:v", count, "-fps_mode", "passthrough", "-start_number", 0, folder / "%04d.png"])
        probe = json.loads(run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-read_intervals",
                               f"{start-1}%{start+(count+1)/FPS}", "-show_frames", "-show_entries",
                               "frame=best_effort_timestamp_time", "-of", "json", source]))
        pts = [float(f["best_effort_timestamp_time"]) for f in probe["frames"]]
        pts = [t for t in pts if t >= start - 0.0001][:count]
        assert len(pts) == count
        ledger[ep] = {"video": str(source), "video_sha256": sha(source), "fps": "24000/1001",
                      "frames": [{"index": i, "source_frame_zero_based": round(t * FPS), "pts": t,
                                  "file": str(folder / f"{i:04}.png"), "sha256": sha(folder / f"{i:04}.png")}
                                 for i, t in enumerate(pts)]}
        print(ep, count, pts[0], pts[-1], flush=True)
    save_json(OUT / "source-ledger.json", ledger)


def analyze():
    a, b = read("ep02", 12), read("ep06", 12)
    # Isolate matching facial artwork, not the red character or the moving fire.
    ma = np.zeros(a.shape[:2], np.uint8); ma[80:405, 850:1120] = 255
    mb = np.zeros(b.shape[:2], np.uint8); mb[100:1030, 530:1310] = 255
    sift = cv2.SIFT_create(nfeatures=10000, contrastThreshold=0.015)
    ka, da = sift.detectAndCompute(cv2.resize(a, None, fx=3, fy=3), cv2.resize(ma, None, fx=3, fy=3))
    kb, db = sift.detectAndCompute(b, mb)
    matches = [p for p,q in cv2.BFMatcher().knnMatch(da, db, k=2) if p.distance < .72*q.distance]
    pa = np.float32([ka[m.queryIdx].pt for m in matches]) / 3
    pb = np.float32([kb[m.trainIdx].pt for m in matches])
    matrix, inliers = cv2.estimateAffinePartial2D(pa, pb, method=cv2.RANSAC, ransacReprojThreshold=2.5)
    assert matrix is not None and inliers.sum() >= 20
    residual = np.linalg.norm(pa @ matrix[:,:2].T + matrix[:,2] - pb, axis=1)[inliers[:,0] > 0]
    metrics = {"ep02_to_ep06": matrix.tolist(), "matches": len(matches), "inliers": int(inliers.sum()),
               "median_residual": float(np.median(residual)), "p95_residual": float(np.percentile(residual,95))}
    print(metrics, flush=True)
    w = cv2.warpAffine(a, matrix, (1920,1080), flags=cv2.INTER_LANCZOS4)
    compare = np.concatenate([cv2.resize(b,(960,540)),cv2.resize(w,(960,540))],axis=1)
    Image.fromarray(compare).save(OUT / "alignment-comparison.jpg", quality=94)
    # Time-match the fire in shared source coverage at a reduced analysis resolution.
    eps=[]
    for ep,(_,count) in SHOTS.items():
        seq=[]
        for i in range(count):
            im=read(ep,i)
            if ep == "ep02": im=cv2.warpAffine(im,matrix,(1920,1080))
            seq.append(cv2.cvtColor(cv2.resize(im,(240,135)),cv2.COLOR_RGB2GRAY))
        eps.append(np.asarray(seq))
    aa,bb=eps
    dynamic=(np.std(bb.astype(float),axis=0)>12)
    dynamic[20:130,80:161]=False
    x=aa[:,dynamic].astype(np.float32); y=bb[:,dynamic].astype(np.float32)
    x-=x.mean(1,keepdims=True); y-=y.mean(1,keepdims=True)
    corr=(x @ y.T) / (np.linalg.norm(x,axis=1)[:,None]*np.linalg.norm(y,axis=1)[None,:]+1e-8)
    best=corr.argmax(1)
    metrics["temporal_pairs"]=[{"ep02_index":i,"ep06_index":int(j),"correlation":float(corr[i,j])} for i,j in enumerate(best)]
    offsets=[]
    for offset in range(-100,140):
        pairs=[(i,i+offset) for i in range(200) if 0<=i+offset<240]
        if len(pairs)>=96: offsets.append((float(np.mean([corr[i,j] for i,j in pairs])),offset,len(pairs)))
    metrics["constant_offsets"]=sorted(offsets,reverse=True)[:12]
    save_json(OUT / "analysis.json",metrics)
    print("offsets",metrics["constant_offsets"],"best",metrics["temporal_pairs"][::20],flush=True)


def compose():
    analysis=json.loads((OUT / "analysis.json").read_text(encoding="utf-8"))
    ledger=json.loads((OUT / "source-ledger.json").read_text(encoding="utf-8"))
    assert all(p["ep02_index"]==p["ep06_index"] for p in analysis["temporal_pairs"])
    m=np.array(analysis["ep02_to_ep06"],np.float64)
    union=np.zeros((470,1920),bool)
    source_clipped=[]
    for i in range(200):
        im=read("ep02",i)[:470]
        union |= im.max(2)>8
        boundary=np.concatenate([im[:2].reshape(-1,3),im[-2:].reshape(-1,3),im[:,:2].reshape(-1,3),im[:,-2:].reshape(-1,3)])
        source_clipped.append(int((boundary.max(1)>8).sum())>4)
    ys,xs=np.where(union)
    native_bounds=[int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)]
    print("ep02_complete_union",native_bounds,flush=True)
    # The entire fire lives above the red character; this is a fixed scene crop,
    # not a luminance key. Dim fire pixels inside are retained unchanged.
    corners=np.array([[xs.min()-24,ys.min()-24],[xs.max()+25,ys.max()+25]]) @ m[:,:2].T + m[:,2]
    lo=np.floor(np.minimum(corners[0],[0,0])/2)*2-32
    hi=np.ceil(np.maximum(corners[1],[1920,1080])/2)*2+32
    width,height=(hi-lo).astype(int)
    offset=-lo
    native=np.array([[1.,0.,offset[0]],[0.,1.,offset[1]]])
    donor=m.copy();donor[:,2]+=offset
    # Weight only at the recorded source-frame boundary. Core of episode 6 is exact.
    yy,xx=np.mgrid[:1080,:1920]
    distance=np.minimum.reduce([xx,yy,1919-xx,1079-yy]).astype(np.float32)
    weight=np.clip(distance/24,0,1)
    selection=np.floor(weight*255+.5).astype(np.uint8)
    Image.fromarray(selection).save(OUT / "ep06-selection.png")
    weight=selection.astype(np.float32)/255
    spec={"provenance_mode":"declared-multisource-derivative","ep02_source_rect":[0,0,1920,470],
          "ep06_to_master":native.tolist(),"ep02_to_master":donor.tolist(),
          "master_size":[int(width),int(height)],"ep06_selection":"ep06-selection.png",
          "seam_width_source_pixels":24,"reason":"soft smoke crossing a source-frame boundary; face core unaffected",
          "blend":"ep06*w + aligned_ep02*(1-w)","interpolation":"Lanczos4, each source sampled directly for each deliverable",
          "temporal_offset":0,"ep02_visible_union_gt8":native_bounds,"frame_rate":"24000/1001",
          "color_adjustment":None,"ai_generated":False,"alpha_contract":"opaque black-background video, no inferred smoke alpha"}

    def render(a,b,scale=1.,size=None):
        if size is None: size=(int(width),int(height))
        ma=donor.copy()*scale;mb=native.copy()*scale
        low=cv2.warpAffine(a[:470],ma,size,flags=cv2.INTER_LANCZOS4).astype(np.float32)
        high=cv2.warpAffine(b,mb,size,flags=cv2.INTER_LANCZOS4).astype(np.float32)
        w=cv2.warpAffine(weight,mb,size,flags=cv2.INTER_LINEAR).clip(0,1)[:,:,None]
        return np.floor(high*w+low*(1-w)+.5).clip(0,255).astype(np.uint8)

    # A single scale and canvas for the whole loop, never per-frame bbox centering.
    runtime_scale=min(1280/width,960/height)
    runtime_size=(int(np.ceil(width*runtime_scale/2)*2),int(np.ceil(height*runtime_scale/2)*2))
    spec["runtime_size"]=list(runtime_size);spec["runtime_scale"]=float(runtime_scale)
    reduced=[]
    for i in range(200):
        reduced.append(cv2.resize(render(read("ep02",i),read("ep06",i),runtime_scale,runtime_size),(320,240)))
    a=np.asarray(reduced,dtype=np.float32)
    dynamic=a.std(0).max(2)>8
    x=a[:,dynamic].reshape(200,-1)
    steps=np.abs(np.diff(x,axis=0)).mean(1)
    candidates=[]
    for first in range(200-36):
        for last in range(first+35,200):
            if any(source_clipped[first:last+1]): continue
            jump=float(np.abs(x[last]-x[first]).mean())
            velocity=float(np.abs((x[last]-x[last-1])-(x[first+1]-x[first])).mean())
            candidates.append({"first":first,"last":last,"frames":last-first+1,"jump_mae":jump,
                               "velocity_mae":velocity,"score":jump+.25*velocity})
    candidates.sort(key=lambda c:(c["score"],-c["frames"]))
    assert candidates, "No sufficiently long complete-fire loop; do not deliver a clipped candidate"
    chosen=candidates[0]
    chosen["native_step_mae_median"]=float(np.median(steps));chosen["native_step_mae_p95"]=float(np.percentile(steps,95))
    assert chosen["jump_mae"] <= np.percentile(steps,95)
    spec["loop"]=chosen
    spec["rejected_source_edge_frames"]=[i for i,v in enumerate(source_clipped) if v]
    master_folder=OUT/f"master-{chosen['first']:03}-{chosen['last']:03}"
    runtime_folder=OUT/f"runtime-{chosen['first']:03}-{chosen['last']:03}"
    spec["master_folder"]=str(master_folder);spec["runtime_folder"]=str(runtime_folder)
    save_json(OUT / "loop-candidates.json",candidates[:30])
    save_json(OUT / "composition.json",spec)
    print("loop",chosen,"master",width,height,"runtime",runtime_size,flush=True)
    master_folder.mkdir(exist_ok=True);runtime_folder.mkdir(exist_ok=True)
    frame_ledger=[]
    for out_i,i in enumerate(range(chosen["first"],chosen["last"]+1)):
        ep2,ep6=read("ep02",i),read("ep06",i)
        master=render(ep2,ep6)
        runtime=render(ep2,ep6,runtime_scale,runtime_size)
        mp=master_folder/f"{out_i:04}.png";rp=runtime_folder/f"{out_i:04}.png"
        Image.fromarray(master).save(mp);Image.fromarray(runtime).save(rp)
        frame_ledger.append({"output_frame":out_i,"source_index":i,
                             "ep02":ledger["ep02"]["frames"][i],"ep06":ledger["ep06"]["frames"][i],
                             "master":str(mp),"runtime":str(rp),"master_sha256":sha(mp),"runtime_sha256":sha(rp)})
    save_json(OUT/"frame-ledger.json",frame_ledger)
    asset=ROOT/"NinjaSlayer/videos/naraku_event.ogv";asset.parent.mkdir(parents=True,exist_ok=True)
    run(["ffmpeg","-v","error","-y","-framerate","24000/1001","-i",runtime_folder/"%04d.png",
         "-frames:v",chosen["frames"],"-an","-c:v","libtheora","-q:v","10","-pix_fmt","yuv420p",asset])
    run(["ffmpeg","-v","error","-y","-framerate","24000/1001","-i",runtime_folder/"%04d.png",
         "-frames:v",chosen["frames"],"-an","-c:v","libx264","-crf","15","-pix_fmt","yuv420p","-movflags","+faststart",OUT/"naraku-event-loop.mp4"])
    first_image=np.array(Image.open(master_folder/"0000.png"))
    poster_path=ROOT/"NinjaSlayer/images/events/naraku_event.png"
    layout(check_video_hash=False)
    cards=Image.new("RGB",(1440,850));draw=ImageDraw.Draw(cards)
    sample=frame_ledger[0]["source_index"]
    for n,(im,label) in enumerate([(read("ep06",sample),"Episode 6: HD face + shared fire"),
                                  (read("ep02",sample),"Episode 2: complete outer fire, same native animation frame"),
                                  (first_image,"Composite: original fire extensions restored")]):
        pic=Image.fromarray(im);pic.thumbnail((720,400));xy=((n%2)*720,(n//2)*425+25)
        cards.paste(pic,xy);draw.text((xy[0]+10,xy[1]-20),label,fill="white")
    cards.save(OUT/"source-comparison.jpg",quality=92)
    save_json(OUT/"delivery.json",{"status":"candidate-user-approval-pending","video":str(asset),"video_sha256":sha(asset),
                                  "poster":str(poster_path),"poster_sha256":sha(poster_path),"frames":chosen["frames"],
                                  "fps":"24000/1001","duration":chosen["frames"]/FPS,"audio":False,
                                  "composition":str(OUT/"composition.json"),"frame_ledger":str(OUT/"frame-ledger.json")})


def layout(check_video_hash=True):
    """Reposition the existing audited film without re-encoding or changing frames."""
    spec=json.loads((OUT/"composition.json").read_text(encoding="utf-8"))
    width,height=spec["master_size"]
    offset=np.array(spec["ep06_to_master"])[:,2]
    face_center=np.array([945.,570.])+offset
    display_scale=1.2*1.4*min(500/1000,760/width,880/height)
    center=np.array([880.,615.])
    video_size=np.array([width,height])*display_scale
    top_left=center-face_center*display_scale
    spec["portrait_placement"]={"revision":"larger-raised-168-left30-20260924",
        "face_center_source_ep06":[945,570],"face_center_portrait":center.tolist(),
        "video_position":top_left.tolist(),"video_size":video_size.tolist(),
        "display_scale_from_master":float(display_scale),"scale_vs_initial":1.68,"scale_vs_previous":1.4,
        "screen_face_center_delta_vs_initial":[52.0,-46.8],
        "horizontal_compensation_reason":"user-requested slight left shift: 30 portrait pixels; size and height unchanged",
        "viewport_overflow_authorization":{"edge":"left","content":"outer flame/smoke only",
                                            "user_confirmed":True,"date":"2026-09-24"}}
    save_json(OUT/"composition.json",spec)
    first_image=np.array(Image.open(Path(spec["master_folder"])/"0000.png"))
    display=np.array([[display_scale,0,top_left[0]],[0,display_scale,top_left[1]]])
    poster=cv2.warpAffine(first_image,display,(2560,1200),flags=cv2.INTER_LANCZOS4)
    poster_path=ROOT/"NinjaSlayer/images/events/naraku_event.png"
    poster_path.parent.mkdir(parents=True,exist_ok=True);Image.fromarray(poster).save(poster_path)
    preview=cv2.warpAffine(poster,np.array([[1.04,0,-371.2],[0,1.04,-79.]]),(1920,1080),flags=cv2.INTER_LANCZOS4)
    Image.fromarray(preview).save(OUT/"event-position-preview.png")
    # Keep the scene and poster driven by the same numbers; compensate native VfxOffset.
    scene_path=ROOT/"scenes/vfx/events/ninja_slayer_event_naraku_event_vfx.tscn"
    import re
    scene=scene_path.read_text(encoding="utf-8")
    local=top_left-np.array([268.,49.])
    for key,value in zip(("left","top","right","bottom"),[*local,*(local+video_size)]):
        scene=re.sub(rf"offset_{key} = [-0-9.]+",f"offset_{key} = {value:.6f}",scene)
    temporary=scene_path.with_suffix(".tscn.tmp");temporary.write_text(scene,encoding="utf-8");temporary.replace(scene_path)
    delivery_path=OUT/"delivery.json"
    if delivery_path.exists():
        delivery=json.loads(delivery_path.read_text(encoding="utf-8"))
        if check_video_hash:
            assert delivery["video_sha256"]==sha(Path(delivery["video"])), "Layout-only update changed the video"
        delivery["poster_sha256"]=sha(poster_path);delivery["layout_revision"]=spec["portrait_placement"]["revision"]
        save_json(delivery_path,delivery)
    print("placement",spec["portrait_placement"],flush=True)


if __name__ == "__main__":
    parser=argparse.ArgumentParser();parser.add_argument("phase",choices=["prepare","analyze","compose","layout"])
    globals()[parser.parse_args().phase]()
