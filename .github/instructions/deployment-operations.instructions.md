---
name: "Operação e Deployment"
description: "Use ao alterar arquivos operacionais em deploy, proxy reverso, configuração runtime, bind mounts, backup, restore, rollout ou runbooks do Dokpod."
applyTo: "deploy/**"
---

# Operação e deployment

- Diferencie os perfis local, equipe interna e ambiente isolado; a imagem permanece idêntica entre ambientes.
- Documente paths absolutos, owner/permissões, sockets e requisitos de bind mounts.
- TLS termina no proxy; valide forwarded headers e hosts confiáveis.
- Readiness bloqueia tráfego até dependências, schema e identidade estarem prontas.
- Migração é etapa exclusiva e observável antes da promoção da nova API.
- Rollout inclui preflight, backup, migration, smoke test e verificação de métricas.
- Rollback não depende de downgrade destrutivo de schema; use compatibilidade expand-contract.
- Backups cobrem PostgreSQL, configuração e estado necessário à operação; execute restore em ambiente limpo.
- Tags de imagem são imutáveis; release inclui SBOM, provenance, scan e digest.
- Valide Docker e Podman separadamente quando a capability não for equivalente.
- Não faça publicação ou deploy sem solicitação explícita e credenciais já configuradas.
