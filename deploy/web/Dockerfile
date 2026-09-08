# syntax=docker/dockerfile:1

ARG ANGULAR_CLI_IMAGE=lzocateli/angular-cli:22.1.0-node24.15.0-bookworm
ARG NGINX_RUNTIME_IMAGE=lzocateli/nginx:1.28.0-bookworm
ARG APP_UID=1654

FROM ${ANGULAR_CLI_IMAGE} AS build

ARG APP_UID

USER root

WORKDIR /src

COPY --chown=${APP_UID}:${APP_UID} frontend/web/package.json frontend/web/package-lock.json frontend/web/

RUN npm ci --prefix frontend/web

COPY --chown=${APP_UID}:${APP_UID} frontend/web/ frontend/web/
COPY --chown=${APP_UID}:${APP_UID} frontend/libs/design-system/ frontend/libs/design-system/

RUN npm run build --prefix frontend/web -- --configuration production

FROM ${NGINX_RUNTIME_IMAGE} AS runtime

ARG IMAGE_VERSION=0.1.0-dev
ARG APP_UID

LABEL org.opencontainers.image.title="Dokpod Web" \
    org.opencontainers.image.description="Console operacional Angular do Dokpod." \
    org.opencontainers.image.source="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.documentation="https://github.com/lzocateli/dokpod/blob/main/docs/distribuicao.md" \
    org.opencontainers.image.url="https://github.com/lzocateli/dokpod" \
    org.opencontainers.image.version="${IMAGE_VERSION}"

COPY --from=build --chown=${APP_UID}:${APP_UID} /src/frontend/web/dist/dokpod-web/browser/ /usr/share/nginx/html/
COPY --chown=${APP_UID}:${APP_UID} deploy/web/nginx.conf /etc/nginx/nginx.conf

USER root
RUN install --directory --owner=${APP_UID} --group=${APP_UID} --mode=0700 \
    /tmp/client_temp /tmp/proxy_temp /tmp/fastcgi_temp /tmp/uwsgi_temp /tmp/scgi_temp

USER ${APP_UID}:${APP_UID}

EXPOSE 5000

HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://127.0.0.1:5000/health/live || exit 1

CMD ["nginx", "-g", "daemon off;"]