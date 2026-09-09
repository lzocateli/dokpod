# syntax=docker/dockerfile:1
FROM lzocateli/dotnet-sdk:10.0.400-noble AS build
USER root
WORKDIR /src

COPY global.json ./
COPY backend/Directory.Build.props backend/Directory.Packages.props ./backend/
COPY contracts/agent/ contracts/agent/
COPY backend/libs/Dokpod.Agent.Contracts/Dokpod.Agent.Contracts.csproj backend/libs/Dokpod.Agent.Contracts/
COPY backend/libs/Dokpod.ControlPlane.Application/Dokpod.ControlPlane.Application.csproj backend/libs/Dokpod.ControlPlane.Application/
COPY backend/libs/Dokpod.Domain/Dokpod.Domain.csproj backend/libs/Dokpod.Domain/
COPY backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj backend/apps/Dokpod.ControlPlane.Api/
RUN dotnet restore backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM lzocateli/dotnet-aspnet:10.0.11-noble AS runtime
LABEL org.opencontainers.image.title="dokpod-api" \
    org.opencontainers.image.description="API do plano de controle Dokpod para protocolo de agentes e health do laboratório." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/tree/main/deploy/e2e" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod"
WORKDIR /app
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 7443 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=6 CMD curl --fail --silent http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Dokpod.ControlPlane.Api.dll"]