# syntax=docker/dockerfile:1

ARG DOTNET_SDK_IMAGE=lzocateli/dotnet-sdk:10.0.400-noble
ARG DOTNET_RUNTIME_IMAGE=lzocateli/dotnet-aspnet:10.0.11-noble

FROM ${DOTNET_SDK_IMAGE} AS build

WORKDIR /src

COPY --chown=${APP_UID}:${APP_UID} backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj backend/apps/Dokpod.ControlPlane.Api/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Contracts/Dokpod.Agent.Contracts.csproj backend/libs/Dokpod.Agent.Contracts/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.ControlPlane.Application/Dokpod.ControlPlane.Application.csproj backend/libs/Dokpod.ControlPlane.Application/

RUN dotnet restore backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj

COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.ControlPlane.Api/ backend/apps/Dokpod.ControlPlane.Api/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Contracts/ backend/libs/Dokpod.Agent.Contracts/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.ControlPlane.Application/ backend/libs/Dokpod.ControlPlane.Application/
COPY --chown=${APP_UID}:${APP_UID} contracts/ contracts/

RUN dotnet publish backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /src/out \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime

ARG IMAGE_VERSION=0.1.0-dev

LABEL org.opencontainers.image.title="Dokpod Control Plane API" \
    org.opencontainers.image.description="API e canal gRPC autenticado do plano de controle Dokpod." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/blob/main/docs/distribuicao.md" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.version="${IMAGE_VERSION}"

COPY --from=build --chown=${APP_UID}:${APP_UID} /src/out/ /app/

USER ${APP_UID}:${APP_UID}

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:5000/health/live || exit 1

ENTRYPOINT ["dotnet", "Dokpod.ControlPlane.Api.dll"]