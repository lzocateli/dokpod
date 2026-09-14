# ADR 2026-0004: Consumir a plataforma global de identidade

- **Status:** approved
- **Data:** 2026-09-14
- **Origem:** IA assistida
- **Revisor humano:** Lincoln Zocateli

## Contexto

O laboratório do Dokpod dependia do Compose do Keycloak no AltivyNotes, da rede `altivy-edge` e de fallbacks `ALTIVY_*`. O BFF também não participava da stack E2E.

## Decisão proposta

O Dokpod consumirá a plataforma global pela rede `identity-global`, usando o realm `dokpod`, o hostname `dokpod.altivy.test`, o client `dokpod-bff` e o tema publicado em `KEYCLOAK_THEMES_ROOT/dokpod`. O BFF entra no Compose E2E e a Web passa a ser publicada pelo gateway global.

A reconciliação cotidiana usa `dokpod-provisioner` por client credentials no realm `dokpod`. `-Bootstrap` é reservado à criação inicial e usa `master` somente com invocação explícita. Nenhum runtime recebe a credencial global.

## Pendências

Validar a plataforma com certificados e secrets externos, concluir o registro inicial do realm/client, separar o database de aplicação, atualizar callbacks e executar os testes OIDC/UMA e de isolamento. O Compose local de Keycloak permanece durante a janela de rollback.
