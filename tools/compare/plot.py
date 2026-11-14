import sys,numpy as np, matplotlib; matplotlib.use('Agg'); import matplotlib.pyplot as plt
from an import *
pre,span,title,out=sys.argv[1],float(sys.argv[2]),sys.argv[3],sys.argv[4]
src=sources(pre); tm=np.median(src['RSP1B'][1])
rows=[('ACC scope, raw (Refine 0)','ACC Main',1),('ACC scope, Refine 3 (YWC default)','ACC Main',.3),('RSP1 (as YWC draws it)','RSP1',None),('RSP1B (as YWC draws it)','RSP1B',None)]
fig,axs=plt.subplots(4,1,figsize=(9,8.2),sharex=True,facecolor='#fcfcfb')
for ax,(lab,n,w) in zip(axs,rows):
    F,t=src[n]; G=F if w in (None,1) else ema(F,w); i=np.argmin(abs(t-tm)); f=G[i]
    N=len(f); x=(np.arange(N)-(395 if N==791 else N//2))*span/N/1000
    y=f-np.median(f)
    ax.plot(x,y,color='#2a78d6',lw=1.0); ax.set_facecolor('#fcfcfb')
    ax.set_ylim(-15,50); ax.grid(axis='y',color='#e4e3df',lw=0.6); ax.set_axisbelow(True)
    for s in ('top','right'): ax.spines[s].set_visible(False)
    for s in ('left','bottom'): ax.spines[s].set_color('#b8b7b2')
    ax.tick_params(colors='#52514e',labelsize=8)
    ax.set_title(f'{lab}: {N} points, {span/N:.1f} Hz each',loc='left',fontsize=9.5,color='#0b0b0b')
    ax.set_ylabel('dB above floor',fontsize=8,color='#52514e')
axs[-1].set_xlabel('kHz from the dial',fontsize=9,color='#52514e')
fig.suptitle(title,x=0.06,ha='left',fontsize=11.5,color='#0b0b0b')
fig.text(0.06,0.005,'One frame from each, recorded at the same moment, 2026-11-14. ACC levels at the measured 0.42 dB per step.',fontsize=7.5,color='#52514e')
fig.tight_layout(rect=(0,0.02,1,0.97)); fig.savefig(out,dpi=110,facecolor='#fcfcfb')
