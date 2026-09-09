# syntax=docker/dockerfile:1
FROM lzocateli/angular-cli:22.1.0-node24.15.0-bookworm AS build
WORKDIR /src

COPY frontend/web/package.json frontend/web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY frontend/web/ ./
RUN npm run build -- --configuration production --base-href /dokpod/

FROM lzocateli/nginx:1.28.0-bookworm AS runtime
LABEL org.opencontainers.image.title="dokpod-web" \
    org.opencontainers.image.description="Aplicação web estática do Dokpod." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/tree/main/deploy/e2e" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod"
COPY deploy/nginx/conf.d/web.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/dokpod-web/browser/ /usr/share/nginx/html/
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && sed -i 's#^[[:space:]]*pid[[:space:]].*#pid /tmp/nginx.pid;#' /etc/nginx/nginx.conf \
    && chown -R nginx:nginx /usr/share/nginx/html /var/cache/nginx /var/log/nginx /var/run
USER nginx
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=6 CMD curl --fail --silent http://127.0.0.1:8080/ || exit 1
