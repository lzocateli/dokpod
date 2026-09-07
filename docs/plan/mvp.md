# Plano: MVP do Dokpod

**Status:** proposed  
**Data de criação:** 2026-09-06  
**Última atualização:** 2026-09-06  
**Responsáveis:** equipe Dokpod  
**Origem:** IA assistida  
**Revisor humano:** pendente  
**Relacionado:** ADR 2026-0001

## Objetivo

Entregar um MVP self-hosted que registre agentes, mostre inventário e execute com segurança início, parada, reinício e exclusão de containers Docker em Linux.

## Contexto e premissas

- .NET 10 e Angular 22 são requisitos definidos;
- backend, BFF e frontend são sempre executados em containers;
- o agente Linux é distribuído como container e o agente Windows como Worker Service self-contained;
- Keycloak é obrigatório para autenticação e autorização de usuários;
- Docker Linux é a primeira plataforma estável;
- Podman Linux e Docker Windows avançam após provas técnicas;
- o ADR 2026-0001 está aceito;
- a primeira edição é a CE sob `AGPL-3.0-only`, sem chave ou limites artificiais de nodes e usuários;
- a Business não faz parte do MVP e só começa após estabilização da CE;
- toolchains, runtimes e serviços containerizados seguem a matriz de `docs/distribuicao.md`.

## Não escopo

- criação de containers e stacks;
- Kubernetes, Swarm e registries;
- console interativo e acesso a arquivos;
- atualização automática do agente;
- alta disponibilidade do plano de controle na primeira release.
- funcionalidades exclusivas, licenciamento e distribuição da edição Business.

## Dependências e decisões

- ADR 2026-0001 aceito;
- ADR 2026-0002 aceito e configuração inicial do Keycloak;
- ADR 2026-0003 aceito e política de licenciamento da CE;
- escolha de pacotes após verificação de licença;
- ambiente real Docker, Podman e Windows Server para os testes.

## Etapas

### P-01: Prova Docker Linux e transporte

**Status:** in-progress  
**Responsável:** não atribuído  
**Dependências:** nenhuma

**Objetivo:** eliminar as maiores incertezas antes do scaffolding definitivo.

Entregas:

- spike Docker Linux e gRPC mTLS;
- journal durável, fencing de sessão e relatório de falhas.

Validação:

- executar as provas 1, 4, 5 e 8 de `docs/viabilidade.md` com evidência reproduzível.

Evidências:

- `test-docker-integration-container`: 1 teste aprovado contra Docker Engine real pelo Unix socket, cobrindo negociação da API, listagem, início, reinício, parada e exclusão sem volumes ou force;
- `test-backend-container`: 21 testes aprovados para deduplicação, fencing, deadline, revisão do alvo, resultado reconciliável, journal durável e negociação da identidade do agente por fingerprint;
- `build-agent-image` e `smoke-agent-image`: imagem Linux construída e executada como usuário não root, filesystem read-only e acesso ao socket somente por grupo suplementar;
- certificados sem EKU de cliente e versões de protocolo incompatíveis são rejeitados antes da ativação da sessão;
- Kestrel com cadeia/revogação mTLS, cliente gRPC do agente, perda de resposta e reconexão permanecem pendentes.

### P-02: Fundação do monorepo e contratos

**Status:** in-progress  
**Responsável:** não atribuído  
**Dependências:** P-01

**Objetivo:** criar soluções .NET, workspace Angular, contratos e gates mínimos.

Entregas:

- estrutura de solução inspirada no AltivyNotes, com hosts e bibliotecas separados;
- imagens reproduzíveis para API, BFF, web e agente Linux;
- publicação self-contained reproduzível do agente Windows;
- builds e testes executados com as imagens `dotnet-sdk`, `angular-cli`, `k6`, `playwright-e2e` e `gitleaks` fixadas na matriz;
- OpenAPI e protocolo do agente versionados;
- testes de arquitetura.

Validação:

- format, lint, build e testes passam nos containers de toolchain; o pacote Windows executa em host sem runtime .NET.

Evidências:

- `Dokpod.slnx`, gerenciamento central de pacotes e separação inicial entre domínio, aplicação, infraestrutura e contratos criados;
- Protocol Buffers v1 compilado no build por `Grpc.Tools`;
- host Worker do agente Linux/Windows e núcleo de aplicação do plano de controle criados;
- `build-backend-container`: build aprovado sem avisos ou erros;
- `test-backend-container`: 21 testes aprovados;
- Dockerfile multi-stage do agente e `.dockerignore` validados por build e smoke test;
- hosts, OpenAPI, Angular, imagens finais, arquitetura completa e publicação Windows self-contained permanecem pendentes.

### P-03: Identidade e cadastro de ambientes

**Status:** not-started  
**Responsável:** não atribuído  
**Dependências:** P-02

**Objetivo:** autenticar usuários e cadastrar agentes sem credenciais estáticas compartilhadas.

