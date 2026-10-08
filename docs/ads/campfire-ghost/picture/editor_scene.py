"""Editor beat: the night clip pulls back into the IsHaunted video editor (real UI, from the help screenshots),
the playhead scrubs back to the ghost, a marker drops, a callout box goes round the ghost."""
import cv2, numpy as np, cairo, math, subprocess, sys
FPS=24; OW,OH=1280,720
def ease(t): t=min(1,max(0,t)); return t*t*(3-2*t)
def ease_out(t): t=min(1,max(0,t)); return 1-(1-t)**3
def ease_out_back(t,s=1.7): t=min(1,max(0,t)); t-=1; return t*t*((s+1)*t+s)+1
def seg(t,a,b): return min(1,max(0,(t-a)/(b-a)))
def lerp(a,b,t): return a+(b-a)*t

base=cv2.imread('ed/clip-properties.png'); EH,EW=base.shape[:2]
# ---- static fixes on the screenshot ----
B=base.copy()
# erase the baked playhead (x 332-356)
B[1626:1700,332:351]=base[1626:1700,410:429]; B[1626:1656,351:356]=base[1626:1656,429:434]
B[1700:1800,332:342]=(23,16,12)[::-1] if False else np.array([23,16,12])[::-1]*0+np.array([23,16,12],np.uint8)[::-1]
B[1700:1800,332:342]=np.array([12,16,23][::-1],np.uint8)
B[1702:1800,342:346]=np.array([44,38,88][::-1],np.uint8)
B[1706:1800,346:348]=np.array([162,143,251][::-1],np.uint8)
B[1708:1800,348:357]=np.array([28,42,58][::-1],np.uint8)
B[1700:1702,342:357]=np.array([12,16,23][::-1],np.uint8)
B[1702:1706,346:357]=np.array([44,38,88][::-1],np.uint8)
B[1706:1708,348:357]=np.array([162,143,251][::-1],np.uint8)
# label "Timeline Preview" as an overlay (white text over the old clip)
lab=base[496:536,255:480].astype(np.float32); lab_a=np.clip((lab.max(2)-170)/60,0,1)
# file name + timecode areas cleared (redrawn by cairo)
B[660:708,2118:2420]=np.array([21,28,40][::-1],np.uint8)
B[1380:1420,410:770]=np.array([21,28,40][::-1],np.uint8)
PX,PY,PW,PH=236,478,1544,868          # preview rect
T0X,PPS=346.0,288.5                  # timeline: x of 0.0 s, pixels per second
MARK_BTN=(2432,1602); CALL_BTN=(2243,1602)
night=[]; cap=cv2.VideoCapture('night.mp4')
while True:
    r,f=cap.read()
    if not r: break
    night.append(f)
NF=len(night)
# callout box in night-frame coords (around the ghost streak in frame 121)
CB=(400,120,1120,430)
def to_prev(x,y): return PX+x*PW/1280, PY+y*PH/720
FONT='Public Sans'
def cairo_ctx(img):
    bgra=cv2.cvtColor(img,cv2.COLOR_BGR2BGRA)
    s=cairo.ImageSurface.create_for_data(bgra,cairo.FORMAT_ARGB32,img.shape[1],img.shape[0],img.shape[1]*4)
    return bgra,s,cairo.Context(s)
def rrect(c,x,y,w,h,r):
    c.new_sub_path(); c.arc(x+w-r,y+r,r,-math.pi/2,0); c.arc(x+w-r,y+h-r,r,0,math.pi/2); c.arc(x+r,y+h-r,r,math.pi/2,math.pi); c.arc(x+r,y+r,r,math.pi,1.5*math.pi); c.close_path()
def cursor(c,x,y,a=1.0,press=0.0):
    s=1.0-0.12*press
    c.save(); c.translate(x,y); c.scale(2.1*s,2.1*s)
    c.move_to(0,0); c.line_to(0,17); c.line_to(4.2,13.2); c.line_to(7.2,19.6); c.line_to(9.6,18.6); c.line_to(6.8,12.4); c.line_to(12.2,12.2); c.close_path()
    c.set_source_rgba(0,0,0,a); c.fill_preserve(); c.set_source_rgba(1,1,1,a); c.set_line_width(1.3); c.stroke(); c.restore()
