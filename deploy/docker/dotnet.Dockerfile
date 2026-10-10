# syntax=docker/dockerfile:1
# Mot Dockerfile cho MOI service .NET. Ngu canh build = goc repo (can shared/, contracts/,
# Directory.*.props). docker compose truyen:
#   APP     = ten project Api (vd Crowd.Tasking.Api) — vua la thu muc publish vua la ten dll
#   FFMPEG  = true neu service can ffprobe (project-svc do thoi luong audio / video)
#
# Stage build KHONG phu thuoc APP: publish MOI service mot lan. Compose build 10 image song
# song tu cung stage nay — BuildKit chi chay no MOT lan va dung chung (truoc day moi image tu
# restore + build rieng, 10 tien trinh cung ghi mot cache NuGet thi tranh nhau giai nen goi).

ARG DOTNET_TAG=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_TAG} AS build
WORKDIR /src
COPY . .
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore datn.slnx \
    && for p in services/*/Crowd.*.Api/Crowd.*.Api.csproj; do \
         ten=$(basename "$p" .csproj); \
         dotnet publish "$p" -c Release --no-restore -o "/out/$ten" || exit 1; \
       done

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_TAG} AS runtime
ARG FFMPEG=false
RUN if [ "$FFMPEG" = "true" ]; then \
      apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*; \
    fi
ARG APP
WORKDIR /app
COPY --from=build /out/${APP} .
ENV APP_DLL=${APP}.dll
# Demo / CI chay o Development: du lieu seed, cong thanh toan sandbox, khoa Turnstile test.
# Production PHAI dat lai ASPNETCORE_ENVIRONMENT va moi bi mat bang bien moi truong.
ENV ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
