import cv2,numpy as np,json,subprocess,sys
src,outp=sys.argv[1],sys.argv[2]
S=int(sys.argv[3]) if len(sys.argv)>3 else 552
EA,EB,EC=8,16,16; E=EA+EB+EC
cap=cv2.VideoCapture(src);F=[]
while True:
    r,f=cap.read()
    if not r:break
    F.append(f)
H,W=F[0].shape[:2]
Q={int(k):np.array(v,np.float32) for k,v in json.load(open('quads_final2.json')).items()}
quad=Q[max(Q)]
scr=np.zeros((H,W),np.float32); cv2.fillConvexPoly(scr,quad.astype(np.int32),1.0); scr=cv2.GaussianBlur(scr,(0,0),2)
yy,xx=np.mgrid[0:H,0:W].astype(np.float32)
rng=np.random.default_rng(7)
def ease(t): t=np.clip(t,0,1); return t*t*(3-2*t)
def lerp(a,b,t): return a+(b-a)*t
Mq=cv2.getPerspectiveTransform(np.float32([[0,0],[1,0],[1,1],[0,1]]),quad)
def onscreen(u,v):
    p=Mq@np.array([u,v,1.0]); return p[:2]/p[2]
c0=onscreen(0.60,0.40); breach=c0.copy()
c1=np.array([W*0.36,H*0.30])     # floats up-left out of the tablet, in front of it
c2=np.array([W*0.50,H*0.45])     # into the lens
# smooth noise fields for internal swirl
def noise_field(seed,scale=40):
    g=np.random.default_rng(seed).normal(0,1,(H//scale+3,W//scale+3)).astype(np.float32)
    return cv2.resize(g,(W+3*scale,H+3*scale),interpolation=cv2.INTER_CUBIC)
NX,NY=noise_field(1),noise_field(2)
shake=np.cumsum(rng.normal(0,1,(E,2)),0); shake-=shake.mean(0); shake/=np.abs(shake).max()
def sample(img,mx,my):
    return cv2.remap(img,mx.astype(np.float32),my.astype(np.float32),cv2.INTER_LINEAR,borderMode=cv2.BORDER_REFLECT)
def fx(base,e):
    base=base.astype(np.float32)
    if e<EA:
        k=ease((e+1)/EA); r=lerp(25,75,k); c=c0; A=0.26*k; vis=k
    elif e<EA+EB:
        k=(e-EA)/(EB-1); kk=ease(k); r=lerp(75,240,kk); c=lerp(c0,c1,kk)+np.array([0,-30])*np.sin(np.pi*k); A=0.28; vis=1
    else:
        k=(e-EA-EB)/(EC-1); r=240*np.exp(np.log(3400/240)*k**1.6); c=lerp(c1,c2,ease(k)); A=lerp(0.28,0.5,k); vis=1
    ph=e*0.45
    dx=xx-c[0]; dy=yy-c[1]; dist=np.sqrt(dx*dx+dy*dy)+1e-3; th=np.arctan2(dy,dx)
    reff=r*(1+0.06*np.sin(3*th+ph)+0.04*np.sin(5*th-1.6*ph)+0.04*np.sin(2*th+2.1*ph))
    d=dist/reff
    inside=np.clip(1-d*d,0,1)
    prof=np.sqrt(inside)                       # glass-ball profile: strong compression at rim
    mx=xx-dx*A*prof*vis; my=yy-dy*A*prof*vis
    # internal swirl
    o=int(e*3)%40
    sw=(3+0.012*r)*prof*vis
    mx+=NX[o:o+H,o:o+W]*sw; my+=NY[o:o+H,o:o+W]*sw
    # breach ripple on the tablet screen as the ghost pushes through
    if EA-3<=e<EA+10:
        tt=(e-(EA-3))/12; rr=np.hypot(xx-breach[0],yy-breach[1])
        wave=np.sin((rr-tt*420)/14)*np.exp(-((rr-tt*420)/60)**2)*(1-tt)*9*scr
        mx+= (xx-breach[0])/(rr+1)*wave; my+=(yy-breach[1])/(rr+1)*wave
    ca=lerp(0.0,0.05,ease((e-EA)/(EB+EC)))
    out=np.empty_like(base)
    for ch,s in zip(range(3),(1+ca,1.0,1-ca)):
        out[...,ch]=sample(base[...,ch],xx+(mx-xx)*s,yy+(my-yy)*s)
    # rim light + faint inner darkening so the invisible body reads
    rim=np.exp(-((d-0.97)/0.035)**2)*vis*(0.6+0.4*np.sin(th*2+ph))
    out+=rim[...,None]*np.array([38,36,30],np.float32)
    out*= (1-0.05*np.exp(-((d-0.85)/0.10)**2)*vis)[...,None]
    # screen flash at the breach
    if EA-2<=e<EA+8:
        fl=0.35*np.exp(-((e-EA)/3.0)**2)
        out+=(scr*fl)[...,None]*np.array([240,245,220],np.float32)
    if e>=EA+EB:
        k=(e-EA-EB)/(EC-1); n=7; acc=out.copy()
        for j in range(1,n+1):
            s=1+0.04*k*j
            M=np.float32([[s,0,c[0]*(1-s)],[0,s,c[1]*(1-s)]])
            acc+=cv2.warpAffine(out,M,(W,H),borderMode=cv2.BORDER_REFLECT)
        out=lerp(out,acc/(n+1),min(1,k*1.5))
        fk=ease((k-0.5)/0.5)
        out=lerp(out,np.array([235,238,226],np.float32)*np.ones_like(out),fk*0.9)
    amp=lerp(0,16,ease((e-EA+2)/(EB+EC-4)))
    sx,sy=shake[e]*amp; sc=1+0.035*ease((e-EA+2)/12)
    M=np.float32([[sc,0,W/2*(1-sc)+sx],[0,sc,H/2*(1-sc)+sy]])
    out=cv2.warpAffine(out,M,(W,H),borderMode=cv2.BORDER_REFLECT)
    return np.clip(out,0,255).astype(np.uint8)
p=subprocess.Popen(['ffmpeg','-v','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{W}x{H}','-r','24','-i','-',
    '-c:v','libx264','-crf','16','-preset','slow','-pix_fmt','yuv420p','-movflags','+faststart',outp],stdin=subprocess.PIPE)
for i in range(S): p.stdin.write(F[i].tobytes())
for e in range(E): p.stdin.write(fx(F[min(S+e,len(F)-1)],e).tobytes())
p.stdin.close();p.wait();print('frames',S+E)
