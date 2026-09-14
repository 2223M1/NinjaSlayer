"""Reconstruct the episode-26 cel from registered source pixels, without synthesis."""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import sys

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / "build/source-reconstruction/one-soul"
VIDEO = Path(r"C:/Users/theon/Videos/[2015][Ninja Slayer][BDRIP][1080P][1-26Fin+SP]/[Ninja Slayer][26][BDRIP][1920x1080][H264_THD].mkv")
TIMES = [331.1, 332.1, 333.1, 334.1, 335.1, 335.6, 336.5, 704.2, 705.2, 710.2, 711.2, 712.2, 714.2]
MID_TIMES = [412.38, 412.42, 412.46, 412.50, 412.54, 412.58, 412.62, 414.68, 414.75, 414.82, 414.90]
PROMPTS = {
    331.1: [(790, 80), (754, 285)],
    332.1: [(770, 100), (860, 340), (800, 650)],
    333.1: [(815, 210), (700, 525), (840, 820), (1580, 480)],
    334.1: [(810, 120), (830, 490), (850, 900), (1720, 250), (1510, 830)],
    335.1: [(975, 110), (840, 450), (935, 800), (1710, 415), (1420, 80)],
    335.6: [(948, 320), (811, 540), (960, 850), (1660, 380), (1420, 180)],
    336.5: [(865, 420), (800, 760), (1330, 860), (1810, 800)],
    704.2: [(1110, 550), (1100, 790), (1070, 875), (1350, 540)],
    705.2: [(1110, 550), (1100, 790), (1070, 875), (1350, 540)],
    710.2: [(1080, 570), (1090, 810), (1090, 895), (1350, 540)],
    711.2: [(1080, 570), (1090, 810), (1090, 895), (1350, 540)],
    712.2: [(1080, 570), (1090, 810), (1090, 895), (1350, 540)],
    714.2: [(1070, 540), (1060, 770), (1030, 910), (1360, 540)],
    412.38: [(120,660),(480,570),(880,550),(1140,480),(1430,720)],
    412.42: [(150,540),(760,520),(1190,380),(1490,790)],
    412.46: [(240,410),(750,470),(1280,400),(1470,800)],
    412.50: [(1050,465),(1320,520),(1480,800)],
    412.54: [(700,100),(890,280),(1230,470),(1270,840)],
    412.58: [(930,70),(1190,380),(1330,520),(1240,820)],
    412.62: [(990,70),(1140,300),(1340,560),(1270,730),(1040,480)],
    414.68: [(1390,535),(1410,720),(1500,925),(1790,540)],
    414.75: [(1370,525),(1390,760),(1770,570)],
    414.82: [(1310,495),(1330,820),(1800,585)],
    414.90: [(1260,395),(1330,730),(1790,680)],
}


def read(path):
    return np.asarray(Image.open(path).convert("RGB")).copy()


def extract():
    WORK.mkdir(parents=True, exist_ok=True)
    for time in TIMES + MID_TIMES:
        path = WORK / f"ep26-{time:07.3f}.png"
        if not path.exists():
            subprocess.run(["ffmpeg", "-v", "error", "-ss", str(time), "-i", str(VIDEO),
                            "-frames:v", "1", "-y", str(path)], check=True)
    paths = [WORK / f"ep26-{t:07.3f}.png" for t in TIMES]
    sheet = Image.new("RGB", (480 * 3, 270 * 5))
    from PIL import ImageDraw
    draw = ImageDraw.Draw(sheet)
    for i, path in enumerate(paths):
        x, y = i % 3 * 480, i // 3 * 270
        sheet.paste(Image.open(path).resize((480, 270)), (x, y))
        draw.text((x + 4, y + 4), path.stem, fill="white", stroke_width=2, stroke_fill="black")
    sheet.save(WORK / "sources.png")
    sheet = Image.new("RGB", (480*3, 270*4)); draw=ImageDraw.Draw(sheet)
    for i,t in enumerate(MID_TIMES):
        x,y=i%3*480,i//3*270
        sheet.paste(Image.open(WORK/f"ep26-{t:07.3f}.png").resize((480,270)),(x,y))
        draw.text((x+4,y+4),str(t),fill="white",stroke_width=2,stroke_fill="black")
    sheet.save(WORK/"mid-sources.png")


