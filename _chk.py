# -*- coding: utf-8 -*-
import urllib.request, urllib.error, re
def probe(url):
    try:
        req=urllib.request.Request(url, headers={'User-Agent':'Mozilla/5.0'})
        r=urllib.request.urlopen(req,timeout=25)
        b=r.read()
        ct=r.headers.get('Content-Type','')
        txt=b.decode('utf-8',errors='replace')
        print('OK', r.status, 'ct=', ct, 'len=', len(b))
        return txt
    except urllib.error.HTTPError as e:
        print('HTTPError', e.code)
    except Exception as e:
        print('ERR', repr(e))
    return None

lst = probe('https://scriborium.ru/uploads/scrisoft/')
if lst:
    for m in re.finditer(r'href=["\x27]([^"\x27#?]+)["\x27]', lst):
        print('  href:', m.group(1))
man = probe('https://scriborium.ru/uploads/scrisoft/latest.json')
if man:
    print('  manifest:', man.strip()[:500])