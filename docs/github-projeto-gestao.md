# Gestão do projeto no GitHub

Este documento define como usar GitHub Issues e GitHub Projects para gerenciar o Dokpod.

## Limites da configuração

Os formulários em `.github/ISSUE_TEMPLATE/` são versionados neste repositório. O Project, seus campos e visualizações são recursos da conta ou organização do GitHub e precisam ser criados na interface ou pela API. Este documento é a configuração de referência para evitar divergência.

O Project organiza o fluxo operacional. Os planos em `docs/plan/` continuam sendo a fonte versionada para dependências, decisões, gates e evidências.

## Tipos de demanda

| Tipo | Formulário | Label inicial |
| --- | --- | --- |
| Bug | `bug.yml` | `type:bug` |
| Feature | `feature.yml` | `type:feature` |
| Tarefa | `technical-task.yml` | `type:task` |
| Slice de plano | `plan-slice.yml` | `type:plan-slice` |
| Spike | `architecture-spike.yml` | `type:spike` |

Vulnerabilidades não devem ser registradas nesses formulários. Use o fluxo de segurança do repositório.

## Project recomendado

Crie um Project de organização chamado `Dokpod Delivery` e associe o repositório `lzocateli/Dokpod`.

| Campo | Tipo | Valores sugeridos |
| --- | --- | --- |
| Status | Single select | Inbox, Triage, Ready, In progress, Blocked, In review, Validation, Done, Cancelled |
| Type | Single select | Bug, Feature, Task, Plan slice, Spike |
| Priority | Single select | P0, P1, P2, P3 |
| Area | Iteration ou texto | Architecture, Backend, Frontend, Agent, Engine, Contracts, Database, Security, DevOps, Documentation, Tests |
| Risk | Single select | Low, Medium, High |
| Plan | Text | nome ou caminho do plano em `docs/plan/` |
| Slice | Text | `P-01`, `P-02`, `WP-01` |
| Iteration | Iteration | sprint ou ciclo de trabalho |
| Target | Single select | MVP, Fase 3, Fase 5, Post-MVP, Unplanned |
| Human approval | Single select | Pending, Approved, Not required |
| Blocked by | Text | Issue, ADR, decisão ou dependência externa |

## Visualizações

Crie views de Backlog, Kanban, Sprint atual, Roadmap, Risco e bloqueios e Entrega de planos. Filtre itens concluídos/cancelados, agrupe o Kanban por `Status` e mantenha `Plan`, `Slice`, `Risk`, `Priority` e `Blocked by` visíveis onde forem necessários.

## Fluxo operacional

1. A demanda entra pelo Issue Form apropriado e no Project com `Status = Inbox`.
2. A triagem confirma Type, Priority, Area, Risk, responsável e dependências.
3. Demandas incompletas ficam em `Triage`; demandas prontas passam a `Ready`.
4. Uma pessoa ou o Copilot executa somente demandas `Ready` e autorizadas.
5. O trabalho fica em `In progress`; impedimentos ficam em `Blocked` com causa explícita.
6. Um Pull Request referencia a Issue, por exemplo `Implements #123`, e move a demanda para `In review`.
7. Após os checks, a demanda vai para `Validation`; a revisão humana confirma os critérios e move para `Done`.
8. O fechamento da Issue não substitui a atualização do plano Markdown quando for um slice.

## Governança

- `Done` no Project não autoriza merge, publicação ou deploy por si só.
- Slices `draft`, `proposed`, `blocked`, `cancelled` ou `superseded` não devem ser executados.
- Mudanças de arquitetura, contrato, banco, segurança, engine ou protocolo atualizam os documentos correspondentes.
- Dependências são registradas com links para Issues, ADRs ou etapas do plano.
- Não registre secrets, certificados, dados de infraestrutura ou logs integrais nas Issues.

## Checklist

- [ ] Criar `Dokpod Delivery` e associar `lzocateli/Dokpod`.
- [ ] Criar os campos personalizados e as seis views.
- [ ] Configurar automação para novas Issues e PRs.
- [ ] Padronizar labels `type:*` e `status:triage`.
- [ ] Definir responsáveis pela triagem e revisão humana.
- [ ] Criar uma Issue de teste por tipo e confirmar filtros e views.
