# Vectorise le logo AVIX (PNG basse définition) : couches jaune/blanche, potrace, puis rendu PNG 2x.
# Usage : python logo-vectorize.py ancien-logo.png dossier-sortie   (pip install pillow numpy scipy potracer)
import sys, numpy as np, potrace
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, zoom
src=np.array(Image.open(sys.argv[1]).convert('RGBA')).astype(float)
out_dir=sys.argv[2]
H,W=src.shape[:2]
a=src[...,3]/255; r,g,b=src[...,0],src[...,1],src[...,2]
sat=np.maximum(r,g)-b
UP=8
layers={'yellow':(sat>60), 'white':(sat<=60)}
colors={'yellow':(196,192,7),'white':(234,235,237)}
paths={}
for name,member in layers.items():
    cov=a*member
    big=zoom(cov,UP,order=3)
    big=gaussian_filter(big, sigma=UP*0.45 if name=='yellow' else UP*0.3)
    bm=potrace.Bitmap(big<=0.5)
    p=bm.trace(turdsize=UP*UP*4, alphamax=1.0, opticurve=True, opttolerance=0.2)
    curves=[]
    for c in p:
        segs=[]
        start=c.start_point
        for s in c.segments:
            if s.is_corner: segs.append(('L',s.c,s.end_point))
            else: segs.append(('C',s.c1,s.c2,s.end_point))
        curves.append(((start.x/UP,start.y/UP),[(t[0],)+tuple((q.x/UP,q.y/UP) for q in t[1:]) for t in segs]))
    paths[name]=curves
    print(name,len(curves),'curves')

def flatten(curve,scale,n=24):
    (sx,sy),segs=curve; pts=[(sx*scale,sy*scale)]; cur=(sx,sy)
    for s in segs:
        if s[0]=='L':
            pts.append((s[1][0]*scale,s[1][1]*scale)); pts.append((s[2][0]*scale,s[2][1]*scale)); cur=s[2]
        else:
            p0,p1,p2,p3=cur,s[1],s[2],s[3]
            for i in range(1,n+1):
                t=i/n; mt=1-t
                x=mt**3*p0[0]+3*mt*mt*t*p1[0]+3*mt*t*t*p2[0]+t**3*p3[0]
                y=mt**3*p0[1]+3*mt*mt*t*p1[1]+3*mt*t*t*p2[1]+t**3*p3[1]
                pts.append((x*scale,y*scale))
            cur=p3
    return pts

def render(scale, ss=4):
    w,h=int(W*scale),int(H*scale)
    out=np.zeros((h,w,4),float)
    for name in ['yellow','white']:
        acc=np.zeros((h*ss,w*ss),bool)
        for c in paths[name]:
            m=Image.new('1',(w*ss,h*ss),0)
            ImageDraw.Draw(m).polygon(flatten(c,scale*ss),fill=1)
            acc^=np.array(m)
        cov=np.array(Image.fromarray((acc*255).astype(np.uint8)).resize((w,h),Image.BOX)).astype(float)/255
        col=np.array(colors[name],float)
        # composite over
        out[...,:3]=out[...,:3]*(1-cov[...,None])+col*cov[...,None]*1  # premultiplied-ish (layers don't overlap)
        out[...,3]=out[...,3]+cov*(1-out[...,3])
    rgb=np.where(out[...,3:]>0, out[...,:3]/np.maximum(out[...,3:],1e-6), 0)
    img=np.dstack([np.clip(rgb,0,255),out[...,3]*255]).round().astype(np.uint8)
    return Image.fromarray(img,'RGBA')

def svg():
    parts=[f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}">']
    for name in ['yellow','white']:
        d=[]
        for (s,segs) in paths[name]:
            d.append(f'M{s[0]:.2f} {s[1]:.2f}')
            for t in segs:
                if t[0]=='L': d.append(f'L{t[1][0]:.2f} {t[1][1]:.2f}L{t[2][0]:.2f} {t[2][1]:.2f}')
                else: d.append('C'+' '.join(f'{x:.2f} {y:.2f}' for x,y in t[1:]))
            d.append('Z')
        c=colors[name]
        parts.append(f'<path fill="rgb{c}" fill-rule="evenodd" d="{"".join(d)}"/>')
    parts.append('</svg>')
    return '\n'.join(parts)

open(out_dir+'/avix-logo.svg','w').write(svg())
big=render(2); big.save(out_dir+'/avix-logo-2x.png', optimize=True)
print(big.size)
