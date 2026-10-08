import cv2,numpy as np,json,subprocess,sys
mode=sys.argv[1]; outp=sys.argv[2]
Q={int(k):np.array(v,np.float32) for k,v in json.load(open('quads_final2.json')).items()}
cap=cv2.VideoCapture('camp_fixed.mp4');camp=[]
while True:
    r,f=cap.read()
    if not r:break
    camp.append(f)
g=cv2.VideoCapture('ghost.mp4');ghost=[]
while True:
    r,f=g.read()
    if not r:break
    ghost.append(f)
ghost=[f for k,f in enumerate(ghost) if k%5!=3]  # remove 3:2 pulldown duplicates -> true 24fps
H,W=camp[0].shape[:2]; NC=len(camp); NG=len(ghost); GFPS=24.0; FPS=24.0
# crop ghost to tablet aspect (~1.43)
gh,gw=ghost[0].shape[:2]; cw=int(round(gh*1.43)); x0=(gw-cw)//2
ghost=[f[:,x0:x0+cw] for f in ghost]
T0=min(Q)  # first frame screen visible
STILL=10
REF_LUM=236.0
if mode=='continuous':
    total=max(NC,T0+NG)+12
    def gidx(i): return int(np.clip(i-T0,0,NG-1))
else:
    total=NC+NG+12
    def gidx(i): return STILL if i<NC else int(np.clip(i-NC,0,NG-1))
ff=subprocess.Popen(['ffmpeg','-v','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{W}x{H}','-r','24','-i','-',
    '-c:v','libx264','-crf','16','-preset','slow','-pix_fmt','yuv420p','-movflags','+faststart',outp],stdin=subprocess.PIPE)
def smooth(x,a,b): t=np.clip((x-a)/(b-a),0,1); return t*t*(3-2*t)
for i in range(total):
    ci=min(i,NC-1); plate=camp[ci].astype(np.float32)
    if ci in Q:
        quad=Q[ci]; G=ghost[gidx(i)].astype(np.float32)
        G=np.clip(G*1.12+8,0,255)
        gh_,gw_=G.shape[:2]
        src=np.float32([[0,0],[gw_-1,0],[gw_-1,gh_-1],[0,gh_-1]])
        M=cv2.getPerspectiveTransform(src,quad)
        # slightly oversize so edges are covered
        c=quad.mean(0); qbig=(quad-c)*1.02+c; M=cv2.getPerspectiveTransform(src,qbig)
        warped=cv2.warpPerspective(G,M,(W,H),flags=cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
        warped=cv2.GaussianBlur(warped,(0,0),0.6)
        poly=np.zeros((H,W),np.uint8); cv2.fillConvexPoly(poly,((quad-c)*1.04+c).astype(np.int32),1)
        poly=cv2.GaussianBlur(poly.astype(np.float32),(0,0),1.5)
        B,Gc,R=plate[...,0],plate[...,1],plate[...,2]
        inner=np.zeros((H,W),np.uint8); cv2.fillConvexPoly(inner,((quad-c)*0.85+c).astype(np.int32),1)
        sel=(inner>0)&(B>120)&(B-R>5)
        scr=np.median(plate[sel],0) if sel.sum()>50 else np.array([238,237,207],np.float32)
        lum=np.minimum(B,Gc); mlum=float(min(scr[0],scr[1]))
        a_l=np.clip((lum-30)/(0.85*mlum-30),0,1)
        a_h=smooth(B-R,-8,0.5*max(scr[0]-scr[2],10))
        alpha=(a_l*a_h*poly)*float(np.clip((ci-295)/9,0,1))
        alpha=np.minimum(cv2.dilate(alpha,np.ones((3,3),np.uint8)),poly)
        alpha=cv2.GaussianBlur(alpha,(0,0),0.6)[...,None]
        # local lighting from plate screen (gradient, viewing-angle dimming)
        light=np.clip(cv2.GaussianBlur(lum,(0,0),6)/REF_LUM,0.45,1.08)[...,None]
        content=warped*light
        # difference-matte composite: remove screen color, add content
        out=plate+alpha*(content-scr[None,None,:])
        # fade-in during edge-on first frames
        out=np.clip(out,0,255)
    else: out=plate
    ff.stdin.write(out.astype(np.uint8).tobytes())
ff.stdin.close(); ff.wait(); print(mode,total,'frames')
