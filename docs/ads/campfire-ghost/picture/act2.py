"""Act 2: editor shrinks into the kid's tablet, the campfire rewinds to the kid, he melts into the ghost,
the ghost rushes the lens and wipes to the site's dark background, logo + tagline."""
import cv2,numpy as np,json,subprocess,sys,math,os
sys.path.insert(0,'ad'); os.environ['AD_BUILD']='ad/build'
from screenfx import comp_screen
import ending as END
OW,OH=1280,720
def ease(t): t=float(np.clip(t,0,1)); return t*t*(3-2*t)
def ease_in(t): t=float(np.clip(t,0,1)); return t*t*t
def seg(t,a,b): return float(np.clip((t-a)/(b-a),0,1))
def lerp(a,b,t): return a+(b-a)*t
camp=[]; cap=cv2.VideoCapture('camp_fixed.mp4')
while True:
    r,f=cap.read()
    if not r: break
    camp.append(f)
Q={int(k):np.array(v,np.float32) for k,v in json.load(open('quads_final2.json')).items()}
ED=cv2.imread('editor_final.png'); CONTENT=ED[:,153:2727]          # 1.43 aspect, as the tablet screen
edlast=None; cap=cv2.VideoCapture('editor_scene.mp4')
while True:
    r,f=cap.read()
    if not r: break
    edlast=f
def fit(f):
    return cv2.resize(f[6:762],(OW,OH),interpolation=cv2.INTER_AREA)
S=OW/1344.0
def qout(q): return (q-np.array([0,6],np.float32))*S
yy,xx=np.mgrid[0:OH,0:OW].astype(np.float32)
frames=[]
def emit(img): frames.append(img)
# ---------- 1. flight: the editor shrinks into the tablet (campfire frame 438) ----------
FL=16; qend=qout(Q[438]); qstart=np.float32([[68,-40],[1212,-40],[1212,760],[68,760]])
keyed438=fit(comp_screen(camp[438],Q[438],CONTENT))
ch,cw=CONTENT.shape[:2]; src=np.float32([[0,0],[cw-1,0],[cw-1,ch-1],[0,ch-1]])
for e in range(FL):
    k=ease(e/(FL-1)); q=lerp(qstart,qend,k)
    bg=fit(camp[438]).astype(np.float32)
    bg=lerp(edlast.astype(np.float32),bg,ease(seg(e,0,6)))
    M=cv2.getPerspectiveTransform(src,q.astype(np.float32))
    w=cv2.warpPerspective(CONTENT,M,(OW,OH),flags=cv2.INTER_AREA)
    m=cv2.warpPerspective(np.ones((ch,cw),np.float32),M,(OW,OH))[...,None]
    out=bg*(1-m)+w*m
    out=lerp(out,keyed438.astype(np.float32),ease(seg(e,FL-5,FL-1)))
    emit(np.clip(out,0,255).astype(np.uint8))
# ---------- 2. rewind the campfire to the kid (438 -> 279), editor on the screen ----------
RW=72; A,Bf=438,279; prev=float(A)
for e in range(1,RW+1):
    fi=lerp(A,Bf,1-(1-e/RW)**2.2)
    def plate(j):
        pl=camp[j]
        if j in Q: pl=comp_screen(pl,Q[j],CONTENT,fade=float(np.clip((j-295)/9,0,1)))
        return pl.astype(np.float32)
    if prev-fi>1.2:      # fast: motion-blur across the frames we pass
        lo,hi=int(np.ceil(fi)),int(np.floor(prev)); img=np.mean([plate(j) for j in range(lo,hi+1)],0)
    else:                # slow: blend between neighbours so the end of the rewind doesn't stutter
        f0=int(np.floor(fi)); w=fi-f0; img=plate(f0)*(1-w)+plate(min(f0+1,A))*w
    emit(fit(np.clip(img,0,255).astype(np.uint8))); prev=fi
