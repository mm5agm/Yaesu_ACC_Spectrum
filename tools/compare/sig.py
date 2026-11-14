import sys,numpy as np
from an import *
pre=sys.argv[1]; span=float(sys.argv[2]); centre=float(sys.argv[3])
src=sources(pre)
def axis(n): return centre+(np.arange(n)-(n//2 if n!=791 else 395))*span/n
grid=np.arange(centre-span/2+span/40, centre+span/2-span/40, span/791)
# frequency alignment: cross-correlate long-term mean spectra (finely interpolated) vs RSP1B
fine=np.arange(grid[0],grid[-1],1.0)
ref=np.interp(fine,axis(src['RSP1B'][0].shape[1]),src['RSP1B'][0].mean(0))
for n,(F,t) in src.items():
    x=np.interp(fine,axis(F.shape[1]),F.mean(0)); a=(x-x.mean()); b=(ref-ref.mean())
    lags=np.arange(-300,301); c=[np.dot(a[300+l:len(a)-300+l],b[300:len(b)-300]) for l in lags]
    print(f'{n:9s} offset vs RSP1B {lags[int(np.argmax(c))]:+d} Hz')
# --- per-signal: peaks found in RSP1B slot averages, measured in every source frame by frame
t0=float(open(f'{DATA}/{pre}-t0.txt').read())
off={'ACC Main':25,'ACC Sub':25}
def refined(n,F,w): return F if w is None else ema(F,w)
cases=[('ACC Main',1),('ACC Main',.3),('ACC Main',.12),('ACC Sub',.3),('RSP1B',None),('RSP1',None)]
res={c:[] for c in cases}; nsig=0
T=src['RSP1B'][1]
for k in range(int((T[0]+t0)//15)+1, int((T[-1]+t0)//15)):
    lo,hi=k*15+2-t0, k*15+12-t0
    F,t=src['RSP1B']; m=(t>lo)&(t<hi)
    if m.sum()<20: continue
    r=np.interp(grid,axis(F.shape[1]),F[m].mean(0)); fl=np.median(r)
    pk=[i for i in range(2,len(r)-2) if r[i]==r[i-2:i+3].max() and r[i]>fl+6]
    pk=[i for i in pk if r[i]<fl+20]          # weak-to-moderate signals only
    nsig+=len(pk)
    for c in cases:
        n,w=c; F,t=src[n]; G=refined(n,F,w); m=(t>lo)&(t<hi); ax=axis(F.shape[1])+off.get(n,0)
        lt=G.mean(0); fp=lt<=np.percentile(lt,50)
        for fr in G[m]:
            med=np.median(fr[fp]); sp=(np.percentile(fr[fp],84)-np.percentile(fr[fp],16))/2
            for i in pk:
                sel=np.abs(ax-grid[i])<=max(26,span/F.shape[1]*1.5)
                res[c].append((fr[sel].max()-med)/sp)
print('weak/moderate signals (6-20 dB above floor on RSP1B slot average):',nsig)
for c,v in res.items():
    v=np.array(v); print(f'{c[0]:9s} {"" if c[1] is None else "w "+str(c[1]):7s} stands out by {np.median(v):5.1f}x spread; visible (>3x) in {100*(v>3).mean():4.0f}% of frames')
