# syntax=docker/dockerfile:1
# Build desde la raíz del repo: docker-compose build  (o: docker build -t velsat-mobile:latest .)

# ---------- Etapa 1: build / publish ----------
FROM mcr.microsoft.com/dotnet/sdk:6.0-bookworm-slim AS build
WORKDIR /src

# Restaurar primero (aprovecha la caché de capas)
COPY VelsatMobile.sln ./
COPY VelsatBackendAPI/VelsatMobile.csproj VelsatBackendAPI/
COPY VelsatBackendAPI.Data/VelsatMobile.Data.csproj VelsatBackendAPI.Data/
COPY VelsatBackendAPI.Model/VelsatMobile.Model.csproj VelsatBackendAPI.Model/
RUN dotnet restore VelsatBackendAPI/VelsatMobile.csproj

COPY . .
RUN dotnet publish VelsatBackendAPI/VelsatMobile.csproj -c Release -o /app/publish \
        --no-restore /p:UseAppHost=false \
    # La configuración se monta en tiempo de ejecución, nunca va dentro de la imagen
    && rm -f /app/publish/appsettings*.json /app/publish/firebase-credentials.json

# ---------- Etapa 2: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:6.0-bookworm-slim AS final

ENV TZ=America/Lima \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl ca-certificates \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 app \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin app

WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

USER 10001:10001
EXPOSE 8080

# No toca la base de datos: solo comprueba que Kestrel responde
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/swagger/v1/swagger.json -o /dev/null || exit 1

ENTRYPOINT ["dotnet", "VelsatMobile.dll"]
