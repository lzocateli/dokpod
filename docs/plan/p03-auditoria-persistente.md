# Plano: Auditoria persistente do P-03

**Status:** approved  
**Data de criação:** 2026-09-10  
**Última atualização:** 2026-09-10  
**Responsáveis:** Lincoln Zocateli  
**Origem:** IA assistida  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** P-03 do [plano MVP](mvp.md), ADR 2026-0001 e ADR 2026-0002

## Objetivo

Implementar a auditoria inicial do P-03 como armazenamento PostgreSQL append-only, integrado ao fluxo de cadastro e aprovação de ambientes, sem replicar usuários, memberships ou políticas do Keycloak.

O resultado observável será uma trilha durável que registre ator, ação, ambiente, correlação, horário e resultado, com idempotência para retries e autorização aplicada antes de revelar ou modificar recursos.

## Contexto e premissas

- O contrato de domínio `AuditEvent` e a porta `IAuditEventWriter` já existem, mas ainda não há persistência.
- PostgreSQL é o armazenamento durável aprovado para auditoria, conforme a arquitetura inicial.
- O domínio não dependerá de EF Core, Npgsql ou ASP.NET Core.
- A infraestrutura será adicionada ao plano de controle, sem criar um datastore novo.
- Keycloak continuará sendo a autoridade para identidade e decisão de autorização.
- A auditoria não armazenará tokens, certificados privados, secrets, payload integral do engine ou conteúdo de logs.
- Eventos corrigidos serão novos eventos; não haverá atualização ou exclusão lógica de eventos existentes.
- A implementação deverá ser compatível com retry, cancelamento, resposta perdida e concorrência.

## Não escopo

- Implementar login, logout, BFF, tela Angular ou configuração completa do realm Keycloak.
- Copiar usuários, roles, memberships ou políticas para o PostgreSQL.
- Implementar inventário de containers, comandos de lifecycle ou reconciliação de snapshots.
- Criar broker, cache distribuído ou serviço separado de auditoria.
- Definir a retenção definitiva de produção sem decisão operacional específica; o particionamento temporal, porém, é obrigatório nesta etapa.
- Marcar P-03 como concluído antes dos testes de autorização, bootstrap e revogação previstos no plano MVP.

## Dependências e decisões

1. Aprovação humana para criar o projeto `Dokpod.ControlPlane.Infrastructure` e adicionar EF Core/Npgsql.
2. ADR proposta para definir composição da persistência, migration, privilégio do usuário PostgreSQL e estratégia append-only. A IA deve manter a ADR como `proposed` até decisão humana.
3. Verificação da licença, manutenção e versão exata dos pacotes NuGet antes de adicioná-los ao gerenciamento central.
4. PostgreSQL real disponível no ambiente de validação containerizada, sem leitura ou criação de secrets dentro do workspace.
5. Definição do caso de uso de cadastro/aprovação de ambiente e do contrato de decisão Keycloak antes da integração final.

## Etapas

### P03-01: Decisão arquitetural e ADR

**Status:** in-progress  
**Responsável:** Lincoln Zocateli  
**Dependências:** nenhuma

**Objetivo:** registrar a decisão sobre o módulo de infraestrutura, EF Core/Npgsql e garantias append-only.

Entregas:

- ADR em `docs/adr/AAAA-NNNN-auditoria-persistente.md`, iniciando em `proposed`;
- alternativas consideradas, consequências, segurança, migration, rollback e operação;
- decisão explícita sobre usuário PostgreSQL, privilégios e retenção inicial.

Validação:

- revisão humana registrada;
- ADR não será alterada para `accepted` pela IA;
- referências para arquitetura, segurança e plano MVP resolvem.

Evidências:

- ADR [2026-0004-auditoria-persistente](../adr/2026-0004-auditoria-persistente.md) criada como `proposed`; aprovação humana ainda pendente.

### P03-02: Projeto de infraestrutura do plano de controle

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-01 aprovado

**Objetivo:** criar a fronteira de persistência sem contaminar domínio ou aplicação com EF Core.

Entregas:

