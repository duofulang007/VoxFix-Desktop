"""Validate continuous display, shorter native context, and bounded software gain."""
import gc
import json
import os
from pathlib import Path
import sys
from types import SimpleNamespace

ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'tools'))
from realtime_probe import LevelBoost, MANIFEST, read, run
os.environ['TRANSCRIBE_LIBRARY']=str(ROOT/'runtime/transcribe-0.4.0/transcribe-native-windows-x86_64-cpu-vulkan/transcribe.dll')
import transcribe_cpp as tc


class LiveStream:
    def __init__(self,stream):self.stream=stream
    def feed(self,pcm):return self.stream.feed(pcm)
    def finalize(self):return self.stream.finalize()
    def reset(self):return self.stream.reset()
    def text(self):
        text=self.stream.text().display
        return SimpleNamespace(committed=text,display=text)


class LiveSession:
    def __init__(self,session,fast):self.session,self.fast=session,fast
    def stream(self,**options):
        if self.fast:options['family']=tc.ParakeetStreamOptions(att_context_right=6)
        return LiveStream(self.session.stream(**options))


def main():
    output=ROOT/'work/realtime-probe-20261020/candidates.json';rows=[]
    with tc.Model(ROOT/'models/nemotron-gguf/nemotron-3.5-asr-streaming-0.6b-Q8_0.gguf',backend='cpu') as model:
        with model.session(n_threads=4) as session:
            for label,fast,boost in [('full',False,False),('fast-full',True,False),('fast-full-gain',True,True)]:
                live=LiveSession(session,fast)
                for row in MANIFEST:rows.append(run(live,row,read(row),label,'base',boost=boost))
                output.write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
                print(label,'done',len(rows),flush=True)
            selected=[]
            for category in ['baseline','everyday','short','negation','mixed','number']:
                selected.extend([r for r in MANIFEST if r['category']==category][:6])
            live=LiveSession(session,True)
            for scale in [0.01,0.001]:
                for row in selected:rows.append(run(live,row,read(row)*scale,'fast-full-gain','atten'+str(scale),boost=True))
            import numpy as np
            rng=np.random.default_rng(20261020)
            for i in range(10):
                audio=np.zeros(32000) if i<2 else rng.normal(0,0.0005 if i<6 else 0.005,32000)
                rows.append(run(live,{'id':'noise-'+str(i),'category':'silence','expected':''},audio,'fast-full-gain','noise-gain',boost=True))
            for identity in ['plain-zh-0','new-short-0','long-0-0']:
                row=next(r for r in MANIFEST if r['id']==identity)
                rows.append(run(live,row,read(row),'fast-full-gain','paced',paced=True,boost=True))
    output.write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
    print('DONE',len(rows),flush=True)


if __name__=='__main__':main()