def editor_image(nf, ph_t, marker_p, callout_p, label_p, overlay_a, cur=None, press=0.0, btn_flash=None):
    img=B.copy()
    fr=cv2.resize(night[min(max(nf,0),NF-1)],(PW,PH),interpolation=cv2.INTER_LINEAR)
    img[PY:PY+PH,PX:PX+PW]=fr
    # label overlay
    reg=img[496:536,255:480].astype(np.float32); a=(lab_a*overlay_a)[...,None]
    img[496:536,255:480]=(reg*(1-a)+255*a).astype(np.uint8)
    bgra,s,c=cairo_ctx(img)
    # file name + timecode
    c.select_font_face(FONT,cairo.FONT_SLANT_NORMAL,cairo.FONT_WEIGHT_BOLD); c.set_font_size(31); c.set_source_rgb(.93,.94,.96)
    c.move_to(2126,697); c.show_text('bedroom-cam-0213am.mp4')
    secs=ph_t; c.select_font_face(FONT,cairo.FONT_SLANT_NORMAL,cairo.FONT_WEIGHT_NORMAL); c.set_font_size(30); c.set_source_rgb(121/255,132/255,148/255)
    c.move_to(418,1411); c.show_text('0:%02d / 0:06'%int(secs))
    c.select_font_face('DejaVu Sans Mono',cairo.FONT_SLANT_NORMAL,cairo.FONT_WEIGHT_NORMAL); c.set_font_size(24); c.set_source_rgb(.55,.48,.96)
    c.move_to(576,1408); c.show_text('F%04d / 0145'%(int(round(secs*24))+1))
    # button flash
    if btn_flash:
        (bx,by),fa=btn_flash; c.set_source_rgba(.49,.36,1,.35*fa); rrect(c,bx-80,by-26,160,52,12); c.fill()
    # marker on the ruler
    if marker_p>0:
        mx=T0X+5.04*PPS; e=ease_out_back(marker_p,2.2)
        c.save(); c.translate(mx,1668); c.scale(e,e)
        c.set_source_rgb(.996,.83,.43); c.move_to(0,18); c.curve_to(-14,0,-14,-24,0,-24); c.curve_to(14,-24,14,0,0,18); c.fill()
        c.set_source_rgb(.05,.06,.09); c.arc(0,-10,5,0,7); c.fill(); c.restore()
        if marker_p>0.35:
            ta=ease(seg(marker_p,.35,1)); c.set_source_rgba(.49,.36,1,.92*ta); rrect(c,mx+22,1646,212,40,10); c.fill()
            c.select_font_face(FONT,cairo.FONT_SLANT_NORMAL,cairo.FONT_WEIGHT_BOLD); c.set_font_size(24); c.set_source_rgba(1,1,1,ta); c.move_to(mx+36,1674); c.show_text('Ghost spotted')
    # callout box on the preview
    if callout_p>0:
        x0,y0=to_prev(CB[0],CB[1]); x1,y1=to_prev(CB[2],CB[3])
        k=ease_out(min(1,callout_p/0.75)); xe=lerp(x0,x1,k); ye=lerp(y0,y1,k)
        c.set_source_rgba(.996,.83,.43,1); c.set_line_width(7); rrect(c,x0,y0,xe-x0,ye-y0,14); c.stroke()
        c.set_source_rgba(.996,.83,.43,.10); rrect(c,x0,y0,xe-x0,ye-y0,14); c.fill()
        if label_p>0:
            e=ease_out_back(label_p,2.0); c.save(); c.translate(x0+8,y0-12); c.scale(e,e)
            c.set_source_rgb(.996,.83,.43); rrect(c,0,-52,250,52,12); c.fill()
            c.select_font_face(FONT,cairo.FONT_SLANT_NORMAL,cairo.FONT_WEIGHT_BOLD); c.set_font_size(34); c.set_source_rgb(.05,.06,.09); c.move_to(18,-14); c.show_text('GHOST?!'); c.restore()
    # playhead
    phx=T0X+ph_t*PPS; c.set_source_rgb(243/255,139/255,168/255); c.rectangle(phx-2.5,1660,5,140); c.fill()
    c.move_to(phx-10,1646); c.line_to(phx+10,1646); c.line_to(phx,1665); c.close_path(); c.fill()
    if cur: cursor(c,cur[0],cur[1],cur[2] if len(cur)>2 else 1.0,press)
    s.flush()
    return cv2.cvtColor(bgra,cv2.COLOR_BGRA2BGR)
