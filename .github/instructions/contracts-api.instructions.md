---
name: "Contratos e API"
description: "Use ao criar ou alterar OpenAPI, endpoints, schemas JSON, DTOs públicos, paginação, erros, eventos SignalR ou clientes gerados."
applyTo: "contracts/**, backend/apps/Dokpod.ControlPlane.Api/**/*.cs, frontend/web/**/data-access/**"
---

# Contratos públicos

- OpenAPI em `contracts/openapi` descreve a API pública versionada.
- O protocolo de agentes em `contracts/agent` mantém compatibilidade N/N-1 quando houver rollout distribuído.
- Gere clientes e compare artefatos no CI; não edite código gerado.
- Modele recursos e sub-recursos com substantivos; não exponha RPC ou verbos de implementação nas rotas HTTP.
- Use `GET` para leitura, `POST` para criação, `PUT` para substituição idempotente, `PATCH` para alteração parcial e `DELETE` para remoção.
- Toda operação deve documentar autenticação, autorização, parâmetros, respostas, erros e headers relevantes.
- Erros usam `application/problem+json` com `type`, `title`, `status`, `detail`, `instance`, `traceId` e código estável quando necessário.
- Listas mutáveis usam paginação por cursor e limites máximos.
- IDs são opacos para clientes e datas são ISO 8601 UTC.
- Não exponha entity models, nomes de tabela, stack traces, paths físicos, tokens ou certificados.
- Mudança aditiva preserva consumidores; breaking change exige nova versão ou estratégia de transição documentada.
- Testes de contrato validam serialização, status, headers, autorização, compatibilidade e geração de clientes.
