# syntax=docker/dockerfile:1
# quality-svc (Python). Ngu canh build = goc repo: can shared/settings/catalog.json (catalog setting
# xuat tu C#). Giu dung bo cuc thu muc nhu repo vi settings_store tim catalog theo duong dan tuong doi.
FROM python:3.12-slim
WORKDIR /srv/services/quality
COPY services/quality/requirements.txt .
RUN --mount=type=cache,id=pip,target=/root/.cache/pip pip install -r requirements.txt
COPY shared/settings/catalog.json /srv/shared/settings/catalog.json
COPY shared/seeding/quality-seed.json /srv/shared/seeding/quality-seed.json
COPY services/quality/app ./app
COPY services/quality/migrations ./migrations
ENV PYTHONUNBUFFERED=1
CMD ["python", "-m", "app.main"]
