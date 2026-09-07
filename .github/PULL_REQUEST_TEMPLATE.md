## Objetivo

<!-- Problema resolvido e resultado observável. -->

## Escopo

<!-- Mudanças e itens deliberadamente excluídos. -->

## Issue e decisões

- Issue:
- ADR:

## Áreas

- [ ] Frontend Angular
- [ ] API .NET
- [ ] BFF/Keycloak
- [ ] Agente .NET
- [ ] Contrato OpenAPI/gRPC
- [ ] PostgreSQL/migration
- [ ] Segurança
- [ ] Container/operação
- [ ] Documentação

## Evidências

- [ ] Formatação e análise estática
- [ ] Build de produção
- [ ] Testes unitários
- [ ] Testes de integração com engine/PostgreSQL real
- [ ] Testes de contrato e compatibilidade N/N-1
- [ ] Playwright desktop/mobile
- [ ] Build e smoke test das imagens afetadas
- [ ] Publicação e smoke test do agente Windows self-contained

Comandos e resultados:

## Segurança e operação

- [ ] Autenticação e autorização foram avaliadas
- [ ] Recursos/scopes Keycloak e comportamento fail-closed foram avaliados
- [ ] Tokens permanecem fora do browser e não chegam ao agente
- [ ] Idempotência, timeout e reconexão foram avaliados
- [ ] Nenhum socket foi exposto nem proxy genérico adicionado
- [ ] Nenhum secret ou dado sensível foi incluído
- [ ] Dependências possuem licença e versão verificadas
- [ ] Rollout, rollback e observabilidade estão documentados

## Riscos e limitações

<!-- Riscos residuais e validações não executadas. -->