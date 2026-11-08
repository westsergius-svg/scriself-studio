import tarfile, pathlib, sqlite3, gzip, json, hashlib
root=pathlib.Path(__file__).resolve().parents[1]
art=root/"artifacts/text-tools-20261104"
assert hashlib.sha256((art/"wiki_ru_wordnet.tar.gz").read_bytes()).hexdigest() == "1c7d55a309f0813cf0dba1db7c814a3742fe7191e6b37ddf3293b6e8ea0b3ac8"
with tarfile.open(art/"wiki_ru_wordnet.tar.gz") as tf:
    for m in tf.getmembers():
        if m.name.endswith("/database/wikiwordnet.db"):
            (art/"wikiwordnet.db").write_bytes(tf.extractfile(m).read())
        if m.name.endswith("/PKG-INFO") and ".egg-info/" not in m.name:
            (art/"wiki-ru-wordnet-PKG-INFO.txt").write_bytes(tf.extractfile(m).read())
db=sqlite3.connect("file:"+str(art/"wikiwordnet.db")+"?mode=ro",uri=True)
print(db.execute("select count(*),count(distinct synset_id),count(distinct lemma) from synsets").fetchone())
groups={}
for sid,word in db.execute("select distinct synset_id, lemma from synsets order by synset_id,lemma"):
    groups.setdefault(sid,[]).append(word)
filtered=[words for words in groups.values() if len(words)>1]
data=root/"src/ScriSelfStudio.Persistence/Language/Data"
data.mkdir(parents=True,exist_ok=True)
payload=json.dumps(filtered,ensure_ascii=False,separators=(",",":")).encode("utf8")
(data/"wiki-synonyms.json.gz").write_bytes(gzip.compress(payload,mtime=0))
print("Synonym groups",len(filtered),"bytes",len(payload),"compressed", (data/"wiki-synonyms.json.gz").stat().st_size)

