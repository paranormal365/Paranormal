import cv2,numpy as np,onnxruntime as ort,sys
sess=ort.InferenceSession('u2net_human_seg.onnx',providers=['CPUExecutionProvider'])
def seg(img):
    h,w=img.shape[:2]
    x=cv2.resize(cv2.cvtColor(img,cv2.COLOR_BGR2RGB),(320,320)).astype(np.float32)/255.
    x=(x-[0.485,0.456,0.406])/[0.229,0.224,0.225]
    x=x.transpose(2,0,1)[None].astype(np.float32)
    o=sess.run(None,{sess.get_inputs()[0].name:x})[0][0,0]
    o=(o-o.min())/(o.max()-o.min()+1e-8)
    return cv2.resize(o,(w,h))
if __name__=='__main__':
    cap=cv2.VideoCapture('camp_fixed.mp4'); n=int(sys.argv[1]); cap.set(cv2.CAP_PROP_POS_FRAMES,n); r,f=cap.read()
    m=seg(f); cv2.imwrite('seg_%d.png'%n,np.hstack([f,cv2.cvtColor((m*255).astype(np.uint8),cv2.COLOR_GRAY2BGR)]))
    np.save('seg_%d.npy'%n,m)
