# Dokpod - Instruções globais

## Contexto obrigatório

- O Dokpod é um plano de controle self-hosted para containers Docker e Podman.
- Backend, BFF, API e agente usam C#/.NET 10; o frontend usa Angular 22.
- Backend, BFF e frontend sempre executam em containers.
- O agente Linux sempre executa em container; o agente Windows é um Worker Service self-contained instalado como Windows Service.
- Keycloak é a autoridade externa obrigatória de identidade e autorização dos usuários.
- Leia o [README](../README.md), a [arquitetura](../docs/arquitetura.md) e a [segurança](../docs/seguranca.md) antes de alterar contratos, agentes, engines, identidade ou deployment.
- O engine local é a fonte de verdade dos containers; o inventário PostgreSQL é uma projeção reconstruível.
- O projeto é inspirado funcionalmente no Portainer, mas não copia seu código, texto, marca ou interface.

## Estrutura do monorepo

```text
backend/apps/api/          # host REST, gRPC e SignalR
backend/apps/bff/          # OIDC confidencial e sessão do browser
backend/apps/agent/        # agente Linux container/Windows Service
backend/libs/              # domínio e aplicações/infraestruturas separadas
backend/tests/             # testes .NET
frontend/web/              # aplicação Angular
frontend/libs/             # bibliotecas compartilhadas
frontend/tests/            # Playwright
contracts/openapi/         # contrato público HTTP
contracts/agent/           # protocolo agente-servidor
deploy/                    # imagens, pacote Windows, Keycloak e operação
docs/                      # arquitetura, ADRs, planos e runbooks
tools/scripts/             # automação global de infraestrutura e manutenção
```

Não crie dependências circulares. Hosts compõem; bibliotecas implementam regras reutilizáveis; contratos não dependem de aplicações.

## Fluxo de trabalho

1. Comece pelo comportamento, teste relacionado e módulo proprietário.
2. Declare uma hipótese local e uma validação capaz de refutá-la.
3. Faça a menor alteração coerente e valide o recorte imediatamente.
4. Valide primeiro o recorte alterado; depois execute verificações amplas proporcionais ao risco.
5. Atualize contrato, compatibilidade, migração, documentação, segurança e observabilidade quando o comportamento exigir.
6. Preserve alterações existentes e não reformate arquivos fora do escopo.

## ADRs e planos

- ADRs ficam em `docs/adr/AAAA-NNNN-titulo.md`, começam como `proposed` e seguem `.github/ADR_TEMPLATE.md`.
- Planos ficam em `docs/plan/<tema>.md` e seguem `.github/PLAN_TEMPLATE.md`.
- Todo plano possui status geral e status em cada etapa; atualize o histórico ao mudar qualquer status.
- Registre `Origem` como humano ou IA assistida e identifique o revisor humano.
- IA não marca ADR como `accepted`, plano como `approved` nem etapa como `completed` ou `skipped` sem decisão ou evidência humana explícita e referenciada.

## Commits

- Todo commit criado ou sugerido segue [Conventional Commits](instructions/conventional-commits.instructions.md) e usa os tipos e escopos aprovados pelo projeto.
- Escreva a descrição em português brasileiro, no imperativo, iniciando com minúscula e sem ponto final.
- Mudanças incompatíveis usam `!` no cabeçalho e o rodapé `BREAKING CHANGE:` com impacto e migração.
- Não faça commit, push, publicação ou deploy sem solicitação explícita.

## Scripts e automação

- Para qualquer comando Angular do frontend, use o alias `ng` definido no profile do PowerShell, executando-o a partir de `frontend/web`. O alias inicia o container padronizado do projeto; não invoque `node`, `npm`, `npx` ou uma instalação local do Angular CLI diretamente no host.
- O profile também define o alias `npx`, que executa a ferramenta na mesma imagem containerizada de `node` e `npm`; use-o no PowerShell interativo quando for necessário executar um pacote diretamente. Em scripts, tarefas e CI que não carregam o profile, use explicitamente `npm exec -- <comando>` dentro do container apropriado; não invoque `npx` instalado no host.
- Antes de enviar `ng`, confirme que o diretório atual é `frontend/web`; se o terminal estiver na raiz do monorepo, envie primeiro `Set-Location $env:USERPROFILE\projetos\dokpod\frontend\web`.
- Envie comandos Angular como texto literal, sem caracteres de controle como `^U` ou `Ctrl+U` antes de `ng`. Se `^U` aparecer no prompt, descarte essa entrada e reenvie o comando limpo.
- Após `ng test` ou `ng build`, aguarde a conclusão e o resumo final do Angular ou Vitest antes de concluir que não houve saída.
- Exemplos: `Set-Location frontend/web; ng test --watch=false` e `Set-Location frontend/web; ng build --configuration production`.
- Toda automação global de infraestrutura, administração, manutenção e validação pertence a `tools/scripts/`.
- Use PowerShell 7 para orquestração de CLIs, containers e sistema; use Python para parsing estruturado, APIs, lógica reutilizável ou testável.
- Ferramentas Python usam exclusivamente `uv`, compartilham o único `tools/pyproject.toml` e mantêm `tools/uv.lock` versionado.
- Não crie `requirements.txt`, ambientes virtuais manuais, outro `pyproject.toml` para automação ou scripts globais fora de `tools/scripts/`.
- Scripts de build, entrypoint, health check, instalação ou runtime permanecem no módulo proprietário.
- Todo script criado ou refatorado oferece `--help` sem efeitos colaterais e segue `.github/instructions/script-authoring.instructions.md` e `.github/SCRIPTING.md`.

