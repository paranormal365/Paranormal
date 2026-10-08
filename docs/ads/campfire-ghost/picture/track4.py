import cv2,numpy as np,json
cap=cv2.VideoCapture('camp.mp4');frames=[]
while True:
    r,f=cap.read()
    if not r:break
    frames.append(f)
N=len(frames)
q2=json.load(open('quads2.json'))
def mask(f):
    b,g,r=[f[...,i].astype(int) for i in range(3)]
    return ((b>140)&(g>140)&(b-r>8)&(np.abs(b-g)<40)).astype(np.uint8)
def line_from(p,q):
    d=q-p; a,b=-d[1],d[0]; n=np.hypot(a,b); a,b=a/n,b/n; return np.array([a,b,-(a*p[0]+b*p[1])])
def inter(l1,l2):
    a1,b1,c1=l1;a2,b2,c2=l2;d=a1*b2-a2*b1
    return np.array([(b1*c2-b2*c1)/d,(c1*a2-c2*a1)/d])
def corners(L): return np.array([inter(L[3],L[0]),inter(L[0],L[1]),inter(L[1],L[2]),inter(L[2],L[3])])
def lines_of(C): return [line_from(C[k],C[(k+1)%4]) for k in range(4)]  # top,right,bottom,left
C=np.array(q2[str(N-1)]['quad']); out={N-1:C.tolist()}; prevc=None; info={}
for i in range(N-2,285,-1):
    m=mask(frames[i]); m=cv2.morphologyEx(m,cv2.MORPH_OPEN,np.ones((3,3),np.uint8))
    n,lab,st,cen=cv2.connectedComponentsWithStats(m)
    c_prev=C.mean(0)
    cands=[k for k in range(1,n) if st[k,4]>200]
    if not cands: break
    k=min(cands,key=lambda k:np.hypot(*(cen[k]-c_prev)))
    if np.hypot(*(cen[k]-c_prev))>80: break
    comp=(lab==k).astype(np.uint8)
    cnt=max(cv2.findContours(comp,cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_NONE)[0],key=cv2.contourArea).reshape(-1,2).astype(float)
    # predicted: shift previous quad by centroid motion
    Cp=C+(cen[k]-c_prev)*0.0
    L=lines_of(Cp); newL=[]; cnts=[]
    tol=max(6,0.06*np.sqrt(st[k,4]))
    s=max(3,len(cnt)//60)
    d1=np.roll(cnt,-s,0)-cnt; d2=cnt-np.roll(cnt,s,0)
    cosv=(d1*d2).sum(1)/(np.linalg.norm(d1,axis=1)*np.linalg.norm(d2,axis=1)+1e-9)
    P=cnt[cosv>0.98]
    for j in range(4):
        # iterate twice to allow line to move
        l=L[j]
        for it in range(3):
            dist=np.abs(P@l[:2]+l[2]); sel=P[dist<tol*(2.0 if it==0 else 1.0)]
            # restrict to segment span (between neighbor corners, shrunk)
            a,b=Cp[j],Cp[(j+1)%4]; dvec=b-a; t=((sel-a)@dvec)/(dvec@dvec)
            sel=sel[(t>0.08)&(t<0.92)]
            if len(sel)<8: break
            if len(sel)<40:
                # offset-only fit, keep previous direction
                l=np.array([L[j][0],L[j][1],-np.median(sel@L[j][:2])])
            else:
                vx,vy,x0,y0=cv2.fitLine(sel.astype(np.float32),cv2.DIST_HUBER,0,0.01,0.01).ravel()
                l=np.array([-vy,vx,-(-vy*x0+vx*y0)])
                if l[:2]@L[j][:2]<0: l=-l
        newL.append(l); cnts.append(len(sel))
    Cn=corners(newL)
    area=cv2.contourArea(Cn.astype(np.float32)); r=area/st[k,4]
    good=cv2.isContourConvex(Cn.astype(np.float32)) and 0.9<r<1.5 and np.abs(Cn-C).max()<60
    info[i]=(round(r,2),cnts,good)
    if good: C=Cn
    else: C=C+(cen[k]-c_prev)  # translate
    out[i]=C.tolist()
json.dump(out,open('quads4.json','w'))
for i in sorted(info):
    if i<335 or not info[i][2]: print(i,info[i],np.round(out[i]).astype(int).tolist())
