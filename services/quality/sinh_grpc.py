"""Sinh stub gRPC Python tu contracts/grpc (proto dung chung voi cac service C#): client cua project-svc
QualityData, server LabelAggregation (project-svc goi).

Chay lai moi khi sua file .proto (grpcio-tools chi can luc sinh, khong nam trong requirements.txt):
    .venv/Scripts/python -m pip install "grpcio-tools==1.76.*"
    .venv/Scripts/python sinh_grpc.py

Stub duoc COMMIT (app/grpc_gen): image Docker va test khong can grpcio-tools. Phien ban grpcio-tools phai
khop grpcio / protobuf ghim trong requirements.txt (stub kiem phien ban runtime luc import).
"""

import re
import sys
from pathlib import Path

from grpc_tools import protoc

GOC = Path(__file__).resolve().parent
PROTO_ROOT = GOC.parents[1] / "contracts" / "grpc"
RA = GOC / "app" / "grpc_gen"
FILES = ["project/v1/quality_data.proto", "quality/v1/label_aggregation.proto"]


def main() -> int:
    RA.mkdir(parents=True, exist_ok=True)
    include = Path(protoc.__file__).resolve().parent / "_proto"
    for f in FILES:
        kq = protoc.main(["grpc_tools.protoc", f"-I{PROTO_ROOT}", f"-I{include}", f"--python_out={RA}",
                          f"--pyi_out={RA}", f"--grpc_python_out={RA}", str(PROTO_ROOT / f)])
        if kq != 0:
            return kq

    # protoc sinh "from project.v1 import x_pb2" (import tuyet doi theo goc proto) → doi ve goi app.grpc_gen.
    for py in RA.rglob("*_pb2_grpc.py"):
        s = py.read_text(encoding="utf-8")
        s = re.sub(r"^from (project|quality)\.", r"from app.grpc_gen.\1.", s, flags=re.M)
        py.write_text(s, encoding="utf-8")
    for d in [RA, *[p for p in RA.rglob("*") if p.is_dir()]]:
        (d / "__init__.py").touch()
    print("Da sinh stub vao", RA)
    return 0


if __name__ == "__main__":
    sys.exit(main())
