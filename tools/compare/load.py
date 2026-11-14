# Reads the files compare-capture writes. DATA is where they are.
import os
DATA=os.environ.get('ACC_COMPARE_DATA','data')
import numpy as np, struct
def load_acc(p):
    b=open(p,'rb').read(); R=8+2*(8+8+4+850); n=len(b)//R
    t=np.zeros(n); rx={k:dict(vfo=np.zeros(n,np.int64),cen=np.zeros(n,np.int64),span=np.zeros(n,np.int32),lv=np.zeros((n,850),np.uint8)) for k in('main','sub')}
    for i in range(n):
        o=i*R; t[i]=struct.unpack_from('<d',b,o)[0]; o+=8
        for k in('main','sub'):
            v,c,s=struct.unpack_from('<qqi',b,o); o+=20
            rx[k]['vfo'][i]=v; rx[k]['cen'][i]=c; rx[k]['span'][i]=s
            rx[k]['lv'][i]=np.frombuffer(b,np.uint8,850,o); o+=850
    return t,rx
def load_sdr(p):
    b=open(p,'rb').read(); o=0; out=[]
    while o<len(b):
        t,c,s=struct.unpack_from('<ddd',b,o); o+=24; ro=b[o]; o+=1; n,=struct.unpack_from('<i',b,o); o+=4
        bins=np.frombuffer(b,np.float32,n,o).copy(); o+=4*n
        out.append((t,c,s,ro,bins))
    return out
