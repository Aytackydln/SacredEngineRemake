"""Register supplied screenshots against terminal terrain; export equal-scale door crops.
Run from repository root. Requires numpy, opencv-python-headless only.
No game input or window capture is used. Models do not participate in registration.
"""
import json, math
from pathlib import Path
import cv2
import numpy as np


root = Path('docs/_research/doors')
references = Path(r'C:\Users\Aytac\Pictures\Screenshots\Sacred\Doors')
rows = [('1715 3397.png','waldburg','closed',[1715,3397]),
        ('1713 3396 open.png','waldburg','open',[1715,3397]),
        ('3050 2725.png','cemetery','closed',[3050,2725]),
        ('3378 2522.png','tower','closed',[3378,2522]),
        ('3425 2583.png','bellevue','closed',[3425,2583]),
        ('4555 990.png','dungeon','closed',[4555,990]),
        ('4557 989 open.png','dungeon','open',[4555,990])]
evidence = json.loads((root/'evidence.json').read_text())
sift=cv2.SIFT_create(nfeatures=7000)
results=[]
(root/'comparisons').mkdir(exist_ok=True)
for filename,scene,state,center in rows:
    ref=cv2.imread(str(references/filename))
    folder=Path(f'_scratch/doors-{scene}-{state}')
    terrain=cv2.imread(str(folder/'world-day.bmp'))
    rendered=cv2.imread(str(folder/'world-models.bmp'))
    kb,db=sift.detectAndCompute(cv2.cvtColor(terrain,cv2.COLOR_BGR2GRAY),None)
    best=None
    for scale in [1.0,.625,.75,1.125]:
        small=cv2.resize(ref,None,fx=scale,fy=scale,interpolation=cv2.INTER_AREA)
        ka,da=sift.detectAndCompute(cv2.cvtColor(small,cv2.COLOR_BGR2GRAY),None)
        matches=[a for a,b in cv2.BFMatcher().knnMatch(da,db,k=2) if a.distance<.7*b.distance]
        src=np.float32([ka[m.queryIdx].pt for m in matches])/scale
        dst=np.float32([kb[m.trainIdx].pt for m in matches])
        matrix,mask=cv2.estimateAffinePartial2D(src,dst,method=cv2.RANSAC,ransacReprojThreshold=2)
        if matrix is not None and (best is None or mask.sum()>best[1].sum()): best=(matrix,mask,src,dst)
    matrix,mask,src,dst=best
    if matrix is None or mask.sum()<30: raise RuntimeError(f'Insufficient alignment: {filename}')
    error=np.linalg.norm(cv2.transform(src[None],matrix)[0]-dst,axis=1)[mask.ravel()!=0]
    aligned=cv2.warpAffine(ref,matrix,(1600,900))
    # Isolate exactly the pixels contributed by the software model pass. Drawing
    # that footprint over the registered original makes wall/column occlusion
    # errors visible even when lighting and texture filtering differ.
    model_delta=np.max(cv2.absdiff(rendered,terrain),axis=2)
    model_mask=(model_delta>8).astype(np.uint8)*255
    model_boundary=cv2.morphologyEx(model_mask,cv2.MORPH_GRADIENT,np.ones((3,3),np.uint8))
    result=dict(reference=filename,scene=scene,state=state,referenceToTerminal=matrix.tolist(),inliers=int(mask.sum()),medianTerrainErrorPixels=float(np.median(error)))
    results.append(result)
    cards=[]
    for door in [e for e in evidence if e['scene']==center]:
        x,y=door['position']; cx,cy=center
        px=800+(x-y-cx+cy)*24
        py=450+(x+y-cx-cy)*12
        # Identical crop in registered reference and software frame, with room for swung leaves.
        box=(max(0,int(px-155)),max(0,int(py-200)),min(1600,int(px+155)),min(900,int(py+55)))
        if box[2]-box[0]<60 or box[3]-box[1]<60: continue
        left,top,right,bottom=box
        footprint=aligned.copy()
        coverage=model_mask!=0
        footprint[coverage]=(footprint[coverage].astype(np.uint16)*2//3+
                             np.array([85,28,85],dtype=np.uint16)).astype(np.uint8)
        footprint[model_boundary!=0]=(255,0,255)
        cols=[aligned[top:bottom,left:right],rendered[top:bottom,left:right],
              footprint[top:bottom,left:right]]
        labels=['Original (registered)',f'Terminal {state}','Terminal model footprint on OG']
        if scene=='bellevue':
            cols.append(cv2.imread('_scratch/doors-bellevue-open/world-models.bmp')[top:bottom,left:right])
            labels.append('Terminal open')
        w,h=right-left,bottom-top
        card=np.full((h+44,w*len(cols),3),25,dtype=np.uint8)

        cv2.putText(card,f"{door['TypeId']} {door['ModelName']} @ {x:.4f}, {y:.4f}",(5,15),cv2.FONT_HERSHEY_SIMPLEX,.4,(255,255,255),1)
        for i,(col,label) in enumerate(zip(cols,labels)):
            cv2.putText(card,label,(i*w+5,35),cv2.FONT_HERSHEY_SIMPLEX,.4,(255,255,255),1)
            card[44:,i*w:(i+1)*w]=col
        cards.append(card)
    gallery=np.full((sum(c.shape[0] for c in cards),max(c.shape[1] for c in cards),3),25,dtype=np.uint8)
    offset=0
    for card in cards: gallery[offset:offset+card.shape[0],:card.shape[1]]=card; offset+=card.shape[0]
    cv2.imwrite(str(root/'comparisons'/f'{scene}-{state}.png'),gallery)
    print(filename, 'inliers',result['inliers'],'median terrain residual',result['medianTerrainErrorPixels'])
(root/'alignment.json').write_text(json.dumps(results,indent=2))


