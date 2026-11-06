# Draws pictures meant to make the search for a scan's paper, in RemoveWhiteBackground, go wrong.
import sys, numpy as np
from PIL import Image, ImageDraw
# Usage, from the repository root: py docs/transparency-report/make-synthetic.py
out = sys.argv[1] if len(sys.argv) > 1 else "docs/transparency-report/synthetic"
W, H = 600, 450
rng = np.random.default_rng(1)

def scene(d, ink=(0,0,0), w=4):
    d.line([(0,330),(150,250),(300,300),(450,220),(600,300)], fill=ink, width=w)   # hills
    d.rectangle([220,200,360,330], outline=ink, width=w)                           # house
    d.polygon([(205,200),(290,130),(375,200)], outline=ink, width=w)               # roof
    d.rectangle([270,260,310,330], outline=ink, width=w)                           # door
    d.ellipse([460,40,540,120], outline=ink, width=w)                              # sun
    for x in range(30,600,90): d.line([(x,400),(x+25,370),(x+50,400)], fill=ink, width=3)

def save(name, img):
    img.save(f"{out}/{name}.png"); print(name)

# 1. A light gray sky wash that covers more area than the white paper.
im = Image.new("RGB", (W,H), "white"); d = ImageDraw.Draw(im)
d.rectangle([0,0,W,300], fill=(228,228,228)); scene(d)
save("s1-gray-sky-wash", im)

# 2. A big pale-yellow area (darkest channel 200) larger than the white paper.
im = Image.new("RGB", (W,H), "white"); d = ImageDraw.Draw(im)
d.rectangle([0,0,W,320], fill=(255,240,200)); d.ellipse([460,40,540,120], fill=(250,200,60))
d.rectangle([220,200,360,330], fill=(200,120,90)); scene(d)
save("s2-pale-yellow-field", im)

# 3. A shaded drawing that is mostly mid gray (170): a dim room.
im = Image.new("RGB", (W,H), "white"); d = ImageDraw.Draw(im)
d.rectangle([20,20,W-20,H-20], fill=(170,170,170)); d.rectangle([60,60,250,250], fill=(235,235,235))
d.rectangle([330,150,560,420], fill=(120,120,120)); scene(d)
save("s3-mostly-mid-gray", im)

# 4. A scan lit unevenly: the paper goes from 250 on the left to 205 on the right, with grain.
base = np.tile(np.linspace(250,205,W), (H,1))
im = Image.new("L", (W,H), 255); d = ImageDraw.Draw(im); scene(d, ink=0)
a = np.minimum(base, np.asarray(im).astype(float)) + rng.normal(0,4,(H,W))
save("s4-scan-uneven-light", Image.fromarray(np.clip(a,0,255).astype(np.uint8)).convert("RGB"))

# 5. A scan on gray paper (232) whose pencil shading is only a little darker than the paper.
im = Image.new("L", (W,H), 255); d = ImageDraw.Draw(im)
for i, v in enumerate([226, 220, 212, 200, 185]):
    d.rectangle([40+i*110, 60, 130+i*110, 200], fill=v)
for y in range(240, 420, 6): d.line([(40,y),(560,y+30)], fill=218, width=2)
scene(d, ink=0, w=3)
a = np.asarray(im).astype(float); a = np.where(a==255, 232, a) + rng.normal(0,3,(H,W))
save("s5-scan-faint-pencil", Image.fromarray(np.clip(a,0,255).astype(np.uint8)).convert("RGB"))

# 6. A scan on cream paper (not gray): R 245, G 235, B 205.
im = Image.new("RGB", (W,H), (245,235,205)); d = ImageDraw.Draw(im)
d.ellipse([460,40,540,120], fill=(240,170,40)); scene(d)
a = np.asarray(im).astype(float) + rng.normal(0,3,(H,W,1))
save("s6-scan-cream-paper", Image.fromarray(np.clip(a,0,255).astype(np.uint8)))

# 7. A picture with no white at all: a coloured scene with a few small highlights at 230.
im = Image.new("RGB", (W,H), (70,130,110)); d = ImageDraw.Draw(im)
d.rectangle([0,0,W,200], fill=(60,90,170)); d.rectangle([220,200,360,330], fill=(170,90,60))
scene(d)
for x, y in [(80,60),(160,90),(330,50),(420,110),(520,70),(250,250),(300,280)]:
    d.ellipse([x-6,y-6,x+6,y+6], fill=(230,230,230))
save("s7-no-white-highlights", im)

# 8. A scan on gray paper (232) with grain, saved as a palette PNG of at most 256 colours.
im = Image.new("L", (W,H), 232); d = ImageDraw.Draw(im); scene(d, ink=0)
a = np.asarray(im).astype(float) + rng.normal(0,3,(H,W))
pal = Image.fromarray(np.clip(a,0,255).astype(np.uint8)).convert("RGB").convert("P", palette=Image.ADAPTIVE, colors=256)
pal.save(f"{out}/s8-scan-palette.png"); print("s8-scan-palette")
