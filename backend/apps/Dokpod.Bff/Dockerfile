# syntax=docker/dockerfile:1
ARG DOTNET_SDK_IMAGE=lzocateli/dotnet-sdk:10.0.400-noble
ARG DOTNET_RUNTIME_IMAGE=lzocateli/dotnet-aspnet:10.0.11-noble
FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src
COPY --chown=${APP_UID}:${APP_UID} backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Bff/Dokpod.Bff.csproj backend/apps/Dokpod.Bff/
RUN dotnet restore backend/apps/Dokpod.Bff/Dokpod.Bff.csproj
COPY --chown=${APP_UID}:${APP_UID} backend/apps/Dokpod.Bff/ backend/apps/Dokpod.Bff/
RUN dotnet publish backend/apps/Dokpod.Bff/Dokpod.Bff.csproj --configuration Release --no-restore --output /src/out /p:UseAppHost=false
FROM ${DOTNET_RUNTIME_IMAGE} AS runtime
ARG IMAGE_VERSION=0.1.0-dev
LABEL org.opencontainers.image.title="Dokpod BFF" org.opencontainers.image.description="BFF Dokpod para sessão OIDC server-side e proteção antiforgery." org.opencontainers.image.source="https://github.com/lzocateli/dokpod" org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/blob/main/docs/arquitetura.md" org.opencontainers.image.url="https://github.com/lzocateli/dokpod" org.opencontainers.image.version="${IMAGE_VERSION}"
USER root
RUN apt-get update \
	&& apt-get install --yes --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/* \
	&& install --directory --owner="${APP_UID}" --group="${APP_UID}" --mode=0700 /var/lib/dokpod-bff/keys
COPY --from=build --chown=${APP_UID}:${APP_UID} /src/out/ /app/
USER ${APP_UID}:${APP_UID}
VOLUME ["/var/lib/dokpod-bff"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD curl --fail --silent http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Dokpod.Bff.dll"]