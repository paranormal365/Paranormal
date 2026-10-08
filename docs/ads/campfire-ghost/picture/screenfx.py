import cv2,numpy as np
def smooth(x,a,b): t=np.clip((x-a)/(b-a),0,1); return t*t*(3-2*t)
REF_LUM=236.0
def comp_screen(plate,quad,content,fade=1.0,bright=(1.0,0.0)):
    """key the white tablet screen in `plate` (1344x768 BGR uint8) and put `content` (any size) on it."""
    H,W=plate.shape[:2]; plate=plate.astype(np.float32)
    G=np.clip(content.astype(np.float32)*bright[0]+bright[1],0,255)
    gh,gw=G.shape[:2]; src=np.float32([[0,0],[gw-1,0],[gw-1,gh-1],[0,gh-1]])
    c=quad.mean(0); qbig=(quad-c)*1.02+c
    M=cv2.getPerspectiveTransform(src,qbig.astype(np.float32))
    warped=cv2.warpPerspective(G,M,(W,H),flags=cv2.INTER_AREA,borderMode=cv2.BORDER_REPLICATE)
    warped=cv2.GaussianBlur(warped,(0,0),0.5)
    poly=np.zeros((H,W),np.uint8); cv2.fillConvexPoly(poly,((quad-c)*1.04+c).astype(np.int32),1)
    poly=cv2.GaussianBlur(poly.astype(np.float32),(0,0),1.5)
    B,Gc,R=plate[...,0],plate[...,1],plate[...,2]
    inner=np.zeros((H,W),np.uint8); cv2.fillConvexPoly(inner,((quad-c)*0.85+c).astype(np.int32),1)
    sel=(inner>0)&(B>120)&(B-R>5)
    scr=np.median(plate[sel],0) if sel.sum()>50 else np.array([238,237,207],np.float32)
    lum=np.minimum(B,Gc); mlum=float(min(scr[0],scr[1]))
    a_l=np.clip((lum-30)/(0.85*mlum-30),0,1)
    a_h=smooth(B-R,-8,0.5*max(scr[0]-scr[2],10))
    alpha=(a_l*a_h*poly)*fade
    alpha=np.minimum(cv2.dilate(alpha,np.ones((3,3),np.uint8)),poly)
    alpha=cv2.GaussianBlur(alpha,(0,0),0.6)[...,None]
    light=np.clip(cv2.GaussianBlur(lum,(0,0),6)/REF_LUM,0.45,1.08)[...,None]
    out=plate+alpha*(warped*light-scr[None,None,:])
    return np.clip(out,0,255).astype(np.uint8)