## Regras invariáveis

- Preserve conteúdo do usuário; conflitos nunca podem causar sobrescrita silenciosa.
- Normalize e autorize paths antes de qualquer acesso ao filesystem.
- Nunca exponha sockets Docker/Podman pela rede nem implemente proxy genérico da API do engine.
- Autorize antes de revelar ambiente, container ou metadado.
- No desenvolvimento local em Windows, armazene todo secret exclusivamente em `$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env`. Isso inclui credenciais administrativas do Keycloak, PostgreSQL, client secrets, connection strings, certificados e qualquer outro valor sensível.
- Nunca crie arquivos `.env`, `.env.*`, `secrets.json`, cópias ou templates com valores reais dentro do repositório. Não use `dotnet user-secrets`; scripts, Compose, coleções HTTP, testes e documentação devem referenciar o arquivo externo ou variáveis de ambiente injetadas por processo seguro.
- Não leia, exiba, registre, copie ou sobrescreva o conteúdo do arquivo externo durante validações. Antes de mover ou gravar secrets, falhe fechado se o destino já existir.
- Keycloak mantém usuários, credenciais, MFA, sessões, roles, recursos, scopes e políticas; o Dokpod não os reimplementa.
- O BFF mantém tokens fora do browser; a API atua como PEP e falha fechada ao aplicar decisões do Keycloak.
- Agentes usam identidade individual, mTLS, rotação e revogação.
- Operações mutáveis usam ID idempotente, expiração, auditoria e reconciliação.
- Não presuma equivalência entre Docker e Podman; anuncie e valide capabilities.
- Não registre tokens, certificados privados, variáveis de ambiente, secrets ou logs integrais de containers.
- Exclusão de container não remove volume implicitamente.
- Dependências externas exigem licença permissiva, gratuita, versão fixada e manutenção verificada.
- Nunca sugira, instale ou adicione biblioteca paga, proprietária, source-available, com licença comercial obrigatória, copyleft ou outra restrição incompatível com a distribuição AGPL-3.0-only do Dokpod. Licença ausente, ambígua ou não verificada bloqueia a dependência.
- O padrão arquitetural Mediator é permitido quando houver necessidade comprovada de desacoplar dispatch, handlers e pipelines. Nesses casos, use `Nuuvify.CommonPack.Mediator` com versão centralizada e licença da versão exata verificada; nunca use MediatR.
- Não adicione broker, cache distribuído, microsserviço ou novo datastore sem ADR e evidência operacional.
- APIs públicas usam OpenAPI, `application/problem+json` e versionamento explícito.
- Mudanças de protocolo mantêm compatibilidade N/N-1 ou documentam migração e rollout.
- Mudanças de schema seguem expand-contract e devem ser testadas com PostgreSQL real.
- Tabelas com potencial de crescimento elevado ou retenção temporal devem usar Table Partitioning/Declarative Partitioning por faixa de data, preferencialmente partições mensais; a solução deve definir criação antecipada, rollover, partição de segurança para datas fora da janela, índices por partição, retenção e testes de roteamento antes da produção, independentemente do banco de dados adotado.

## Qualidade mínima

- Código novo possui testes nos níveis adequados ao risco.
- Rode formatador, análise estática, build e testes do projeto alterado.
- Adapters são testados contra engines reais, não apenas mocks.
- Para fluxos do usuário, atualize ou adicione Playwright.
- Para PostgreSQL e filesystem, use testes de integração reais, não mocks como evidência principal.
- Imagens possuem build e smoke test; o pacote Windows possui instalação e smoke test em host sem runtime .NET.
- Superfícies privilegiadas recebem threat model e revisão de segurança.
- Reviews priorizam integridade de dados, segurança, regressões, concorrência e lacunas de teste.

## Instruções especializadas

As regras detalhadas em `.github/instructions/` são carregadas pelo caminho alterado ou pela descrição. Não replique essas regras em código ou documentação local.