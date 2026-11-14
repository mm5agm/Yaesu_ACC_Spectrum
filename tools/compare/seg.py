import sys,json,numpy as np
from load import *
def segs(pre, settle=0.4, rx='main'):
    D=DATA+'/'+pre; t0=float(open(D+'-t0.txt').read()); log=json.load(open(D+'-log.json'))
    t,R=load_acc(D+'-acc.bin'); ta=t+t0; L=R[rx]['lv'].astype(float)
    s=load_sdr(D+'-sdrA.bin'); ts=np.array([x[0] for x in s])+t0; B=np.array([x[4][::-1] for x in s])
    out=[]
    for k,(a,bb,v) in enumerate(log[:-1]):
        lo,hi=bb+settle, log[k+1][0]
        m=(ta>lo)&(ta<hi); ms=(ts>lo)&(ts<hi)
        if m.sum()<3: continue
        out.append((v,L[m],B[ms] if ms.sum() else None))
    return out
def pairdiff(vals):
    v=np.array(vals); d=[]
    for i in range(1,len(v)-1): d.append(v[i]-(v[i-1]+v[i+1])/2)
    return np.array(d)
