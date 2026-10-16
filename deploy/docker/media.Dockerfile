# syntax=docker/dockerfile:1
# media-svc (Python) — bao ve du lieu: phat file mau qua token, watermark chim.
# Ngu canh build = goc repo: can shared/settings/catalog.json. Giu bo cuc thu muc nhu repo vi
# settings_store tim catalog theo duong dan tuong doi.
FROM python:3.12-slim
# libglib2.0-0: OpenCV headless can. ffmpeg: nhung / giai watermark am thanh, video (watermark_av.py).
RUN apt-get update \
    && apt-get install -y --no-install-recommends libglib2.0-0 ffmpeg \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /srv/services/media
COPY services/media/requirements.txt .
RUN --mount=type=cache,id=pip,target=/root/.cache/pip pip install -r requirements.txt
COPY shared/settings/catalog.json /srv/shared/settings/catalog.json
COPY services/media/app ./app
ENV PYTHONUNBUFFERED=1
CMD ["python", "-m", "app.main"]
