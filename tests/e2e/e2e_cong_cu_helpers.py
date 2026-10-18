"""Helper tao du an theo loai du lieu / tap nhan (tach tu e2e_cong_cu.py de bo khac dung lai)."""
import os, subprocess, sys, uuid
import requests

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tro_giup import G, TAG, cho, vi  # noqa: E402
import tao_media  # noqa: E402

M = os.path.join(os.path.dirname(os.path.abspath(__file__)), "media")
tao_media.dam_bao(M)
GIA = 1000


def nap(u, so):
    k = uuid.uuid4().hex
    d = requests.post(G + "/payments/deposits", headers=dict(u["h"], **{"Idempotency-Key": k}), json={"amountVnd": so}).json()
    requests.post(G + f"/payments/sandbox/checkout/{d['intentId']}/pay?amount={so}")
    assert cho(lambda: vi(u)["businessAvailableVnd"] >= so, 30), vi(u)


def anh_mau(ten, mau, rong, cao):
    """Sinh mot anh mot mau bang ffmpeg (moi mau mot noi dung khac nhau — project-svc bo anh trung)."""
    p = os.path.join(M, ten)
    if not os.path.exists(p):
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", f"color=c={mau}:size={rong}x{cao}:duration=1",
                        "-frames:v", "1", p], check=True)
    return p


def du_an(chu, ten, modality, tap_nhan, rows=None, files=None, red=2, tran=3, gold_pct=0, dieu_kien=None, suat=None, nguong=60):
    """Tao du an nhap + nap du lieu. files: duong dan file (du lieu dang file); rows: dong manifest
    (du lieu dang noi dung). Tra (pid, lo du lieu, {ten mau: mau}).

    Gia CA DU AN (3.18): thuong = GIA x so mau (toi thieu 10.000), so suat = max(3, tran) tru khi truyen `suat`.
    red / gold_pct giu de goi tuong thich, khong con nghia."""
    r = requests.post(G + "/projects", headers=chu["h"], json={"name": f"[e2e-cong-cu] {ten} {TAG}", "description": "e2e", "modality": modality, "visibility": "public"})
    assert r.status_code in (200, 201), r.text
    pid = r.json()["id"]
    r = requests.put(G + f"/projects/{pid}/label-schema", headers=chu["h"], json=tap_nhan)
    assert r.status_code == 200, r.text
    requests.put(G + f"/projects/{pid}/guideline", headers=chu["h"], json={"markdown": "e2e", "examples": []}).raise_for_status()
    if dieu_kien:
        requests.put(G + f"/projects/{pid}/eligibility", headers=chu["h"], json=dieu_kien).raise_for_status()

    if files is not None:
        r = requests.post(G + f"/projects/{pid}/uploads", headers=chu["h"],
                          json={"files": [{"name": os.path.basename(f), "sizeBytes": os.path.getsize(f)} for f in files]})
        assert r.status_code == 200, r.text
        rows = []
        for o, f in zip(r.json(), files):
            with open(f, "rb") as fh:
                requests.put(o["uploadUrl"], data=fh.read()).raise_for_status()
            rows.append({"file": o["key"], "name": os.path.basename(f)})

    r = requests.post(G + f"/projects/{pid}/datasets/manifest", headers=chu["h"], json={"name": "lo", "rows": rows})
    assert r.status_code == 202, r.text
    did = r.json()["id"]
    lo = cho(lambda: next((d for d in requests.get(G + f"/projects/{pid}/datasets", headers=chu["h"]).json()
                           if d["id"] == did and d["status"] in ("ready", "failed")), None), 90)
    assert lo is not None, "lo du lieu chua xu ly xong"
    mau = {m["originalName"]: m for m in requests.get(G + f"/projects/{pid}/samples?pageSize=200", headers=chu["h"]).json()["items"]}

    so = max(1, len(mau))
    requests.put(G + f"/projects/{pid}/pricing", headers=chu["h"], json={
        "rewardVnd": max(10000, GIA * so), "slots": suat if suat is not None else max(3, tran), "passThresholdPercent": nguong,
        "deadline": "2027-12-31T00:00:00Z"}).raise_for_status()
    return pid, lo, mau
