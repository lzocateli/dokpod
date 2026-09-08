---
name: "Contratos de API e agente"
description: "Use ao alterar OpenAPI, Protocol Buffers, DTOs públicos, eventos, paginação ou compatibilidade."
applyTo: "contracts/**, backend/apps/api/**, frontend/**/data-access/**"
---

# Contratos

- OpenAPI 3.1 é a fonte da API browser; Protocol Buffers é a fonte do canal agente.
- Contratos têm versão explícita, exemplos e erros documentados.
- Use IDs opacos, timestamps UTC e enums com valor desconhecido tolerável.
- Não exponha paths de socket, entidades EF, stack traces ou payload bruto do engine.
- Todo endpoint declara autenticação, scope Keycloak e comportamento deny-by-default.
- Ambientes usam recursos opacos no Keycloak; DTOs não contêm memberships ou políticas copiadas.
- Erros HTTP usam `application/problem+json` e correlation ID seguro.
- Coleções extensas usam paginação por cursor e limites máximos.
- Operações mutáveis aceitam ID idempotente, hash imutável, deadline, revisão esperada e documentam estado assíncrono.
- O handshake do agente anuncia protocolo, engine, SO, arquitetura e capabilities.
- O protocolo do agente usa mTLS e nunca transporta access token ou refresh token de usuário.
- Evolua por adição compatível; tags protobuf removidas nunca são reutilizadas. Remoções exigem depreciação, ADR, migration expand-contract e teste N/N-1.
- Clientes são gerados; alterações manuais em arquivos gerados são proibidas.
- Valide contratos com testes de provider/consumer e exemplos sem dados reais.