- `backend/libs/Dokpod.ControlPlane.Infrastructure`;
- referência da infraestrutura para a aplicação e domínio conforme a direção arquitetural;
- `DbContext` do plano de controle;
- composição do DbContext no host API;
- configuração sem secrets versionados e com health/readiness separado.

Validação:

- teste arquitetural impede domínio/aplicação de dependerem de EF Core ou infraestrutura;
- build containerizado sem warnings;
- conexão inválida falha fechada e não habilita bypass de autorização.

Evidências:

- pendente.

### P03-03: Dependências e schema de auditoria

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-01 e P03-02

**Objetivo:** criar o schema durável com constraints e índices explícitos.

Entregas:

- versões centralizadas e verificadas de EF Core, Npgsql e tooling necessário;
- entidade de persistência interna, sem exposição como DTO;
- tabela de auditoria com `EventId` único, `EnvironmentId`, ator, ação, resultado, código de falha, UTC e correlação;
- tabela de auditoria particionada por faixa de `OccurredAtUtc` (`PARTITION BY RANGE`), com partições mensais, partições futuras criadas antecipadamente e partição de segurança;
- índices para ambiente/tempo, correlação e consulta autorizada;
- migration expand-contract;
- proteção contra `UPDATE` e `DELETE` pelo usuário de runtime, conforme decisão da ADR;
- limites de retenção documentados como operação futura, sem apagar evidência automaticamente nesta etapa.
- procedimento de rollover e monitoramento de partições documentados, sem depender de criação manual durante escrita.

Validação:

- migration aplica em PostgreSQL real;
- rollback da aplicação mantém compatibilidade com a versão anterior;
- constraints rejeitam duplicidade conflitante, IDs inválidos e timestamps não UTC;
- testes confirmam roteamento para partição mensal, pruning, partição de segurança, rollover e falha controlada quando a configuração de partições está incompleta;
- inspeção de privilégios confirma que o runtime não pode alterar ou remover eventos.

Evidências:

- pendente.

### P03-04: Writer append-only idempotente

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-03

**Objetivo:** implementar `IAuditEventWriter` com escrita atômica e semântica de retry segura.

Entregas:

- adapter PostgreSQL na infraestrutura;
- append transacional;
- repetição do mesmo `EventId` e payload tratada como idempotente;
- mesmo `EventId` com payload divergente rejeitado;
- cancelamento propagado;
- nenhum método de update/delete exposto pela porta;
- normalização e limites do contrato de domínio preservados.

Validação:

- testes unitários do mapeamento;
- integração PostgreSQL para retry, concorrência, cancelamento e divergência;
- falha de persistência impede confirmar uma operação que exige auditoria;
- nenhuma credencial, token ou payload sensível aparece em logs.

Evidências:

- pendente.

### P03-05: Caso de uso de cadastro e aprovação

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-04 e contrato de autorização Keycloak definido

**Objetivo:** integrar auditoria ao fluxo de ambientes sem substituir o Keycloak.

Entregas:

- caso de uso de registro/aprovação/suspensão/revogação de ambiente;
- decisão do Keycloak aplicada antes da leitura ou alteração do recurso;
- eventos para sucesso, negação, falha e resultado indeterminado;
- correlation ID propagado;
- vínculo do evento ao ambiente opaco;
- falha do writer tratada sem confirmar a operação auditável;
- integração idempotente e reconciliável com recurso do Keycloak.

Validação:

- usuário autorizado no ambiente A não acessa ou audita B;
- decisão negada, expirada ou indisponível falha fechada;
- retry não duplica evento nem cadastro;
- concorrência de bootstrap/clone não cria dois ambientes para a mesma identidade;
- auditoria não revela nome, host ou metadado antes da autorização.

Evidências:

- pendente.

### P03-06: API, contrato e observabilidade

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-05

**Objetivo:** expor somente o contrato necessário e tornar o fluxo operável.

Entregas:

- endpoint REST versionado para cadastro/estado conforme contrato aprovado;
- `application/problem+json` para autorização, indisponibilidade e conflito;
- OpenAPI atualizado sem entidades EF;
- health/readiness diferenciando API, PostgreSQL e Keycloak;
- métricas de append, latência, conflito de idempotência e falhas sem conteúdo sensível;
- logs estruturados com IDs técnicos e correlation ID.

