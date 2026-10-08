import cv2,numpy as np,subprocess
W,H=1280,720
def read(fn):
    c=cv2.VideoCapture(fn);o=[]
    while True:
        r,f=c.read()
        if not r:break
        o.append(f)
    return o
A_=read('v_exit2.mp4'); N=read('night.mp4')
def fit(f):  # 1344x768 -> crop to 16:9 -> 1280x720
    h,w=f.shape[:2]; ch=int(round(w*9/16)); y=(h-ch)//2
    return cv2.resize(f[y:y+ch],(W,H),interpolation=cv2.INTER_AREA)
yy,xx=np.mgrid[0:H,0:W].astype(np.float32)
def ease(t): t=np.clip(t,0,1); return t*t*(3-2*t)
def lerp(a,b,t): return a+(b-a)*t
def noise_field(seed,scale=40):
    g=np.random.default_rng(seed).normal(0,1,(H//scale+3,W//scale+3)).astype(np.float32)
    return cv2.resize(g,(W+3*scale,H+3*scale),interpolation=cv2.INTER_CUBIC)
NX,NY=noise_field(1),noise_field(2)
rng=np.random.default_rng(11); EN=22
shake=np.cumsum(rng.normal(0,1,(EN,2)),0); shake-=shake.mean(0); shake/=np.abs(shake).max()
cS=np.array([W*0.5,H*0.45]); cT=np.array([W*0.40,H*0.52])   # lands over the bed
def entry(base,e):
    base=base.astype(np.float32)
    if e<14:
        k=e/13; r=3200*np.exp(np.log(110/3200)*(1-(1-k)**1.6)); c=lerp(cS,cT,ease(k)); Am=lerp(0.5,0.26,k); vis=1
    else:
        k=(e-14)/(EN-15); r=lerp(110,70,k); c=cT+np.array([-20,10])*k; Am=0.26; vis=1-ease(k)
    ph=e*0.45+3
    dx=xx-c[0]; dy=yy-c[1]; dist=np.sqrt(dx*dx+dy*dy)+1e-3; th=np.arctan2(dy,dx)
    reff=r*(1+0.06*np.sin(3*th+ph)+0.04*np.sin(5*th-1.6*ph)+0.04*np.sin(2*th+2.1*ph))
    d=dist/reff; prof=np.sqrt(np.clip(1-d*d,0,1))
    mx=xx-dx*Am*prof*vis; my=yy-dy*Am*prof*vis
    o=int(e*3)%40; sw=(3+0.012*r)*prof*vis
    mx+=NX[o:o+H,o:o+W]*sw; my+=NY[o:o+H,o:o+W]*sw
    ca=lerp(0.05,0.0,ease(e/12)); out=np.empty_like(base)
    for ch,s in zip(range(3),(1+ca,1.0,1-ca)):
        out[...,ch]=cv2.remap(base[...,ch],(xx+(mx-xx)*s).astype(np.float32),(yy+(my-yy)*s).astype(np.float32),cv2.INTER_LINEAR,borderMode=cv2.BORDER_REFLECT)
    rim=np.exp(-((d-0.97)/0.035)**2)*vis*(0.6+0.4*np.sin(th*2+ph))
    out+=rim[...,None]*np.array([38,36,30],np.float32)
    out*=(1-0.05*np.exp(-((d-0.85)/0.10)**2)*vis)[...,None]
    k=min(1,e/10)
    if k<1:
        n=7; acc=out.copy()
        for j in range(1,n+1):
            s=1+0.04*(1-k)*j
            M=np.float32([[s,0,c[0]*(1-s)],[0,s,c[1]*(1-s)]]); acc+=cv2.warpAffine(out,M,(W,H),borderMode=cv2.BORDER_REFLECT)
        out=lerp(acc/(n+1),out,ease(k))
    fk=1-ease(e/7)
    out=lerp(out,np.array([235,238,226],np.float32)*np.ones_like(out),fk*0.9)
    amp=lerp(16,0,ease(e/16)); sx,sy=shake[e]*amp; sc=1+0.035*(1-ease(e/14))
    M=np.float32([[sc,0,W/2*(1-sc)+sx],[0,sc,H/2*(1-sc)+sy]])
    out=cv2.warpAffine(out,M,(W,H),borderMode=cv2.BORDER_REFLECT)
    return np.clip(out,0,255).astype(np.uint8)
p=subprocess.Popen(['ffmpeg','-v','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{W}x{H}','-r','24','-i','-',
    '-c:v','libx264','-crf','16','-preset','slow','-pix_fmt','yuv420p','-movflags','+faststart','v_seq1.mp4'],stdin=subprocess.PIPE)
for f in A_: p.stdin.write(fit(f).tobytes())
for i,f in enumerate(N):
    p.stdin.write((entry(f,i) if i<EN else f).tobytes())
p.stdin.close();p.wait();print(len(A_),len(N),len(A_)+len(N))
