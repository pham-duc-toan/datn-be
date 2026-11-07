"""Loi tra ve dang ProblemDetails co truong "code" — cung hinh dang voi cac service C#."""

from fastapi import Request
from fastapi.responses import JSONResponse

_TYPE = {
    400: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    401: "https://tools.ietf.org/html/rfc9110#section-15.5.2",
    403: "https://tools.ietf.org/html/rfc9110#section-15.5.4",
    404: "https://tools.ietf.org/html/rfc9110#section-15.5.5",
    409: "https://tools.ietf.org/html/rfc9110#section-15.5.10",
    503: "https://tools.ietf.org/html/rfc9110#section-15.6.4",
}


class LoiApi(Exception):
    def __init__(self, status: int, code: str, detail: str) -> None:
        super().__init__(detail)
        self.status = status
        self.code = code
        self.detail = detail


async def xu_ly_khong_goi_duoc(request: Request, ex: Exception) -> JSONResponse:
    """project-svc (gRPC) tam khong tra loi — giong IdentityClient ben C#: 503, client thu lai."""
    return await xu_ly_loi_api(request, LoiApi(503, "du_lieu_du_an_tam_thoi_khong_doc_duoc",
                                               "Tạm thời không đọc được dữ liệu dự án, vui lòng thử lại sau."))


async def xu_ly_loi_api(request: Request, ex: LoiApi) -> JSONResponse:
    return JSONResponse(
        status_code=ex.status,
        content={"type": _TYPE.get(ex.status), "title": ex.code, "status": ex.status, "detail": ex.detail, "code": ex.code},
        media_type="application/problem+json",
    )
