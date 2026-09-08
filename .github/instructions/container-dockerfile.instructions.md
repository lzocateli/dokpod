---
name: "Dockerfiles da Aplicação"
description: "Use ao criar, alterar ou revisar Dockerfiles e arquivos dockerignore do Dokpod. Cobre multi-stage, cache, usuário, secrets, labels e health check."
applyTo: "**/Dockerfile*, **/.dockerignore"
---

# Dockerfiles

- Use build multi-stage e imagem runtime mínima, suportada e fixada por versão.
- Copie manifests e lockfiles antes do código para preservar cache e restaure com modo reprodutível.
- Não instale SDK, compilador ou shell administrativo na imagem final sem necessidade operacional.
- Execute como usuário não root com UID/GID documentado quando o serviço permitir.
- Use filesystem read-only e diretórios temporários explícitos quando viável.
- Nunca passe secrets por `ARG`, `ENV`, camada ou contexto; use secret de build ou runtime.
- `.dockerignore` bloqueia `.git`, `.env`, secrets, backups, cobertura e artifacts locais.
- Declare labels OCI, porta e health check que prove saúde útil.
- Preserve encerramento gracioso; entrypoint usa `exec` e encaminha sinais.
- O agente Linux deve manter o contrato de socket/mount documentado e não ampliar privilégios sem justificativa.
- Valide BuildKit, build limpo, usuário, entrypoint, labels, porta, health e ausência de secrets.
