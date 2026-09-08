---
name: "Docker Compose"
description: "Use ao criar ou alterar Docker Compose do Dokpod. Cobre serviços, redes, health dependencies, bind mounts, secrets, restart e perfis."
applyTo: "**/compose*.yml, **/compose*.yaml"
---

# Docker Compose

- Separe web, BFF, API, agente, PostgreSQL e serviços de identidade conforme o ambiente.
- Publique somente web/proxy por padrão; APIs internas, banco e agentes ficam em redes restritas.
- Não exponha socket Docker/Podman pela rede nem adicione proxy genérico do engine.
- Use tags imutáveis e secrets externos; nunca versione valores em Compose, imagens ou configuração.
- Toda persistência usa bind mount documentado ou volume explicitamente justificado; não remova volumes implicitamente ao excluir containers.
- Use `depends_on` com health somente para ordenar dependências reais; os serviços devem tolerar reconexão.
- Defina health checks, restart policy, limites e `stop_grace_period` coerentes com cada processo.
- Valide bootstrap limpo, readiness, restart, recriação com persistência e shutdown gracioso.
- Agentes devem operar com o menor privilégio possível e anunciar capabilities sem presumir equivalência Docker/Podman.
