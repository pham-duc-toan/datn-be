"""Ham thuan cua views: doc header Range (tua am thanh / video)."""

from app.views import _doc_range


def test_doc_range():
    assert _doc_range(None, 100) is None
    assert _doc_range("bytes=0-9", 100) == (0, 9)
    assert _doc_range("bytes=90-", 100) == (90, 99)
    assert _doc_range("bytes=-10", 100) == (90, 99)
    assert _doc_range("bytes=50-500", 100) == (50, 99)
    assert _doc_range("bytes=200-300", 100) is None
    assert _doc_range("bytes=0-1,5-6", 100) is None
    assert _doc_range("items=0-1", 100) is None
