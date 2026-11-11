"""Cau hinh log: LOG_FORMAT=json → MOT dong JSON moi ban ghi (cung ten truong voi .NET JSON console:
Timestamp, LogLevel, Category, Message, Exception) de Filebeat gom vao Elasticsearch; mac dinh giu dang chu de doc khi chay tay."""
import json
import logging
import os
import sys
from datetime import datetime, timezone


# Ten muc log giong .NET (Information thay vi Info) de loc chung mot truong tren Kibana.
_MUC = {"INFO": "Information", "WARNING": "Warning", "ERROR": "Error", "CRITICAL": "Critical", "DEBUG": "Debug"}


class _JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        d = {
            "Timestamp": datetime.fromtimestamp(record.created, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{int(record.msecs):03d}Z",
            "LogLevel": _MUC.get(record.levelname, record.levelname),
            "Category": record.name,
            "Message": record.getMessage(),
        }
        if record.exc_info:
            d["Exception"] = self.formatException(record.exc_info)
        return json.dumps(d, ensure_ascii=False)


def cau_hinh_log() -> None:
    if os.getenv("LOG_FORMAT", "").lower() == "json":
        h = logging.StreamHandler(sys.stdout)
        h.setFormatter(_JsonFormatter())
        logging.basicConfig(level=logging.INFO, handlers=[h], force=True)
    else:
        logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
