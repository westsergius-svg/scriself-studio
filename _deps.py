import struct, os

def read_imports(path):
    data=open(path,'rb').read()
    pe=struct.unpack_from('<I',data,0x3c)[0]
    coff=pe+4
    numsec=struct.unpack_from('<H',data,coff+2)[0]
    size_opt=struct.unpack_from('<H',data,coff+16)[0]
    opt=coff+20
    magic=struct.unpack_from('<H',data,opt)[0]
    is64=magic==0x20b
    # data directories: PE32+ -> 112; PE32 -> 96
    num_dir=struct.unpack_from('<I',data,opt+(108 if is64 else 92))[0]
    dd_base=opt+(112 if is64 else 96)
    import_rva=struct.unpack_from('<I',data,dd_base+1*8)[0]
    sec_off=opt+size_opt
    secs=[]
    for i in range(numsec):
        off=sec_off+i*40
        vaddr=struct.unpack_from('<I',data,off+12)[0]
        vsize=struct.unpack_from('<I',data,off+8)[0]
        rawptr=struct.unpack_from('<I',data,off+20)[0]
        rawsize=struct.unpack_from('<I',data,off+16)[0]
        secs.append((vaddr,vsize,rawptr,rawsize))
    def rva2off(rva):
        for (vaddr,vsize,rawptr,rawsize) in secs:
            if vaddr<=rva<vaddr+max(vsize,rawsize):
                return rawptr+(rva-vaddr)
        return None
    o=rva2off(import_rva)
    if o is None: return None, import_rva
    dlls=[]
    idx=0
    while True:
        ent=o+idx*20
        name_rva=struct.unpack_from('<I',data,ent+12)[0]
        if name_rva==0: break
        no=rva2off(name_rva)
        end=data.find(b'\x00',no)
        dlls.append(data[no:end].decode('latin1'))
        idx+=1
    return dlls, import_rva

internal=r"D:\2\project\Programm\scriboriium_beta\src\dist\Scriborium\_internal"
qdll=r"C:\Users\Papa\AppData\Local\Python\pythoncore-3.14-64\Lib\site-packages\PySide6\Qt6Core.dll"
res=read_imports(qdll)
print("res:", res[0])
if res[0]:
    allfiles=set()
    for root,dirs,files in os.walk(internal):
        for f in files: allfiles.add(f.lower())
    for d in res[0]:
        print("  ", d, "->", "OK" if d.lower() in allfiles else "MISSING")
