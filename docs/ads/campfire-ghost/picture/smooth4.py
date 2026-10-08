import json,numpy as np
from scipy.ndimage import gaussian_filter1d
Q={int(k):np.array(v) for k,v in json.load(open('quads4.json')).items()}
ks=sorted(Q); arr=np.array([Q[k] for k in ks])
a1=gaussian_filter1d(arr,1.2,axis=0,mode='nearest')
a2=gaussian_filter1d(arr,2.5,axis=0,mode='nearest')
out={}
for j,k in enumerate(ks):
    w=np.clip((k-306)/10,0,1)   # heavier smoothing in early turn
    out[k]=(a2[j]*(1-w)+a1[j]*w).tolist()
json.dump(out,open('quads_final2.json','w'))
prev=None
for k in ks:
    c=np.array(out[k])
    if prev is not None:
        d=np.linalg.norm(c-prev,axis=1); acc=None
        print(k,d.round(1).tolist()) if k<320 or d.max()>14 else None
    prev=c
