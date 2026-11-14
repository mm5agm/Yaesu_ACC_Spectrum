import sys,numpy as np
from load import *
DB=0.42
def ema(L,w):
    out=np.empty_like(L,dtype=float); a=L[0].astype(float)
    for i in range(len(L)): a=a+w*(L[i]-a); out[i]=a
    return out
def sources(pre, skip=2.0):
    t,rx=load_acc(f'{DATA}/{pre}-acc.bin'); m=t>skip
    src={}
    for k,n in (('main','ACC Main'),('sub','ACC Sub')):
        lv=rx[k]['lv'][m][:,425-395:425+396].astype(float)   # the 791 span points
        src[n]=(lv*DB, t[m])
    for k,n in (('A','RSP1B'),('B','RSP1')):
        s=load_sdr(f'{DATA}/{pre}-sdr{k}.bin'); ts=np.array([x[0] for x in s]); mm=ts>skip
        src[n]=(np.array([x[4][::-1] for x in s])[mm].astype(float), ts[mm])
    return src
def metrics(F):
    lt=F.mean(0); floorpts=lt<=np.percentile(lt,50)
    med=np.median(F[:,floorpts],1)
    sp=[(np.percentile(f[floorpts],84)-np.percentile(f[floorpts],16))/2 for f in F]
    sp=np.array(sp); pk=F.max(1)-med
    jit=(F[:,floorpts]-lt[floorpts]).std()
    return dict(spread=sp.mean(), jitter=jit, peak=pk.mean(), clarity=(pk/sp).mean())
if __name__=='__main__':
    pre=sys.argv[1]; src=sources(pre)
    print(f'{"source":22s} {"pts":>5s} {"Hz/pt":>6s} {"fps":>5s} {"spread dB":>9s} {"jitter dB":>9s} {"peak dB":>8s} {"clarity":>7s}')
    for n,(F,t) in src.items():
        fps=len(t)/(t[-1]-t[0]); span=float(sys.argv[2]) if len(sys.argv)>2 else 20000
        ws=[('raw',1)]+[(f'Refine {i} (w {w})',w) for i,w in ((1,.6),(2,.45),(3,.3),(4,.2),(5,.12))] if n.startswith('ACC') else [('as YWC draws it',None)]
        for lab,w in ws:
            G=F if w in (None,1) else ema(F,w); r=metrics(G)
            print(f'{n+" "+lab:22.22s} {F.shape[1]:5d} {span/F.shape[1]:6.1f} {fps:5.1f} {r["spread"]:9.2f} {r["jitter"]:9.2f} {r["peak"]:8.1f} {r["clarity"]:7.1f}')