Entregas:

- realm `dokpod`, clients `dokpod-bff`, `dokpod-api` e provisioner com menor privilégio;
- BFF confidencial, recursos por ambiente, scopes e matriz deny-by-default no Keycloak;
- bootstrap vinculado a ambiente/chave, aprovação por fingerprint, mTLS, rotação e revogação do agente;
- auditoria inicial.

Validação:

- testes de login/logout sem token no browser, Keycloak indisponível, autorização horizontal/SignalR, CSRF, corrida no bootstrap, agente falso, ambiente divergente, clone concorrente, replay, token expirado e revogação de stream.

Evidências:

- pendente.

### P-04: Inventário reconciliável

**Status:** not-started  
**Responsável:** não atribuído  
**Dependências:** P-03

**Objetivo:** mostrar estado confiável e idade dos dados por ambiente.

Entregas:

- eventos, snapshots, projeção PostgreSQL e UI de ambientes/containers;
- métricas de conexão, atraso e erro.

Validação:

- reconexão, lacuna de sequência e carga nominal de 56 agentes, aproximadamente 1.120 containers e 30 usuários simultâneos sem perda ou crescimento ilimitado;
- margem de 100 agentes, 2.000 containers e 50 usuários validada com k6 e simuladores de agente.

Evidências:

- pendente.

### P-05: Ciclo de vida de containers

**Status:** not-started  
**Responsável:** não atribuído  
**Dependências:** P-04

**Objetivo:** iniciar, parar, reiniciar e excluir containers com confirmação e auditoria.

Entregas:

- comandos duráveis com deduplicação por ID/hash, fencing e reconciliação;
- UI com estados intermediários e confirmação de exclusão;
- autorização por ambiente e ação.

Validação:

- testes reais de sucesso, timeout, replay após restart, ID com payload divergente, alvo recriado, desconexão e resposta perdida.

Evidências:

- pendente.

### P-06: Hardening e release candidata

**Status:** not-started  
**Responsável:** não atribuído  
**Dependências:** P-05

**Objetivo:** produzir uma release candidata reproduzível e operável.

Entregas:

- threat model, SBOM, scans, imagens e pacote Windows assinados e runbooks;
- backup/restore e política de atualização;
- documentação da matriz suportada.
- texto integral da AGPL-3.0-only, avisos de terceiros e link visível para o código-fonte correspondente.

Validação:

- gates completos, smoke tests e exercício de recuperação aprovados por revisão humana.
- artefatos CE executam sem chave, telemetria comercial obrigatória ou serviço externo de licenciamento.

Evidências:

- pendente.

### P-07: Qualificar capabilities adicionais

**Status:** not-started  
**Responsável:** não atribuído  
**Dependências:** P-01

**Objetivo:** avaliar Podman Linux e o Worker Service em Docker Windows sem bloquear a entrega Docker Linux.

Entregas:

- matriz Podman rootless/rootful e diferenças Libpod;
- matriz Windows Server, RID, Docker, conta de serviço, named pipe e ACL;
- decisão de suporte estável, experimental ou adiado por capability.

Validação:

- executar as provas 2 e 3 de `docs/viabilidade.md`; confirmar que principal não autorizado não acessa o pipe e que instalação, atualização e rollback preservam identidade e journal.

Evidências:

- pendente.

## Critérios de aceite finais

- agentes autenticados operam apenas o ambiente autorizado;
- inventário converge após reconexão;
- quatro ações do MVP são deduplicadas, reconciliáveis e auditadas;
- UI informa conexão, idade do estado e resultado sem ambiguidades;
- Docker Linux passa na matriz publicada;
- nenhum achado crítico ou alto permanece sem aceitação formal.

## Riscos e mitigação

| Risco | Impacto | Mitigação |
| --- | --- | --- |
| escopo crescer para paridade com Portainer | atraso e superfície insegura | manter não escopo e slices verticais |
| Windows atrasar o MVP | atraso de release | separar estabilidade por capability |
| protocolo prematuro | incompatibilidade futura | spikes e contract tests antes de congelar v1 |
| privilégio do socket | comprometimento do host | allowlist, identidade forte e hardening |

## Rollout e rollback

Começar com Keycloak e plano de controle containerizados em laboratório e um único host Docker Linux. Ampliar por capability e versão de agente. Rollback mantém bancos, identidade e auditoria; ações em curso são reconciliadas antes de novo envio.

## Histórico de status

| Data | Escopo | De | Para | Evidência ou motivo | Autor |
| --- | --- | --- | --- | --- | --- |
| 2026-09-06 | plano | - | proposed | criação da proposta inicial | IA assistida |
| 2026-09-07 | P-01 | not-started | in-progress | adapter Docker real e núcleo de journal iniciados; provas restantes pendentes | IA assistida |
| 2026-09-07 | P-02 | not-started | in-progress | solução, dependências centralizadas e contrato v1 compilável criados | IA assistida |