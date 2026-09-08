# syntax=docker/dockerfile:1

ARG DOTNET_SDK_IMAGE=lzocateli/dotnet-sdk:10.0.400-noble
ARG DOTNET_RUNTIME_IMAGE=lzocateli/dotnet-aspnet:10.0.11-noble

FROM ${DOTNET_SDK_IMAGE} AS build

WORKDIR /src

COPY --chown=${APP_UID}:${APP_UID} backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Bff/Dokpod.Bff.csproj backend/apps/Dokpod.Bff/

RUN dotnet restore backend/apps/Dokpod.Bff/Dokpod.Bff.csproj

COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Bff/ backend/apps/Dokpod.Bff/

RUN dotnet publish backend/apps/Dokpod.Bff/Dokpod.Bff.csproj \
    --configuration Release \
    --no-restore \
    --output /src/out \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS runtime

ARG IMAGE_VERSION=0.1.0-dev

LABEL org.opencontainers.image.title="Dokpod BFF" \
    org.opencontainers.image.description="Backend for Frontend do Dokpod para sessões seguras do navegador." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/blob/main/docs/distribuicao.md" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.version="${IMAGE_VERSION}"

COPY --from=build --chown=${APP_UID}:${APP_UID} /src/out/ /app/

USER ${APP_UID}:${APP_UID}

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:5000/health/live || exit 1

ENTRYPOINT ["dotnet", "Dokpod.Bff.dll"]