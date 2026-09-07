# Governança do repositório

Esta pasta concentra regras de contribuição, segurança, decisões e instruções contextuais para desenvolvimento assistido.

## Conteúdo

- `copilot-instructions.md`: contexto e regras invariáveis do Dokpod;
- `instructions/`: regras especializadas por caminho;
- `agents/`: especialistas selecionáveis e agentes de revisão read-only;
- `prompts/`: comandos focados para planejar, implementar, diagnosticar, testar e revisar;
- `skills/`: workflows completos com gates de engenharia;
- `COPILOT_GUIDE.md`: guia de escolha e uso das customizações;
- `ADR_TEMPLATE.md`: modelo para decisões arquiteturais;
- `PLAN_TEMPLATE.md`: modelo para planos verificáveis;
- `COMMIT_CONVENTIONS.md`: tipos e escopos de commit;
- `CONTRIBUTING.md`: fluxo de contribuição e Definition of Done;
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