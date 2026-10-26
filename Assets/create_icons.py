"""Deterministic app icon: a display with an audio wave, antialiased at every size."""
from PIL import Image, ImageDraw
from pathlib import Path
p=Path(__file__).resolve().parent
im=Image.new('RGBA',(1024,1024))
d=ImageDraw.Draw(im)
d.rounded_rectangle((16,16,1008,1008),radius=260,fill='#657BEE')
d.rounded_rectangle((174,230,850,698),radius=82,outline='white',width=54)
d.line((512,700,512,794),fill='white',width=48)
d.rounded_rectangle((363,775,661,824),radius=24,fill='white')
d.line([(326,468),(391,468),(438,399),(503,551),(558,430),(605,485),(698,485)],fill='white',width=44,joint='curve')
for n in (44,50,150): im.resize((n,n),Image.Resampling.LANCZOS).save(p/f'Logo{n}.png')
im.resize((256,256),Image.Resampling.LANCZOS).save(p.parent/'app.ico',sizes=[(n,n) for n in (16,20,24,32,40,48,64,128,256)])
