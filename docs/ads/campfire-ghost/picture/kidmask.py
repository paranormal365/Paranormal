import cv2,numpy as np
cap=cv2.VideoCapture('camp_fixed.mp4'); cap.set(cv2.CAP_PROP_POS_FRAMES,279); r,f=cap.read()
m=np.load('seg_279.npy')
H,W=m.shape
poly=np.array([(650,0),(1000,0),(1010,190),(1150,215),(1185,330),(1230,500),(1262,768),(200,768),(205,575),(505,600),(600,600),(660,520),(700,420),(690,300),(645,250)],np.int32)
P=np.zeros((H,W),np.float32); cv2.fillPoly(P,[poly],1); P=cv2.GaussianBlur(P,(0,0),6)
tab=np.array([(212,560),(505,598),(490,768),(290,768)],np.int32)
T=np.zeros((H,W),np.float32); cv2.fillPoly(T,[tab],1)
M=np.maximum(np.clip((m-0.15)/0.5,0,1)*P,T)
M=cv2.GaussianBlur(M,(0,0),1.5)
np.save('kidmask.npy',M)
vis=f.copy(); vis[...,2]=np.clip(vis[...,2]+M*120,0,255)
cv2.imwrite('kidmask.png',cv2.resize(np.hstack([vis,cv2.cvtColor((M*255).astype(np.uint8),cv2.COLOR_GRAY2BGR)]),None,fx=0.5,fy=0.5))
