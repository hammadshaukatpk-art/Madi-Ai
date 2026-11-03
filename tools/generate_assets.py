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
# Holographic assistant portrait: transparent wireframe with bright eyes, neck and digital nodes.
W=700;H=730
face=Image.new("RGBA",(W,H),(0,0,0,0))
f=ImageDraw.Draw(face,"RGBA")
f.ellipse((140,48,560,570),fill=(4,35,79,195),outline=(29,198,255,255),width=6)
f.polygon([(154,340),(186,490),(265,570),(300,580),(298,665),(210,698),(490,698),(402,665),(400,580),(435,570),(516,490),(546,340)],fill=(5,29,73,180),outline=(35,177,255,180))
for y in range(90,570,15):
    span=(1-((y-306)/280)**2)**0.5 if abs(y-306)<280 else 0
    xl=350-205*span;xr=350+205*span
    f.line((xl,y,xr,y),fill=(15,145,242,65),width=1)
for x in range(170,530,18):
    f.arc((x-60,90,x+60,568),70,290,fill=(30,164,252,46),width=1)
f.arc((185,230,335,355),185,350,fill=(36,220,255,255),width=6)
f.arc((365,230,515,355),185,350,fill=(36,220,255,255),width=6)
f.ellipse((241,281,296,303),fill=(76,232,255,255))
f.ellipse((404,281,459,303),fill=(76,232,255,255))
f.line((350,313,323,441,356,457),fill=(30,173,247,170),width=3)
f.arc((296,468,403,507),8,175,fill=(70,201,255,220),width=3)
for _ in range(950):
    x=random.randrange(152,548);y=random.randrange(80,564)
    if ((x-350)/205)**2+((y-305)/258)**2<1:
        f.point((x,y),fill=(48,177,255,random.randrange(50,175)))
glow=face.filter(ImageFilter.GaussianBlur(15))
glow.putalpha(glow.getchannel("A").point(lambda x:int(x*.35)))
face=Image.alpha_composite(glow,face)
face.save("assets/assistant.png")
print("Generated HD app icon and transparent assistant art")
