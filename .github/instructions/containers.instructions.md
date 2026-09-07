---
name: "Containers e deployment"
description: "Use ao alterar Dockerfiles, Compose, imagens OCI, health checks, mounts, entrypoints ou deployment."
applyTo: "deploy/**, **/Dockerfile*, **/compose*.yml, **/compose*.yaml, **/.dockerignore"
---

# Containers

- Use builds multi-stage, imagens base oficiais e tags/digests reproduzíveis.
- Backend, BFF, frontend e agente Linux sempre executam em containers em desenvolvimento, testes integrados e produção.
- Não crie imagem Windows para o agente; publique Worker Service self-contained por RID homologado.
- Execute como usuário não root quando compatível com o acesso ao socket.
- Monte somente o socket necessário no agente e documente que ele concede privilégio elevado.
- Nunca monte sockets no frontend, API pública ou workloads auxiliares.
- Não exponha Docker TCP ou Podman API sem mTLS; o desenho padrão não os expõe.
- Não inclua secrets, certificados privados ou arquivos de desenvolvimento em camadas.
- Configure filesystem read-only e capabilities removidas quando possível.
- Defina health check real, shutdown gracioso e limites de recursos.
- Gere SBOM, faça scan e assine imagens e pacotes; instalação verifica assinatura, provenance, emissor e digest/versão permitidos.
- Teste imagens com Docker Linux e Podman rootless; teste o pacote Windows em Windows Server sem runtime .NET.
- Mudança de privilégio, mount ou rede exige revisão de segurança.