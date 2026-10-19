# syntax=docker/dockerfile:1
# ml-svc (Python) — nhung cau cho quality-svc cham cau tra loi mo theo nghia (kien truc 3.19).
# Mo hinh tai SAN luc build vao /models: container chay khong can Internet, khoi dong chi mat thoi gian nap.
FROM python:3.12-slim
WORKDIR /srv/services/ml
COPY services/ml/requirements.txt .
RUN --mount=type=cache,id=pip,target=/root/.cache/pip pip install -r requirements.txt
ARG ML_MODEL_NAME=sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2
ENV ML_MODEL_NAME=${ML_MODEL_NAME} ML_MODEL_CACHE_DIR=/models HF_HUB_OFFLINE=0
RUN python -c "import os; from fastembed import TextEmbedding; TextEmbedding(os.environ['ML_MODEL_NAME'], cache_dir='/models')"
ENV HF_HUB_OFFLINE=1
COPY services/ml/app ./app
COPY services/ml/migrations ./migrations
ENV PYTHONUNBUFFERED=1
CMD ["python", "-m", "app.main"]