# ---------- 3. the kid melts into the ghost ----------
P=camp[Bf].astype(np.float32); M0=np.load('kidmask.npy').astype(np.float32)
H,W=M0.shape
dil=cv2.dilate((M0>0.05).astype(np.uint8),np.ones((25,25),np.uint8))
small=cv2.resize(camp[Bf],(W//4,H//4)); ms=cv2.resize(dil,(W//4,H//4),interpolation=cv2.INTER_NEAREST)
BG=cv2.inpaint(small,ms,9,cv2.INPAINT_TELEA); BG=cv2.GaussianBlur(cv2.resize(BG,(W,H),interpolation=cv2.INTER_CUBIC),(0,0),9).astype(np.float32)
BG=np.where(dil[...,None]>0,BG,P)
rng=np.random.default_rng(3)
dr=cv2.resize(rng.random((1,10)).astype(np.float32),(W,1),interpolation=cv2.INTER_CUBIC)[0]
dr2=cv2.GaussianBlur(cv2.resize(rng.random((1,34)).astype(np.float32),(W,1),interpolation=cv2.INTER_CUBIC),(0,0),6)[0]
drip=0.65*dr+0.35*dr2
Yg,Xg=np.mgrid[0:H,0:W].astype(np.float32)
lum=P.mean(2,keepdims=True)
GH=np.concatenate([lum*0.85+95,lum*0.85+80,lum*0.75+55],2)   # cool, pale, glowing
def ghost_blob(img,c,r,A,ph,vis):
    dx=xx-c[0]; dy=yy-c[1]; dist=np.sqrt(dx*dx+dy*dy)+1e-3; th=np.arctan2(dy,dx)
    reff=r*(1+0.06*np.sin(3*th+ph)+0.04*np.sin(5*th-1.6*ph)+0.04*np.sin(2*th+2.1*ph))
    d=dist/reff; prof=np.sqrt(np.clip(1-d*d,0,1))
    mx=(xx-dx*A*prof*vis).astype(np.float32); my=(yy-dy*A*prof*vis).astype(np.float32)
    out=np.empty_like(img)
    for ch in range(3): out[...,ch]=cv2.remap(img[...,ch],mx,my,cv2.INTER_LINEAR,borderMode=cv2.BORDER_REFLECT)
    rim=np.exp(-((d-0.97)/0.035)**2)*vis*(0.6+0.4*np.sin(th*2+ph))
    out+=rim[...,None]*np.array([38,36,30],np.float32)
    return out,prof,d
ML=60
for e in range(ML):
    t=e/(ML-1)
    gk=ease(seg(t,0,0.35)); s=ease_in(seg(t,0.12,0.9))
    f=1+3.6*s*(0.5+drip[None,:]*0.9)
    ys=H-(H-Yg)*f
    xs=Xg+7*s*np.sin(Yg/23+e*0.5)
    xs=xs.astype(np.float32); ys=ys.astype(np.float32)
    per=cv2.remap(lerp(P,GH,gk*0.85).astype(np.float32),xs,ys,cv2.INTER_LINEAR,borderMode=cv2.BORDER_CONSTANT)
    a=cv2.remap(M0,xs,ys,cv2.INTER_LINEAR,borderMode=cv2.BORDER_CONSTANT,borderValue=0)
    a=cv2.GaussianBlur(a,(0,0),1.0+3*s)*(1-0.6*ease(seg(t,0.35,1.0)))
    glow=cv2.GaussianBlur(a,(0,0),9)*gk*0.35
    out=BG*(1-a[...,None])+per*a[...,None]+glow[...,None]*np.array([255,235,190],np.float32)*0.5
    o=fit(np.clip(out,0,255).astype(np.uint8)).astype(np.float32)
    if t>0.62:   # the invisible ghost lifts out of the puddle
        k=ease(seg(t,0.62,1.0)); c=(lerp(780,700,k),lerp(700,380,k)); r=lerp(40,150,k)
        o,_,_=ghost_blob(o,c,r,0.3*k,e*0.45,k)
    emit(np.clip(o,0,255).astype(np.uint8))
last=frames[-1].astype(np.float32)
# ---------- 4. the ghost rushes the lens; inside it, the site's dark background ----------
RU=18; T_LOGO_ABS=0.0
for e in range(RU):
    k=e/(RU-1); r=150*np.exp(np.log(3200/150)*k**1.5); c=(lerp(700,640,k),lerp(380,360,k))
    o,prof,d=ghost_blob(last.copy(),c,r,lerp(0.3,0.5,k),(ML+e)*0.45,1.0)
    dark=END.bg_frame(e/24).astype(np.float32)
    inside=np.clip((1-d)*4,0,1)*ease(seg(k,0.15,0.9))
    inside=np.maximum(inside,ease(seg(k,0.7,1.0)))
    o=o*(1-inside[...,None])+dark*inside[...,None]
    amp=12*math.sin(math.pi*k); M=np.float32([[1.02,0,-12.8+amp*0.6],[0,1.02,-7.2+amp*0.3]])
    o=cv2.warpAffine(o,M,(OW,OH),borderMode=cv2.BORDER_REFLECT)
    emit(np.clip(o,0,255).astype(np.uint8))
# ---------- 5. logo build + tagline ----------
LG=int(sys.argv[2]) if len(sys.argv)>2 else 112
for e in range(LG):
    emit(END.ending((RU+e)/24,e/24))
out=sys.argv[1]
p=subprocess.Popen(['ffmpeg','-v','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{OW}x{OH}','-r','24','-i','-','-c:v','libx264','-crf','12','-preset','medium','-pix_fmt','yuv420p',out],stdin=subprocess.PIPE)
for f in frames: p.stdin.write(f.tobytes())
p.stdin.close(); p.wait(); print('act2 frames',len(frames))
