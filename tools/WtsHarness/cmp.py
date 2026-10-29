import json, sys
a = json.load(open("old.json")); b = json.load(open("new.json"))
assert len(a) == len(b), (len(a), len(b))
def norm_raw(r):   # raw SQL log: bỏ phần khoá (cách khoá khác có chủ đích), giữ UPDATE
    return [x for x in r if x.startswith("UPDATE")]
diff = 0
for sa, sb in zip(a, b):
    name = sa["buoc"]
    if sa["loi"] != sb["loi"]:
        print(f"!! LOI KHAC [{name}]\n   cu : {sa['loi']}\n   moi: {sb['loi']}"); diff += 1
    da, db = sa["dump"], sb["dump"]
    if da is None or db is None: continue
    for k in ["wts", "stds", "extras", "cases", "acts"]:
        if da[k] != db[k]:
            diff += 1
            print(f"!! DB KHAC [{name}] bảng {k}")
            for i, (x, y) in enumerate(zip(da[k], db[k])):
                if x != y: print("   cu :", x, "\n   moi:", y)
            if len(da[k]) != len(db[k]): print("   số dòng", len(da[k]), len(db[k]))
    if norm_raw(da["raw"]) != norm_raw(db["raw"]):
        diff += 1; print(f"!! UPDATE raw khác [{name}]", norm_raw(da["raw"])[-3:], norm_raw(db["raw"])[-3:])
last = b[-1]["dump"]
print(f"so sánh {len(a)} bước · khác: {diff} · cuối: wts={len(last['wts'])} stds={len(last['stds'])} extras={len(last['extras'])} cases={len(last['cases'])} acts={len(last['acts'])}")
# thống kê khoá: bản cũ sp_getapplock Session vs bản mới Transaction
ra = [x for x in a[-1]["dump"]["raw"] if "applock" in x]; rb = [x for x in b[-1]["dump"]["raw"] if "applock" in x]
print("khoá cũ:", len(ra), "lệnh (Session);", "khoá mới:", len(rb), "lệnh (Transaction):", sum('Transaction' in x for x in rb))
