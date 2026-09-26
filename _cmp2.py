import struct, os
# load shiboken6 / QtCore deps from frozen vs source
def imports(path):
    data=open(path,'rb').read()
    pe=struct.unpack_from('<I',data,0x3c)[0]
    coff=pe+4
    numsec=struct.unpack_from('<H',data,coff+2)[0]
    size_opt=struct.unpack_from('<H',data,coff+16)[0]
    opt=coff+20
    magic=struct.unpack_from('<H',data,opt)[0]
    is64=magic==0x20b
    dd_base=opt+(112 if is64 else 96)
    import_rva=struct.unpack_from('<I',data,dd_base+1*8)[0]
    sec_off=opt+size_opt
    secs=[]
    for i in range(numsec):
        off=sec_off+i*40
        vaddr=struct.unpack_from('<I',data,off+12)[0]; vsize=struct.unpack_from('<I',data,off+8)[0]
        rawptr=struct.unpack_from('<I',data,off+20)[0]; rawsize=struct.unpack_from('<I',data,off+16)[0]
        secs.append((vaddr,vsize,rawptr,rawsize))
    def r2o(rva):
        for (vaddr,vsize,rawptr,rawsize) in secs:
            if vaddr<=rva<vaddr+max(vsize,rawsize): return rawptr+(rva-vaddr)
        return None
    o=r2o(import_rva)
    out=[]
    idx=0
    while True:
        ent=o+idx*20
        nrva=struct.unpack_from('<I',data,ent+12)[0]
        if nrva==0: break
        no=r2o(nrva); end=data.find(b'\x00',no); out.append(data[no:end].decode('latin1')); idx+=1
    return out

fz = r"D:\2\project\Programm\scriboriium_beta\src\dist\mini\_internal\PySide6"
src = r"C:\Users\Papa\AppData\Local\Python\pythoncore-3.14-64\Lib\site-packages\PySide6"
print("=== QtCore.pyd deps comparison ===")
a=imports(os.path.join(src,'QtCore.pyd')); b=imports(os.path.join(fz,'QtCore.pyd'))
print("src:", sorted(set(a)))
print("frozen:", sorted(set(b)))
print("only in frozen:", sorted(set(b)-set(a)))
print("missing in frozen:", sorted(set(a)-set(b)))
