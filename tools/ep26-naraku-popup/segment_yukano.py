"""SAM2 target mask for the requested final 36 native frames of Yukano's shot."""
import sys,json
from pathlib import Path
import numpy as np
from PIL import Image
import torch
from build_popup import WORK,png,save_json
runtime=Path.home()/'.codex/runtimes/source-pixel-cutout'
sys.path.insert(0,str(runtime/'src/sam2'))
from sam2.build_sam import build_sam2
from sam2.sam2_image_predictor import SAM2ImagePredictor
model=runtime/'models/sam2/sam2.1-large/sam2.1_hiera_large.pt'
p=SAM2ImagePredictor(build_sam2('configs/sam2.1/sam2.1_hiera_l.yaml',str(model),device='cuda'))
report={}
for n in range(42,78):
    output=WORK/'yukano-masks'/f'{n:04}.png'
    if output.exists():continue
    s=np.array(Image.open(WORK/'ep15-source'/f'{n:04}.png').convert('RGB'))
    positive=[(800,880),(520,800),(970,470),(1250,710),(1770,770),(670,100),(1000,200)]
    if n<69:positive += [(200,500),(390,540)]
    else:positive += [(230,380),(290,150)]
    negative=[(30,60),(1790,100),(1740,450),(100,1000),(1700,990)]
    points=positive+negative
    with torch.inference_mode(),torch.autocast('cuda',dtype=torch.bfloat16):
        p.set_image(s)
        masks,scores,_=p.predict(point_coords=np.array(points),point_labels=np.array([1]*len(positive)+[0]*len(negative)),multimask_output=True)
    selected=int(scores.argmax())
    for k,m in enumerate(masks):png(WORK/'yukano-masks/raw'/f'{n:04}-{k}.png',m.astype(np.uint8)*255)
    png(output,masks[selected].astype(np.uint8)*255)
    report[str(n)]={'points':points,'labels':[1]*len(positive)+[0]*len(negative),'scores':scores.tolist(),'selected':selected}
    save_json(WORK/'yukano-sam.json',report)
    print(n,scores.tolist(),flush=True)
