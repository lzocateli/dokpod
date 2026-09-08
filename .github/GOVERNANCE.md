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
- `PERFORMANCE_TESTING_CRITERIA.md`: perfis, thresholds e evidências de capacidade;
- `ADR_TEMPLATE.md`: modelo para decisões arquiteturais;
- `PLAN_TEMPLATE.md`: modelo para planos verificáveis;
- `COMMIT_CONVENTIONS.md`: tipos e escopos de commit;
- `CONTRIBUTING.md`: fluxo de contribuição e Definition of Done;
- `CODEOWNERS`: ownership padrão e superfícies de revisão;
- `dependabot.yml`: atualizações semanais de Actions, NuGet e Docker;
- `SECRET-SCANNING.md`: política, instalação e tratamento de achados do Gitleaks;
- `SECURITY.md`: política de divulgação responsável;
- `PULL_REQUEST_TEMPLATE.md`: evidências exigidas em revisão;
- `ISSUE_TEMPLATE/`: formulários de bug e feature.
- `workflows/`: checks obrigatórios de CI e detecção de secrets.

O CI backend e a detecção de secrets existem desde o baseline executável do
repositório. Novos gates são adicionados com os manifests correspondentes e após
validação do comando local equivalente. Nenhum gate existe apenas de forma
decorativa.

## Configuração administrativa

- exija `CI / Result` e `Gitleaks / Full History` no ruleset de `main`;
- habilite revisão de Code Owner quando houver revisor independente;
- habilite Dependency Graph, Dependabot Alerts e security updates;
- habilite Private Vulnerability Reporting, secret scanning e push protection;
- mantenha `GITHUB_TOKEN` somente leitura por padrão e actions fixadas por SHA;
- proteja tags `v*` antes da primeira release.

Configurações do GitHub não são reconstruídas pelos arquivos versionados. Registre
responsável, data e evidência sanitizada para cada controle habilitado.

## Hierarquia de autoridade

1. `README.md`, documentação técnica e ADRs aceitos;
2. `copilot-instructions.md`;
3. instruções específicas selecionadas por `applyTo` ou descrição;
4. agente, prompt ou skill selecionado;
5. convenções observadas no módulo proprietário.

Em conflito, a fonte superior prevalece. Skills e agentes coordenam o trabalho, mas não podem relaxar as regras invariáveis do projeto.
