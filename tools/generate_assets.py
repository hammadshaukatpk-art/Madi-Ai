from PIL import Image, ImageDraw, ImageFilter
import math, os, random
random.seed(8)
os.makedirs("assets", exist_ok=True)
S=1024
base=Image.new("RGBA",(S,S),(0,0,0,0))
d=ImageDraw.Draw(base,"RGBA")
d.rounded_rectangle((12,12,1012,1012),radius=240,fill=(3,13,30,255),outline=(20,135,255,255),width=18)
for r in (365,345,315):
    d.arc((512-r,512-r,512+r,512+r),15,335,fill=(35,197,255,155),width=10)
    d.arc((512-r,512-r,512+r,512+r),145,245,fill=(137,67,255,180),width=8)
for _ in range(50):
    a=random.random()*2*math.pi;r=random.randrange(310,380)
    x=int(512+math.cos(a)*r);y=int(512+math.sin(a)*r)
    d.ellipse((x-2,y-2,x+2,y+2),fill=(91,210,255,170))
glow=Image.new("RGBA",(S,S),(0,0,0,0));g=ImageDraw.Draw(glow)
poly=[(225,705),(225,304),(352,304),(512,497),(671,304),(798,304),(798,705),(674,705),(674,477),(512,656),(349,477),(349,705)]
g.polygon(poly,fill=(37,165,255,210))
halo=glow.filter(ImageFilter.GaussianBlur(32))
base=Image.alpha_composite(base,halo)
d=ImageDraw.Draw(base,"RGBA")
d.polygon(poly,fill=(70,203,255,255))
d.polygon([(248,674),(248,337),(343,337),(512,539),(679,337),(775,337),(775,674),(700,674),(700,426),(512,626),(324,426),(324,674)],fill=(7,19,55,255))
d.polygon([(225,304),(352,304),(512,497),(512,656),(349,477),(349,705),(225,705)],fill=(19,198,255,115))
d.polygon([(671,304),(798,304),(798,705),(674,705),(674,477),(512,656),(512,497)],fill=(150,83,255,90))
base.save("assets/madi.png")
base.resize((256,256),Image.Resampling.LANCZOS).save("assets/madi.ico",format="ICO",sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
# Holographic assistant: dense point-cloud portrait, luminous facial contours,
# ear pieces and neon floor. This is original vector/raster artwork, not a flat emoji.
W,H=700,730
face=Image.new("RGBA",(W,H),(0,0,0,0))
glow=Image.new("RGBA",(W,H),(0,0,0,0))
g=ImageDraw.Draw(glow,"RGBA")
f=ImageDraw.Draw(face,"RGBA")
cx=350
# Neck and shoulder hologram
f.polygon([(280,515),(290,612),(180,670),(164,722),(536,722),(520,670),(410,612),(420,515)],fill=(6,43,93,130))
for y in range(590,713,9):
    half=int(160*math.sqrt(max(0,1-((y-680)/160)**2)))
    f.line((cx-half,y,cx+half,y),fill=(25,157,255,50),width=1)
f.arc((130,618,570,720),0,180,fill=(46,214,255,175),width=3)
# 3D skull ellipse, darker toward edges and bottom, brighter central ridge
for ry in range(254,12,-2):
    t=1-ry/254
    r=int(13+53*t)
    b=int(56+175*t)
    a=int(45+128*t)
    f.ellipse((cx-194*(ry/254),52+254-ry,cx+194*(ry/254),52+254+ry),fill=(r,b,150,a))
# Face contour in front
f.arc((158,51,542,568),188,354,fill=(61,231,255,255),width=7)
f.arc((158,51,542,568),5,175,fill=(42,187,255,230),width=5)
# Perspective vertical latitude wireframe, sparse enough to preserve face depth
for y in range(76,554,11):
    ny=(y-308)/256
    if abs(ny)>=1: continue
    half=190*math.sqrt(1-ny*ny)
    f.arc((cx-half,y-6,cx+half,y+6),0,180,fill=(25,144,240,64),width=1)
for a in range(-72,73,12):
    # longitudinal curves on an oval head
    pts=[]
    for y in range(82,540,8):
        ny=(y-308)/256
        if abs(ny)<1:
            xx=cx+190*math.sqrt(1-ny*ny)*math.sin(math.radians(a))
            pts.append((int(xx),y))
    if len(pts)>2:f.line(pts,fill=(28,144,255,50),width=1)
# Ears / headset, match reference's cyan outlines
for x,sgn in [(153,-1),(547,1)]:
    f.rounded_rectangle((x-13,280,x+13,405),radius=12,fill=(4,34,78,220),outline=(45,207,255,220),width=4)
    f.arc((x-24,278,x+24,412),-75 if sgn<0 else 75,75 if sgn<0 else 255,fill=(51,199,255,230),width=3)
# Eye sockets and luminous almond eyes
for ex in (275,425):
    f.arc((ex-63,276,ex+63,332),180,356,fill=(25,170,255,175),width=3)
    g.ellipse((ex-47,300,ex+47,322),fill=(21,211,255,225))
    f.ellipse((ex-40,301,ex+40,321),fill=(161,248,255,255))
    f.ellipse((ex-16,302,ex+16,320),fill=(245,255,255,255))
# Nose ridge and realistic lip contour
f.line([(348,314),(341,367),(328,411),(350,420),(373,414)],fill=(62,210,255,170),width=3)
f.arc((300,438,400,477),3,175,fill=(75,226,255,245),width=4)
f.arc((308,457,393,490),182,350,fill=(42,161,245,180),width=2)
f.arc((287,486,413,520),8,173,fill=(22,127,225,100),width=2)
# Blue pixel constellation and soft center highlights
for _ in range(13500):
    x=random.randrange(162,538);y=random.randrange(68,559)
    if ((x-350)/190)**2+((y-307)/255)**2>=.98: continue
    radial=((x-350)/190)**2+((y-307)/255)**2
    alpha=int(45+random.randrange(50,195)*(1-radial*.5))
    f.point((x,y),fill=(66,210,255,alpha))
for _ in range(110):
    x=random.randrange(185,516);y=random.randrange(85,527)
    if ((x-350)/190)**2+((y-307)/255)**2<.91:
        rr=random.choice([1,1,2])
        f.ellipse((x-rr,y-rr,x+rr,y+rr),fill=(80,217,255,random.randrange(100,225)))
# Edge glow and eyes glow
halo=face.filter(ImageFilter.GaussianBlur(16))
halo.putalpha(halo.getchannel("A").point(lambda v:int(v*.68)))
face=Image.alpha_composite(halo,face)
eyeGlow=glow.filter(ImageFilter.GaussianBlur(18))
face=Image.alpha_composite(face,eyeGlow)
# Brighter reference-style luminous eyes, volumetric cheeks and projected collar.
# The image is generated from original geometry, rather than copying a flat UI screenshot.
light=Image.new("RGBA",(W,H),(0,0,0,0))
ld=ImageDraw.Draw(light,"RGBA")
for ex in (275,425):
    ld.ellipse((ex-58,286,ex+58,337),fill=(8,163,255,155))
    ld.ellipse((ex-43,298,ex+43,321),fill=(95,229,255,255))
    ld.ellipse((ex-26,302,ex+26,317),fill=(232,255,255,255))
light=light.filter(ImageFilter.GaussianBlur(16))
face=Image.alpha_composite(face,light)
detail=Image.new("RGBA",(W,H),(0,0,0,0))
dd=ImageDraw.Draw(detail,"RGBA")
for ex in (275,425):
    dd.ellipse((ex-42,299,ex+42,320),fill=(51,206,255,255))
    dd.ellipse((ex-28,302,ex+28,316),fill=(222,252,255,255))
    dd.line((ex-47,299,ex+47,299),fill=(97,235,255,230),width=2)
dd.arc((192,145,508,510),5,175,fill=(28,192,255,140),width=2)
dd.arc((205,164,495,510),185,355,fill=(53,217,255,95),width=2)
dd.line([(294,499),(290,571),(258,611)],fill=(57,214,255,175),width=3)
dd.line([(406,499),(410,571),(442,611)],fill=(57,214,255,175),width=3)
dd.arc((182,605,518,715),180,355,fill=(39,214,255,220),width=4)
face=Image.alpha_composite(face,detail)
face.save("assets/assistant.png")
print("Generated 1024px Madi icon and HD holographic point-cloud assistant")
