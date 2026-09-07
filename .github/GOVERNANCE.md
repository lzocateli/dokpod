# Governança do repositório

Esta pasta concentra regras de contribuição, segurança, decisões e instruções contextuais para desenvolvimento assistido.

## Conteúdo

- `copilot-instructions.md`: contexto e regras invariáveis do Dokpod;
- `instructions/`: regras especializadas por caminho;
- `agents/`: especialistas selecionáveis e agentes de revisão read-only;
- `prompts/`: comandos focados para planejar, implementar, diagnosticar, testar e revisar;
- `skills/`: workflows completos com gates de engenharia;
- `COPILOT_GUIDE.md`: guia de escolha e uso das customizações;
- `SCRIPTING.md`: localização, linguagem e contrato das automações do repositório;
- `ADR_TEMPLATE.md`: modelo para decisões arquiteturais;
- `PLAN_TEMPLATE.md`: modelo para planos verificáveis;
- `COMMIT_CONVENTIONS.md`: tipos e escopos de commit;
- `CONTRIBUTING.md`: fluxo de contribuição e Definition of Done;
- `CODEOWNERS`: propriedade e revisão obrigatória de todas as alterações;
- `SECURITY.md`: política de divulgação responsável;
- `PULL_REQUEST_TEMPLATE.md`: evidências exigidas em revisão;
- `ISSUE_TEMPLATE/`: formulários de bug e feature.

Workflows e Dependabot serão adicionados junto dos primeiros manifests .NET e Angular, quando comandos, lockfiles e imagens puderem ser validados. Nenhum gate deve existir apenas de forma decorativa.

## Hierarquia de autoridade

1. `README.md`, documentação técnica e ADRs aceitos;
2. `copilot-instructions.md`;
3. instruções específicas selecionadas por `applyTo` ou descrição;
4. agente, prompt ou skill selecionado;
5. convenções observadas no módulo proprietário.

Em conflito, a fonte superior prevalece. Skills e agentes coordenam o trabalho, mas não podem relaxar as regras invariáveis do projeto.

## Administração e promoção para `main`

`@lzocateli` é o único administrador e proprietário de código autorizado. A branch `main` é protegida por ruleset ativo no GitHub com estas garantias:

- somente administradores podem atualizar `main`, e o único administrador autorizado é `@lzocateli`;
- toda atualização ocorre por pull request; pushes diretos são bloqueados inclusive para o proprietário;
- alterações propostas por terceiros passam por pull request e revisão do proprietário;
- exclusão da branch e atualizações sem fast-forward são bloqueadas;
- histórico linear e commits assinados são obrigatórios;
- conversas de revisão precisam estar resolvidas e novas alterações invalidam aprovações anteriores;
- a última alteração de um pull request deve ser aprovada por outra pessoa antes da promoção, salvo bypass explícito do proprietário;
- somente squash merge é permitido e branches são excluídas após o merge;
- GitHub Actions possui permissões de escrita desabilitadas por padrão e não pode aprovar pull requests.

O bypass administrativo vale somente dentro de pull requests e existe para permitir que o proprietário promova mudanças próprias, pois o GitHub não permite autoaprovação. Conceder acesso administrativo, alterar o `CODEOWNERS`, desativar o ruleset ou modificar permissões do Actions é uma mudança de controle e exige decisão explícita de `@lzocateli`.

Checks de CI obrigatórios serão adicionados ao ruleset somente depois que seus workflows e comandos locais equivalentes existirem e forem validados. Proteções administrativas não substituem os gates técnicos descritos na Definition of Done.

O estado remoto é auditado e reconciliado por [`tools/scripts/configure-github-governance.ps1`](../tools/scripts/configure-github-governance.ps1). O modo padrão somente audita; alterações exigem `-Mode Apply` explícito.