def segment():
    import torch
    sys.path.insert(0, str(Path.home() / ".codex/runtimes/source-pixel-cutout/src/sam2"))
    from sam2.build_sam import build_sam2
    from sam2.sam2_image_predictor import SAM2ImagePredictor
    checkpoint = Path.home() / ".codex/runtimes/source-pixel-cutout/models/sam2/sam2.1-large/sam2.1_hiera_large.pt"
    predictor = SAM2ImagePredictor(build_sam2("configs/sam2.1/sam2.1_hiera_l.yaml", str(checkpoint), device="cuda"))
    reports = {}
    for t, points in PROMPTS.items():
        output = WORK / f"mask-{t:07.3f}.png"
        if output.exists() and t not in (412.50,):
            continue
        source = read(WORK / f"ep26-{t:07.3f}.png")
        with torch.inference_mode(), torch.autocast("cuda", dtype=torch.bfloat16):
            predictor.set_image(source)
            box = np.array([590, 0, 1130, 860]) if t == 332.1 else np.array([660, 0, 950, 395]) if t == 331.1 else np.array([180,200,1590,1070]) if t == 412.50 else None
            masks, scores, _ = predictor.predict(point_coords=np.array(points), point_labels=np.ones(len(points)), box=box, multimask_output=True)
        selected = int(scores.argmax())
        Image.fromarray(masks[selected].astype(np.uint8) * 255).save(output)
        rgba = np.dstack((source, masks[selected].astype(np.uint8) * 255))
        rgba[rgba[:, :, 3] == 0, :3] = 0
        Image.fromarray(rgba).save(WORK / f"cut-{t:07.3f}.png")
        reports[str(t)] = {"points": points, "scores": scores.tolist(), "selected": selected}
        print(t, scores, flush=True)
    (WORK / "segmentation.json").write_text(json.dumps(reports, indent=2))


def register():
    # SIFT on the masked character, followed by a uniform-scale/rotation fit.
    # No anisotropic scaling, warping, or mirroring can alter the source proportions.
    sift = cv2.SIFT_create(nfeatures=18000, contrastThreshold=.015, edgeThreshold=20)
    features = {}
    for t in TIMES + MID_TIMES:
        image = read(WORK / f"ep26-{t:07.3f}.png")
        mask = np.asarray(Image.open(WORK / f"mask-{t:07.3f}.png"))
        features[t] = sift.detectAndCompute(cv2.cvtColor(image, cv2.COLOR_RGB2GRAY), mask)
    pairs = [(332.1, 333.1), (333.1, 334.1), (334.1, 335.1), (335.1, 335.6), (336.5, 335.6)]
    pairs += [(t, target) for t in TIMES if t > 700 for target in (335.6, 332.1)]
    pairs += [(t,335.6) for t in MID_TIMES]
    results = []
    for a, b in pairs:
        ka, da = features[a]; kb, db = features[b]
        matches = cv2.BFMatcher().knnMatch(da, db, k=2)
        good = [m for m, n in matches if m.distance < .72 * n.distance]
        src = np.float32([ka[m.queryIdx].pt for m in good]); dst = np.float32([kb[m.trainIdx].pt for m in good])
        affine, inliers = cv2.estimateAffinePartial2D(src, dst, method=cv2.RANSAC, ransacReprojThreshold=2, maxIters=20000, confidence=.999)
        if affine is None:
            raise RuntimeError(f"No cel registration: {a} -> {b}")
        chosen = inliers.ravel().astype(bool)
        error = np.linalg.norm(src @ affine[:, :2].T + affine[:, 2] - dst, axis=1)
        result = dict(source=a, target=b, matrix=affine.tolist(), inliers=int(chosen.sum()), median_px=float(np.median(error[chosen])), source_points=src[chosen].tolist(), target_points=dst[chosen].tolist())
        results.append(result)
        print(a, b, result["inliers"], result["median_px"], affine.tolist(), flush=True)
    (WORK / "registration.json").write_text(json.dumps(results, indent=2))


