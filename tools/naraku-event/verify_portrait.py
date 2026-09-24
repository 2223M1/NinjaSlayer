"""Fresh source-hash, reconstruction, source-border, rate and encode audit."""
import json
from pathlib import Path

import cv2
import numpy as np
from PIL import Image

from build_portrait import OUT, ROOT, FPS, read, run, sha, save_json


def verify():
    spec=json.loads((OUT/"composition.json").read_text(encoding="utf-8"))
    ledger=json.loads((OUT/"frame-ledger.json").read_text(encoding="utf-8"))
    delivery=json.loads((OUT/"delivery.json").read_text(encoding="utf-8"))
    source_ledger=json.loads((OUT/"source-ledger.json").read_text(encoding="utf-8"))
    weight=np.array(Image.open(OUT/spec["ep06_selection"]),dtype=np.float32)/255
    ma=np.array(spec["ep02_to_master"]);mb=np.array(spec["ep06_to_master"])
    mw,mh=spec["master_size"];rw,rh=spec["runtime_size"]
    report={"source_hash_errors":0,"master_reconstruction_mismatch":0,"runtime_reconstruction_mismatch":0,
            "ep06_unmixed_core_rgb_mismatch":0,"source_fire_boundary_pixels_gt8":0,
            "output_border_pixels_gt8":0,"runtime_red_character_pixels":0,"file_hash_errors":0,
            "layout_fire_clipped_pixels_gt8":0,"layout_fire_in_text_region_pixels_gt8":0,
            "layout_unapproved_clipped_pixels_gt8":0,"layout_face_clipped_pixels_gt8":0,
            "layout_authorized_left_overflow_pixels":0}
    placement=spec["portrait_placement"]
    overflow=placement.get("viewport_overflow_authorization",{})
    allow_left=overflow.get("user_confirmed") is True and overflow.get("edge")=="left" and overflow.get("content")=="outer flame/smoke only"
    screen_position=np.array(placement["video_position"])*1.04+[-371.2,-79.]
    screen_scale=np.array(placement["video_size"])*1.04/[rw,rh]
    screen_min=np.array([np.inf,np.inf]);screen_max=-screen_min
    for ep in source_ledger.values(): report["source_hash_errors"]+=int(sha(Path(ep["video"]))!=ep["video_sha256"])
    runtime=[]
    native_core=weight==1
    ox,oy=mb[:,2].astype(int)
    for row in ledger:
        i=row["source_index"]
        a,b=read("ep02",i),read("ep06",i)
        for ep in ("ep02","ep06"):
            report["source_hash_errors"]+=int(sha(Path(row[ep]["file"]))!=row[ep]["sha256"])
        crop=a[:470]
        boundary=np.concatenate([crop[:2].reshape(-1,3),crop[-2:].reshape(-1,3),crop[:,:2].reshape(-1,3),crop[:,-2:].reshape(-1,3)])
        report["source_fire_boundary_pixels_gt8"]+=int((boundary.max(1)>8).sum())
        for key,scale,size in (("master",1.,(mw,mh)),("runtime",spec["runtime_scale"],(rw,rh))):
            low=cv2.warpAffine(crop,ma*scale,size,flags=cv2.INTER_LANCZOS4).astype(np.float32)
            high=cv2.warpAffine(b,mb*scale,size,flags=cv2.INTER_LANCZOS4).astype(np.float32)
            w=cv2.warpAffine(weight,mb*scale,size,flags=cv2.INTER_LINEAR).clip(0,1)[:,:,None]
            rebuilt=np.floor(high*w+low*(1-w)+.5).clip(0,255).astype(np.uint8)
            path=Path(row[key]);actual=np.array(Image.open(path))
            report["file_hash_errors"]+=int(sha(path)!=row[key+"_sha256"])
            report[key+"_reconstruction_mismatch"]+=int(np.any(actual!=rebuilt,2).sum())
            if key=="master":
                aligned=actual[oy:oy+1080,ox:ox+1920]
                report["ep06_unmixed_core_rgb_mismatch"]+=int(np.any(aligned[native_core]!=b[native_core],1).sum())
            else:
                runtime.append(actual)
                edge=np.concatenate([actual[0],actual[-1],actual[:,0],actual[:,-1]])
                report["output_border_pixels_gt8"]+=int((edge.max(1)>8).sum())
                # Colored character exclusion, not a production key.
                rgb=actual.astype(int)
                report["runtime_red_character_pixels"]+=int(((rgb[:,:,0]>80)&(rgb[:,:,0]>rgb[:,:,1]+40)&(rgb[:,:,0]>rgb[:,:,2]+40)).sum())
                yy,xx=np.where(actual.max(2)>8)
                screen=np.column_stack([xx,yy])*screen_scale+screen_position
                screen_min=np.minimum(screen_min,screen.min(0));screen_max=np.maximum(screen_max,screen.max(0))
                outside=(screen[:,0]<0)|(screen[:,0]>=1920)|(screen[:,1]<80)|(screen[:,1]>=1080)
                allowed=(screen[:,0]<0)&(screen[:,1]>=80)&(screen[:,1]<1080)&allow_left
                report["layout_fire_clipped_pixels_gt8"]+=int(outside.sum())
                report["layout_authorized_left_overflow_pixels"]+=int(allowed.sum())
                report["layout_unapproved_clipped_pixels_gt8"]+=int((outside&~allowed).sum())
                ep6_coordinates=np.column_stack([xx,yy])/spec["runtime_scale"]-mb[:,2]
                face=(ep6_coordinates[:,0]>=500)&(ep6_coordinates[:,0]<=1390)&(ep6_coordinates[:,1]>=40)&(ep6_coordinates[:,1]<=1050)
                report["layout_face_clipped_pixels_gt8"]+=int((outside&face).sum())
                report["layout_fire_in_text_region_pixels_gt8"]+=int((screen[:,0]>=922).sum())
    assert [r["source_index"] for r in ledger]==list(range(spec["loop"]["first"],spec["loop"]["last"]+1))
    video=Path(delivery["video"])
    probe=json.loads(run(["ffprobe","-v","error","-count_frames","-show_streams","-of","json",video]))
    assert len(probe["streams"])==1 and probe["streams"][0]["codec_type"]=="video"
    stream=probe["streams"][0]
    assert stream["r_frame_rate"]=="24000/1001" and int(stream["nb_read_frames"])==len(ledger)
    decoded=run(["ffmpeg","-v","error","-i",video,"-f","rawvideo","-pix_fmt","rgb24","pipe:1"])
    decoded=np.frombuffer(decoded,np.uint8).reshape(-1,rh,rw,3)
    originals=np.asarray(runtime)
    diff=np.abs(decoded.astype(float)-originals.astype(float))
    report.update({"frames":len(ledger),"fps":stream["r_frame_rate"],"duration":len(ledger)/FPS,
                   "audio_streams":0,"encode_rgb_mae":float(diff.mean()),"encode_channel_error_p95":float(np.percentile(diff,95)),
                   "encode_psnr":float(10*np.log10(255**2/max(1e-12,np.mean(diff**2)))),
                   "loop_seam_mae":spec["loop"]["jump_mae"],"ordinary_frame_step_mae_median":spec["loop"]["native_step_mae_median"],
                   "screen_fire_union":[*screen_min.tolist(),*screen_max.tolist()]})
    import re
    scene=(ROOT/"scenes/vfx/events/ninja_slayer_event_naraku_event_vfx.tscn").read_text(encoding="utf-8")
    local=np.array(placement["video_position"])-[268,49]
    expected=[*local,*(local+placement["video_size"])]
    for name,value in zip(("left","top","right","bottom"),expected):
        actual=float(re.search(rf"offset_{name} = ([-0-9.]+)",scene)[1])
        assert abs(actual-value)<.00001, "Scene and static poster placement diverged"
    zero_keys=[k for k in report if k.endswith("errors") or k.endswith("mismatch") or k.endswith("pixels_gt8") or k=="runtime_red_character_pixels"]
    if allow_left: zero_keys.remove("layout_fire_clipped_pixels_gt8")
    report["passed"]=all(report[k]==0 for k in zero_keys) and report["encode_rgb_mae"]<1.5
    report["video_sha256"]=sha(video)
    save_json(OUT/"verification-report.json",report)
    print(json.dumps(report,indent=2))
    assert report["passed"], "Do not label an unaudited/clipped film accepted"
    manifest={"schema_version":1,"status":"candidate-user-approval-pending",
              "provenance_contract":"declared-multisource-raster-derivative",
              "video":"res://NinjaSlayer/videos/naraku_event.ogv","video_sha256":sha(video),
              "poster":"res://NinjaSlayer/images/events/naraku_event.png","poster_sha256":sha(Path(delivery["poster"])),
              "scene":"res://scenes/vfx/events/ninja_slayer_event_naraku_event_vfx.tscn",
              "fps":"24000/1001","frames":len(ledger),"duration_seconds":len(ledger)/FPS,"audio":False,
              "sources":{ep:{"video_name":Path(source_ledger[ep]["video"]).name,
                              "video_sha256":source_ledger[ep]["video_sha256"],
                              "first_frame":ledger[0][ep]["source_frame_zero_based"],
                              "last_frame":ledger[-1][ep]["source_frame_zero_based"],
                              "first_pts":ledger[0][ep]["pts"],"last_pts":ledger[-1][ep]["pts"],
                              "contribution":"HD face and inner fire" if ep=="ep06" else "same-phase complete outer fire"}
                         for ep in ("ep02","ep06")},
              "composition":spec,"audit":report,
              "rebuild":"tools/naraku-event/build_portrait.py: prepare, analyze, compose; then verify_portrait.py",
              "opencv_version":cv2.__version__,"numpy_version":np.__version__,
              "separate_source_ledger":"build/naraku-event/source-ledger.json",
              "separate_output_ledger":"build/naraku-event/frame-ledger.json"}
    save_json(ROOT/"NinjaSlayer/videos/naraku_event.source.json",manifest)


if __name__=="__main__": verify()
