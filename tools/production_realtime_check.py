import json,sys
from pathlib import Path
sys.path.insert(0,str(Path('tools').resolve()))
from realtime_probe import MANIFEST,create_engine,read,run
rows=[]
with create_engine('nemotron-cpp') as session:
 for row in MANIFEST:rows.append(run(session,row,read(row),'production-1.2','base'))
 Path('work/realtime-probe-20261020/production.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
 selected=[]
 for category in ['baseline','everyday','short','negation','mixed','number']:selected.extend([r for r in MANIFEST if r['category']==category][:6])
 for scale in [.01,.001]:
  for row in selected:rows.append(run(session,row,read(row)*scale,'production-1.2','atten'+str(scale)))
 import numpy as np
 rng=np.random.default_rng(20261020)
 for i in range(10):rows.append(run(session,{'id':'noise-'+str(i),'category':'silence','expected':''},np.zeros(32000) if i<2 else rng.normal(0,.0005 if i<6 else .005,32000),'production-1.2','noise'))
 for identity in ['plain-zh-0','new-short-0','long-0-0']:
  row=next(r for r in MANIFEST if r['id']==identity);rows.append(run(session,row,read(row),'production-1.2','paced',paced=True))
Path('work/realtime-probe-20261020/production.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf8')
print('DONE',len(rows))