Validação:

- contract tests;
- testes de autorização horizontal e SignalR;
- Keycloak ou PostgreSQL indisponível não resulta em bypass;
- OpenAPI e cliente gerado permanecem compatíveis.

Evidências:

- pendente.

### P03-07: Testes de segurança, recuperação e operação

**Status:** not-started  
**Responsável:** Lincoln Zocateli  
**Dependências:** P03-06

**Objetivo:** validar os critérios do P-03 relacionados a auditoria e identidade.

Entregas:

- testes de login/logout sem token no browser;
- Keycloak indisponível;
- autorização horizontal e SignalR;
- CSRF;
- corrida no bootstrap;
- agente falso e ambiente divergente;
- clone concorrente;
- replay, token expirado e revogação de stream;
- backup/restore da tabela e migration;
- teste de recuperação após resposta perdida.

Validação:

- `dotnet test` na imagem oficial fixada;
- PostgreSQL real em container;
- testes de engine Docker aplicáveis permanecem verdes;
- análise de secrets e vulnerabilidades;
- revisão de código e segurança antes de alterar status do P-03.

Evidências:

- pendente.

## Critérios de aceite finais

- Auditoria persistente é append-only e durável em PostgreSQL.
- Eventos possuem ator, ambiente, ação, resultado, UTC e correlation ID.
- Retry idêntico é idempotente; conflito de payload é rejeitado.
- Usuário não autorizado não consegue revelar, modificar ou auditar outro ambiente.
- Keycloak indisponível, decisão inválida ou token expirado falham fechados.
- Cadastro e auditoria suportam retry, concorrência, cancelamento e resposta perdida.
- Runtime não possui privilégio de alterar ou remover eventos existentes.
- Nenhum secret, token, certificado privado ou payload integral de engine é persistido ou registrado.
- Migrations, API, observabilidade e documentação são validadas em containers.
- Todos os testes de backend, arquitetura, integração PostgreSQL e engine aplicáveis passam.
- O revisor humano registra a decisão da ADR e a evidência de aceite do slice.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| Auditoria integrada depois da operação | perda de evidência | confirmar operação somente após append obrigatório |
| Retry com payload divergente | corrupção de trilha | chave única por `EventId` e comparação de hash/payload canônico |
| Usuário de runtime altera eventos | perda de integridade | privilégios separados e testes reais de `UPDATE`/`DELETE` |
| Keycloak indisponível | bypass de autorização | deny-by-default e erro `503` |
| Host/identificador sensível em auditoria | vazamento de dados | IDs opacos, allowlists, limites e redaction |
| Migration incompatível | rollback bloqueado | expand-contract e teste com versão anterior |
| Crescimento ilimitado da tabela | custo e degradação | particionamento temporal, pruning, rollover e política de retenção aprovada antes da produção |
| Concorrência no bootstrap | ambientes duplicados | constraint, idempotency key e teste concorrente |

## Rollout e rollback

1. Aprovar a ADR e validar dependências/licenças.
2. Aplicar migration expand sem remover ou renomear dados existentes.
3. Publicar writer atrás do caso de uso, mantendo deny-by-default.
4. Habilitar cadastro em laboratório com Keycloak e PostgreSQL containerizados.
5. Executar testes de recuperação, concorrência, autorização e backup/restore.
6. Promover somente após revisão humana e evidências do P-03.
7. Em rollback, manter a migration compatível, desabilitar o fluxo novo e preservar eventos; nunca apagar auditoria para voltar a versão anterior.

## Histórico de status

| Data | Escopo | De | Para | Evidência ou motivo | Autor |
| --- | --- | --- | --- | --- | --- |
| 2026-09-10 | plano | - | draft | Plano criado para detalhar as dependências da persistência append-only do P-03; aprovação humana pendente. | IA assistida |
| 2026-09-10 | P03-01 | not-started | in-progress | ADR 2026-0004 criada como `proposed`; decisão humana sobre infraestrutura, privilégios e migration pendente. | IA assistida |
