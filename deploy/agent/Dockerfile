# syntax=docker/dockerfile:1

ARG DOTNET_SDK_IMAGE=lzocateli/dotnet-sdk:10.0.400-noble
ARG DOTNET_RUNTIME_IMAGE=lzocateli/dotnet-aspnet:10.0.11-noble

FROM ${DOTNET_SDK_IMAGE} AS build

WORKDIR /src

COPY --chown=${APP_UID}:${APP_UID} backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Agent/Dokpod.Agent.csproj backend/apps/Dokpod.Agent/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Application/Dokpod.Agent.Application.csproj backend/libs/Dokpod.Agent.Application/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Infrastructure/Dokpod.Agent.Infrastructure.csproj backend/libs/Dokpod.Agent.Infrastructure/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Domain/Dokpod.Domain.csproj backend/libs/Dokpod.Domain/

RUN dotnet restore backend/apps/Dokpod.Agent/Dokpod.Agent.csproj

COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Agent/ backend/apps/Dokpod.Agent/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Application/ backend/libs/Dokpod.Agent.Application/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Agent.Infrastructure/ backend/libs/Dokpod.Agent.Infrastructure/
COPY --chown=${APP_UID}:${APP_UID} backend/libs/Dokpod.Domain/ backend/libs/Dokpod.Domain/

RUN dotnet publish backend/apps/Dokpod.Agent/Dokpod.Agent.csproj \
    --configuration Release \
    --no-restore \
    --output /src/out \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime

ARG IMAGE_VERSION=0.1.0-dev

LABEL org.opencontainers.image.title="Dokpod Agent" \
    org.opencontainers.image.description="Agente Dokpod para inventário e operações permitidas em containers locais." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/blob/main/docs/distribuicao.md" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.version="${IMAGE_VERSION}"

USER root
RUN install --directory --owner="${APP_UID}" --group="${APP_UID}" --mode=0700 /var/lib/dokpod-agent

COPY --from=build --chown=${APP_UID}:${APP_UID} /src/out/ /app/

USER ${APP_UID}:${APP_UID}

VOLUME ["/var/lib/dokpod-agent"]

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD test -f /var/lib/dokpod-agent/health/ready.json || exit 1

ENTRYPOINT ["dotnet", "Dokpod.Agent.dll"]