def matrix3(value):
    return np.vstack((np.asarray(value), [0, 0, 1]))


def clean_alpha(source, mask):
    from scipy import ndimage
    binary = mask > 0
    holes, count = ndimage.label(~binary)
    sizes = np.bincount(holes.ravel())
    small = sizes < 2400
    small[0] = False
    binary |= small[holes]
    inside = cv2.erode(binary.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
    outside = cv2.dilate(binary.astype(np.uint8), np.ones((5, 5), np.uint8)) == 0
    _, fi = ndimage.distance_transform_edt(~inside, return_indices=True)
    _, bi = ndimage.distance_transform_edt(~outside, return_indices=True)
    fg = source[tuple(fi)].astype(np.float32)
    bg = source[tuple(bi)].astype(np.float32)
    rgb = source.astype(np.float32)
    diff = fg - bg
    alpha = np.clip(((rgb-bg)*diff).sum(2) / np.maximum((diff*diff).sum(2), 1), 0, 1)
    alpha[inside] = 1; alpha[outside] = 0
    # The source RGB remains untouched throughout the opaque interior.
    edge = (~inside) & (~outside) & (alpha > .01)
    rgb[edge] = np.clip((rgb[edge] - bg[edge] * (1-alpha[edge, None])) / alpha[edge, None], 0, 255)
    result = np.dstack((np.rint(rgb).astype(np.uint8), np.rint(alpha*255).astype(np.uint8)))
    result[result[:, :, 3] == 0, :3] = 0
    return result


def compose():
    registrations = json.loads((WORK / "registration.json").read_text())
    by_pair = {(r["source"], r["target"]): matrix3(r["matrix"]) for r in registrations}
    transforms = {335.6: np.eye(3)}
    for a, b in [(335.1, 335.6), (334.1, 335.1), (333.1, 334.1), (332.1, 333.1), (336.5, 335.6)]:
        transforms[a] = transforms[b] @ by_pair[a, b]
    # Independent calf/boot registration, not the head fit, owns the missing foot.
    transforms[704.2] = by_pair[704.2, 335.6]
    for t in MID_TIMES:
        transforms[t] = by_pair[t,335.6]
    shift = np.array([[1, 0, 0], [0, 1, 100], [0, 0, 1]])
    size = (2300, 3200)
    canvas = np.zeros((size[1], size[0], 4), np.float32)
    owner = np.zeros((size[1], size[0]), np.uint16)
    layers = WORK / "layers"
    layers.mkdir(exist_ok=True)
    for t in [414.68, 412.62, 412.50, 412.46, 412.42, 412.38, 332.1, 333.1, 334.1, 335.1, 335.6, 336.5]:
        source = read(WORK / f"ep26-{t:07.3f}.png")
        mask = np.asarray(Image.open(WORK / f"mask-{t:07.3f}.png")).copy()
        occluded = None
        if t in MID_TIMES:
            # Grey smoke in this shot occludes the cel; never turn it into boot pixels.
            hsv = cv2.cvtColor(source,cv2.COLOR_RGB2HSV)
            smoke = (hsv[:,:,1] < 55) & (hsv[:,:,2] > 50) & (hsv[:,:,2] < 190)
            occluded = cv2.dilate(smoke.astype(np.uint8),np.ones((5,5),np.uint8)) > 0
            mask[occluded] = 0
        if t == 704.2:
            # Low-resolution source is only the underlay where the pan is occluded
            # or cropped. Every available high-resolution pixel supersedes it.
            pass
        if t == 336.5:
            # This close shot contributes the face only; keep the pan's hand/scarf.
            head = np.zeros_like(mask)
            cv2.fillPoly(head, [np.array([[470,0],[1190,0],[1190,650],[1040,970],[780,1040],[550,720]], np.int32)], 255)
            mask &= head
        rgba = clean_alpha(source, mask).astype(np.float32) / 255
        if occluded is not None:
            rgba[occluded] = 0
        # Fade only at source-frame boundaries/selection seams, never across the cel.
        confidence = np.ones(mask.shape, np.float32)
        confidence[:80,:] = 0; confidence[-80:,:] = 0
        confidence[:,:25] = 0; confidence[:,-25:] = 0
        confidence[80:100,:] *= np.linspace(0,1,20)[:,None]
        confidence[-100:-80,:] *= np.linspace(1,0,20)[:,None]
        if occluded is not None:
            # Keep the entire contaminated edge of moving smoke out of the splice.
            confidence *= np.clip(cv2.distanceTransform((~occluded).astype(np.uint8),cv2.DIST_L2,5)/12,0,1)
        if t == 332.1:
            # The golden foreground cuts across the toe: SAM's boundary is not a
            # character outline. End this source above that occlusion entirely.
            confidence[750:780] *= np.linspace(1,0,30)[:,None]
            confidence[780:] = 0
        rgba[:,:,:3] *= rgba[:,:,3:4]
        transform = shift @ transforms[t]
        warped = cv2.warpAffine(rgba, transform[:2], size, flags=cv2.INTER_LINEAR)
        conf = cv2.warpAffine(confidence, transform[:2], size, flags=cv2.INTER_LINEAR)
        if t == 414.68:
            canvas = warped
            owner[warped[:,:,3] > .1] = int(t*10)
        else:
            # Replace existing source pixels rather than accumulating alpha at each overlap.
            weight = conf * (warped[:,:,3] > .05)
            # Existing outer alpha must not be punched out by frame-edge SAM holes.
            weight *= np.clip(warped[:,:,3] / np.maximum(canvas[:,:,3],.01),0,1)
            canvas = canvas*(1-weight[:,:,None]) + warped*weight[:,:,None]
            owner[weight > .5] = int(t*10)
        straight = warped.copy()
        straight[:,:,:3] /= np.maximum(straight[:,:,3:4], 1/255)
        Image.fromarray(np.rint(np.clip(straight,0,1)*255).astype(np.uint8)).save(layers / f"registered-{t:07.3f}.png")
    alpha = canvas[:,:,3]
    ys, xs = np.where(alpha > .01)
    crop = [int(xs.min())-8, int(ys.min())-8, int(xs.max())+9, int(ys.max())+9]
    canvas[:,:,:3] /= np.maximum(canvas[:,:,3:4], 1/255)
    rgba = np.rint(np.clip(canvas,0,1)*255).astype(np.uint8)
    rgba[rgba[:,:,3] == 0,:3] = 0
    result = Image.fromarray(rgba).crop(crop)
    result.save(WORK / "one_body_one_soul.png")
    result.getchannel("A").save(WORK / "alpha.png")
    Image.fromarray(owner).crop(crop).save(WORK / "pixel-source-map.png")
    for label, color in [("black",(0,0,0)),("white",(255,255,255))]:
        background = Image.new("RGB", result.size, color)
        background.paste(result, mask=result.getchannel("A"))
        background.resize((int(result.width*900/result.height),900)).save(WORK / f"check-{label}.png")
    preview = read(WORK / "ep26-332.100.png")
    donor = read(WORK / "ep26-704.200.png")
    registered = cv2.warpAffine(donor, by_pair[704.2,332.1][:2], (1920,1080))
    panel = np.concatenate((preview[480:1000,620:930], registered[480:1000,620:930], cv2.addWeighted(preview, .5, registered, .5,0)[480:1000,620:930]), axis=1)
    Image.fromarray(panel).save(WORK / "foot-registration-check.png")
    report = dict(source_video=str(VIDEO), frame_rate="24000/1001", crop=crop,
                  transforms_to_master={str(t):(shift@m).tolist() for t,m in transforms.items()},
                  foot_registration=next(r for r in registrations if r["source"]==704.2 and r["target"]==332.1),
                  intermediate_donors=[r for r in registrations if r["source"] in MID_TIMES],
                  pixel_policy="Source RGB; uniform similarity registration; only edge unmixing. No generated or mirrored parts.",
                  sha256={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [WORK/"one_body_one_soul.png",WORK/"alpha.png"]})
    (WORK / "provenance.json").write_text(json.dumps(report, indent=2))
    print(result.size, crop, flush=True)


def calibrate():
    old_path = ROOT.parent / "unused_runtime_assets/legacy_assets/characters/ninja_slayer/one_body_one_soul.png"
    new = read(WORK/"one_body_one_soul.png")
    old = read(old_path)
    sift=cv2.SIFT_create(nfeatures=18000,contrastThreshold=.02)
    mask=np.asarray(Image.open(WORK/"alpha.png"))
    kn,dn=sift.detectAndCompute(cv2.cvtColor(new,cv2.COLOR_RGB2GRAY),mask)
    ko,do=sift.detectAndCompute(cv2.cvtColor(old,cv2.COLOR_RGB2GRAY),None)
    good=[m for m,n in cv2.BFMatcher().knnMatch(dn,do,k=2) if m.distance < .7*n.distance]
    src=np.float32([kn[m.queryIdx].pt for m in good]);dst=np.float32([ko[m.trainIdx].pt for m in good])
    affine,inliers=cv2.estimateAffinePartial2D(src,dst,method=cv2.RANSAC,ransacReprojThreshold=2,maxIters=20000)
    transform=matrix3(affine)
    old_scale=1080*.33/2560
    center=np.array([new.shape[1]/2,new.shape[0]/2,1])
    pos=(transform@center)[:2]-np.array([960,1280])
    pos=pos*old_scale+np.array([0,-183])
    hand=(np.linalg.inv(transform)@np.array([335.0842,432.413,1]))[:2]-center[:2]
    y,x=np.where(mask>230)
    foot=np.array([float(np.median(x[y>y.max()-10])),float(y.max())])-center[:2]
    # The torso hull is manually bounded to exclude the long scarf.
    body_region=np.zeros_like(mask)
    cv2.fillPoly(body_region,[np.array([[0,820],[40,230],[400,0],[650,0],[680,280],
        [855,365],[855,820],[780,1600],[550,1920],[425,2300],[370,2680],[170,2703],[75,1940]],np.int32)],255)
    body_mask=mask & body_region
    yy,xx=np.where(body_mask>230)
    hull=cv2.convexHull(np.column_stack((xx,yy)).astype(np.float32)).reshape(-1,2)
    hull=cv2.approxPolyDP(hull,5,True).reshape(-1,2)-center[:2]
    scale=float(np.linalg.norm(affine[:,0]))*old_scale
    core=np.array([520,900])-center[:2]
    report=dict(new_to_reference=affine.tolist(), inliers=int(inliers.sum()),
                median_error=float(np.median(np.linalg.norm(src@affine[:,:2].T+affine[:,2]-dst,axis=1)[inliers.ravel()>0])),
                size=[new.shape[1],new.shape[0]],body_position=pos.tolist(),body_scale=scale,
                hand=hand.tolist(),foot=foot.tolist(),core=core.tolist(),contour=hull.tolist(),
                standing_y=float(pos[1]+foot[1]*scale))
    (WORK/"calibration.json").write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stage", choices=["extract", "segment", "register", "compose", "calibrate"], default="extract", nargs="?")
    args = parser.parse_args()
    {"extract": extract, "segment": segment, "register": register, "compose": compose, "calibrate": calibrate}[args.stage]()
