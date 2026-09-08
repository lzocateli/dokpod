# Engenharia assistida por GitHub Copilot

Este diretório define como pessoas e o GitHub Copilot devem planejar, implementar, testar, revisar e preparar releases do Dokpod.

Leia primeiro o [guia do Copilot](COPILOT_GUIDE.md). Para gerenciar trabalho, use os formulários de Issue e o [guia de gestão do GitHub Project](../docs/github-projeto-gestao.md).

## Estrutura

| Diretório/arquivo | Finalidade |
| --- | --- |
| `copilot-instructions.md` | regras globais do Dokpod |
| `instructions/*.instructions.md` | regras carregadas por domínio e caminho |
| `agents/*.agent.md` | especialistas selecionáveis |
| `prompts/*.prompt.md` | tarefas focadas invocáveis por `/` |
| `skills/*/SKILL.md` | workflows multi-etapas com gates |
| `COPILOT_GUIDE.md` | tutorial de uso |
| `ISSUE_TEMPLATE/` | entrada estruturada para o GitHub Project |
| `COMMIT_CONVENTIONS.md` | formato de commits |
| `PULL_REQUEST_TEMPLATE.md` | evidências mínimas de pull request |
| `SECRET-SCANNING.md` | política de detecção de secrets |
| `CONTRIBUTING.md` | processo de contribuição e Definition of Done |
| `GOVERNANCE.md` | decisões, revisão e releases |
| `SECURITY.md` | reporte de vulnerabilidades |
| `ADR_TEMPLATE.md` | modelo de decisão arquitetural |
| `PLAN_TEMPLATE.md` | modelo de plano verificável |

## Como escolher

- Use **Dokpod Delivery Lead** ou `/implement-feature` para entrega ponta a ponta.
- Use **Dokpod Engine & Protocol Engineer** para Docker/Podman, gRPC, mTLS, journal e reconciliação.
- Use **Dokpod Angular Engineer** para frontend e UX operacional.
- Use **Dokpod .NET Engineer** para API, BFF, agente, domínio e PostgreSQL.
- Use `/plan-feature` para registrar um plano em `docs/plan`.
- Use `/continuar-plano` somente para um slice aprovado e desbloqueado.
- Use `/review-change` ou `/pull-request-review` para revisão.
- Use `/release-readiness` antes de publicar uma versão.

O GitHub Project organiza a demanda, status, prioridade, risco e bloqueios. O plano Markdown continua sendo a fonte versionada de dependências, decisões, gates e evidências.