def cam(img,rect):
    x,y,w,h=rect; sx=OW/w
    M=np.float32([[sx,0,-x*sx],[0,sx,-y*sx]])
    return cv2.warpAffine(img,M,(OW,OH),flags=cv2.INTER_AREA if sx<1 else cv2.INTER_LINEAR,borderMode=cv2.BORDER_REPLICATE)
P=(PX,PY,PW,PH); Wd=(0,90,2880,1620); Z1=(200,495,2320,1305)
def lr(a,b,t): return tuple(lerp(np.array(a,float),np.array(b,float),t))
def path(p0,p1,t):  # slightly curved cursor path
    t=ease(t); m=((p0[0]+p1[0])/2,(p0[1]+p1[1])/2-80)
    return ((1-t)**2*p0[0]+2*(1-t)*t*m[0]+t*t*p1[0],(1-t)**2*p0[1]+2*(1-t)*t*m[1]+t*t*p1[1])
N=138; NF0=137
def frame(e):
    nf=min(NF0+e,NF-1); ph=nf/24; marker=0; call=0; lab=0; cur=None; press=0; flash=None
    ov=ease(seg(e,4,22))
    if e<34: rect=lr(P,Wd,ease(e/33))
    elif e<64: rect=lr(Wd,Z1,ease((e-34)/30))
    elif e<126: rect=Z1
    else: rect=lr(Z1,Wd,ease((e-126)/11))
    # scrub back
    if e>=46:
        k=ease(seg(e,46,64)); nf=int(round(lerp(NF-1,121,k))); ph=lerp((NF-1)/24,5.04,k)
    phx=T0X+ph*PPS
    if 34<=e<126 or e>=126:
        if e<46: cur=path((2700,1150),(phx,1652),seg(e,34,46))
        elif e<64: cur=(phx,1652)
        elif e<74: cur=path((phx,1652),MARK_BTN,seg(e,64,74))
        elif e<94: cur=path(MARK_BTN,CALL_BTN,seg(e,80,94)) if e>=80 else MARK_BTN
        elif e<104: cur=path(CALL_BTN,to_prev(CB[0],CB[1]),seg(e,97,104)) if e>=97 else CALL_BTN
        elif e<118: x0,y0=to_prev(CB[0],CB[1]); x1,y1=to_prev(CB[2],CB[3]); k=ease_out(seg(e,104,114)); cur=(lerp(x0,x1,k),lerp(y0,y1,k))
        else: cur=to_prev(CB[2],CB[3])
        ca=1-ease(seg(e,124,134)); cur=(cur[0],cur[1],ca)
    if 46<=e<64: press=1
    if 74<=e<78: press=1; flash=(MARK_BTN,1-seg(e,74,80))
    if 78<=e<82: flash=(MARK_BTN,1-seg(e,74,82))
    if 94<=e<100: press=1 if e<97 else 0; flash=(CALL_BTN,1-seg(e,94,100))
    if 104<=e<114: press=1
    marker=seg(e,77,88); call=seg(e,104,118) if e>=104 else 0; lab=seg(e,116,126)
    img=editor_image(nf,ph,marker,call,lab,ov,cur,press,flash)
    return cam(img,rect), img
if __name__=='__main__':
    out=sys.argv[1]
    p=subprocess.Popen(['ffmpeg','-v','error','-y','-f','rawvideo','-pix_fmt','bgr24','-s',f'{OW}x{OH}','-r','24','-i','-','-c:v','libx264','-crf','10','-preset','medium','-pix_fmt','yuv420p',out],stdin=subprocess.PIPE)
    for e in range(N):
        o,full=frame(e); p.stdin.write(o.tobytes())
    p.stdin.close(); p.wait()
    cv2.imwrite('editor_final.png',full); print('ok',N)
