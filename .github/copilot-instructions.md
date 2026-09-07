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
4. Atualize contrato, compatibilidade, documentação, segurança e observabilidade quando necessário.
5. Preserve alterações existentes e não reformate arquivos fora do escopo.
6. Não faça commit, push, publicação ou deploy sem solicitação explícita.

## ADRs e planos

- ADRs ficam em `docs/adr/AAAA-NNNN-titulo.md`, começam como `proposed` e seguem `.github/ADR_TEMPLATE.md`.
- Planos ficam em `docs/plan/<tema>.md` e seguem `.github/PLAN_TEMPLATE.md`.
- IA não marca ADR como `accepted`, plano como `approved` nem etapa como `completed` sem decisão ou evidência humana explícita.

## Scripts e automação

- Toda automação global de infraestrutura, administração, manutenção e validação pertence a `tools/scripts/`.
- Use PowerShell 7 para orquestração de CLIs, containers e sistema; use Python para parsing estruturado, APIs, lógica reutilizável ou testável.
- Ferramentas Python usam exclusivamente `uv`, compartilham o único `tools/pyproject.toml` e mantêm `tools/uv.lock` versionado.
- Não crie `requirements.txt`, ambientes virtuais manuais, outro `pyproject.toml` para automação ou scripts globais fora de `tools/scripts/`.
- Scripts de build, entrypoint, health check, instalação ou runtime permanecem no módulo proprietário.
- Todo script criado ou refatorado oferece `--help` sem efeitos colaterais e segue `.github/instructions/script-authoring.instructions.md` e `.github/SCRIPTING.md`.

## Regras invariáveis

- Nunca exponha sockets Docker/Podman pela rede nem implemente proxy genérico da API do engine.
- Autorize antes de revelar ambiente, container ou metadado.
- Keycloak mantém usuários, credenciais, MFA, sessões, roles, recursos, scopes e políticas; o Dokpod não os reimplementa.
- O BFF mantém tokens fora do browser; a API atua como PEP e falha fechada ao aplicar decisões do Keycloak.
- Agentes usam identidade individual, mTLS, rotação e revogação.
- Operações mutáveis usam ID idempotente, expiração, auditoria e reconciliação.
- Não presuma equivalência entre Docker e Podman; anuncie e valide capabilities.
- Não registre tokens, certificados privados, variáveis de ambiente, secrets ou logs integrais de containers.
- Exclusão de container não remove volume implicitamente.
- Dependências externas exigem licença permissiva, gratuita, versão fixada e manutenção verificada.
- Não adicione broker, cache distribuído, microsserviço ou novo datastore sem ADR e evidência operacional.
- APIs públicas usam OpenAPI, `application/problem+json` e versionamento explícito.
- Mudanças de protocolo mantêm compatibilidade N/N-1 ou documentam migração e rollout.

## Qualidade mínima

- código novo possui testes proporcionais ao risco;
- adapters são testados contra engines reais, não apenas mocks;
- formatador, análise estática, build e testes do projeto alterado passam;
- fluxos críticos do usuário possuem Playwright;
- imagens possuem build e smoke test; o pacote Windows possui instalação e smoke test em host sem runtime .NET;
- superfícies privilegiadas recebem threat model e revisão de segurança.