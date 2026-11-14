# toggle.py prefix seconds kind a b dwell
# Runs compare-capture while switching one receive setting on Main between a and b
# every dwell seconds through YWC (kind: att, or a scope setting such as level),
# and logs when each switch happened. Receive settings only; nothing transmits.
# Ends on a. Needs YWC running on localhost:8080.
import subprocess,sys,time,json,urllib.request
pre,secs,kind,a,b,dwell=sys.argv[1],float(sys.argv[2]),sys.argv[3],sys.argv[4],sys.argv[5],float(sys.argv[6])
import os
DATA=os.environ.get('ACC_COMPARE_DATA','data'); PORT=os.environ.get('ACC_PORT','COM6')
def post(url,body):
    r=urllib.request.Request('http://localhost:8080'+url,json.dumps(body).encode(),{'Content-Type':'application/json'});urllib.request.urlopen(r,timeout=5).read()
def setv(v):
    if kind=='att': post('/api/cat/attenuator/main',{'code':v})
    else: post('/api/scope/main/'+kind,{'value':v})
setv(a); time.sleep(1)
p=subprocess.Popen(['dotnet','bin/Debug/net10.0/compare-capture.dll',f'{DATA}/{pre}',str(secs+2),PORT])
time.sleep(1.5); log=[]; end=time.time()+secs; i=0
while time.time()<end:
    v=[a,b][i%2]; t1=time.time(); setv(v); log.append((t1,time.time(),v)); i+=1; time.sleep(dwell)
setv(a); p.wait(); json.dump(log,open(f'{DATA}/{pre}-log.json','w'))
