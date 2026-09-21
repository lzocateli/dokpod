# Plano: MVP do Dokpod

**Status:** approved  
**Data de criação:** 2026-09-06  
**Última atualização:** 2026-09-21
**Responsáveis:** equipe Dokpod  
**Origem:** IA assistida  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** ADR 2026-0001

Entregar um MVP self-hosted que registre agentes, mostre inventário e execute com segurança início, parada, reinício e exclusão de containers Docker em Linux.

## Contexto e premissas
- .NET 10 e Angular 22 são requisitos definidos;
- backend, BFF e frontend são sempre executados em containers;
- o agente Linux é distribuído como container e o agente Windows como Worker Service self-contained;
- criação de containers e stacks;
- Kubernetes, Swarm e registries;
- console interativo e acesso a arquivos;
- atualização automática do agente;
- alta disponibilidade do plano de controle na primeira release.
- funcionalidades exclusivas, licenciamento e distribuição da edição Business.

## Dependências e decisões

- ADR 2026-0001 aceito;
- ADR 2026-0002 aceito e configuração inicial do Keycloak;
### P-01: Prova Docker Linux e transporte

**Status:** in-progress  
**Dependências:** nenhuma

**Objetivo:** eliminar as maiores incertezas antes do scaffolding definitivo.

- executar as provas 1, 4, 5 e 8 de `docs/viabilidade.md` com evidência reproduzível.

Evidências:

- `test-docker-integration-container`: 1 teste aprovado contra Docker Engine real pelo Unix socket, cobrindo negociação da API, listagem, início, reinício, parada e exclusão sem volumes ou force;
- `test-backend-container`: 21 testes aprovados para deduplicação, fencing, deadline, revisão do alvo, resultado reconciliável, journal durável e negociação da identidade do agente por fingerprint;
- comandos do protocolo são convertidos para o domínio somente após validar ambiente, fencing token, UUID, ID imutável do container, revisão, hash SHA-256 e deadline; entradas malformadas são rejeitadas antes de alcançar o engine;
- `build-agent-image` e `smoke-agent-image`: imagem Linux construída e executada como usuário não root, filesystem read-only e acesso ao socket somente por grupo suplementar;
- certificados sem EKU de cliente e versões de protocolo incompatíveis são rejeitados antes da ativação da sessão;
- host gRPC configurado com listener HTTPS/HTTP2 explícito, certificado de cliente obrigatório e revogação de cadeia habilitada;
- testes focados de transporte aprovados (12 testes), incluindo handshake Kestrel/mTLS, fencing por reconexão, invalidação ativa de sessão e rejeição de metadados inválidos;
- testes focados do negociador aprovados (4 testes), cobrindo fingerprint, EKU, versão de protocolo, enums e capabilities;
- invalidação ativa encerra o stream com `agent_session_fenced` e a confirmação do handshake verifica a sessão antes do primeiro envio;
- revogação persistente da identidade, bloqueio de reconexão após revogação e integração com o caso de uso de cadastro permanecem pendentes para P-03;

### P-02: Fundação do monorepo e contratos
**Status:** in-progress  
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

Evidências:

- `Dokpod.slnx`, gerenciamento central de pacotes e separação inicial entre domínio, aplicação, infraestrutura e contratos criados;
- `build-backend-container`: build aprovado sem avisos ou erros;
- `test-backend-container`: 21 testes aprovados;
- Dockerfile multi-stage do agente e `.dockerignore` validados por build e smoke test;

### P-03: Identidade e cadastro de ambientes

**Status:** in-progress  
**Responsável:** Lincoln Zocateli  
**Dependências:** P-02

**Objetivo:** autenticar usuários e cadastrar agentes sem credenciais estáticas compartilhadas.

Entregas:

- auditoria inicial.
- endpoint inicial de cadastro protegido por Keycloak, com persistência PostgreSQL e auditoria append-only.

Validação:
- testes de login/logout sem token no browser, Keycloak indisponível, autorização horizontal/SignalR, CSRF, corrida no bootstrap, agente falso, ambiente divergente, clone concorrente, replay, token expirado e revogação de stream.

Evidências:
- caso de uso, persistência de ambientes e endpoint protegido implementados; integração real PostgreSQL/Keycloak e testes horizontais permanecem pendentes.
- catálogo paginado `GET /api/v1/environments` implementado com autorização `environment:read` por recurso, omissão de ambientes negados, falha fechada em decisão indeterminada e ausência de total global;
- tela inicial Angular lista os ambientes autorizados, diferencia habilitados e desabilitados e permite cadastrar um ambiente previamente provisionado no Keycloak com antiforgery e tratamento de conflito;

### P-04: Inventário reconciliável

**Status:** in-progress  
**Responsável:** Lincoln Zocateli  
**Dependências:** P-03

**Objetivo:** mostrar estado confiável e idade dos dados por ambiente.

Entregas:

- modelo de domínio para snapshots, deltas e mudanças de containers por revisão monotônica;
- projeção PostgreSQL reconstruível por ambiente;
- ingestão de deltas pelo stream gRPC autenticado, com solicitação de snapshot após base divergente ou lacuna de revisão.
- montagem limitada e ordenada de snapshots paginados por sessão, persistidos atomicamente somente após a última página;
- consulta REST autorizada por `environment:read`, com cursor opaco, limite máximo, revisão e idade da projeção;
- invalidação SignalR mínima após delta aceito ou snapshot completo persistido.

Validação:

- reconexão, lacuna de sequência e carga nominal de 56 agentes, aproximadamente 1.120 containers e 30 usuários simultâneos sem perda ou crescimento ilimitado;
Evidências:

- testes de domínio cobrem aplicação de delta, base stale, lacuna e revisão não monotônica;
- testes de transporte mTLS comprovam solicitação de snapshot após lacuna de inventário;
- teste de integração PostgreSQL real comprova aplicação de delta e persistência da projeção;
- testes da aplicação cobrem ordenação, identidade e atomicidade de páginas, além da autorização anterior à leitura;
- contrato OpenAPI documenta a consulta paginada do inventário e seus erros;
- teste PostgreSQL real comprova substituição atômica de snapshot, atualização de container existente e paginação por cursor;
- teste de transporte mTLS comprova persistência do snapshot completo e entrega da invalidação SignalR;
- autorização HTTP horizontal permanece pendente;
- carga nominal de 56 agentes, aproximadamente 1.120 containers e 30 usuários simultâneos por 30 minutos: **NOT RUN**.

### P-05: Ciclo de vida de containers

**Status:** in-progress  
**Responsável:** Lincoln Zocateli  
**Dependências:** P-04

**Objetivo:** iniciar, parar, reiniciar e excluir containers com confirmação e auditoria.

Entregas:

- comandos duráveis com deduplicação por ID/hash, fencing e reconciliação;
- API REST autorizada para iniciar, parar, reiniciar e excluir containers;
Validação:

- testes reais de sucesso, timeout, replay após restart, ID com payload divergente, alvo recriado, desconexão e resposta perdida.
Evidências:

- núcleo local do agente valida deadline, fencing, tipo, alvo imutável, revisão e deduplicação por ID/hash;
- journal em arquivo persiste comandos e resultados entre instâncias;
- rejeições determinísticas são persistidas e reproduzidas após reabertura do journal sem executar a engine;
- fencing stale é rejeitado antes do journal, permitindo redistribuição legítima na sessão ativa;
- deadline é revalidado após a espera pela serialização e resultados terminais são persistidos mesmo após cancelamento do chamador;
- control plane possui modelo de comando pendente e fila PostgreSQL com chave por ambiente/ID, deadline, fencing, estado e timestamps;
- envelope do comando valida ação permitida, alvo imutável, revisão, SHA-256 canônico, deadline UTC e fencing positivo;
- enqueue concorrente no PostgreSQL comprova um único vencedor, replay idêntico e rejeição de hash ou envelope divergente sem sobrescrita;
- stream bidirecional entrega comandos ao agente conectado mesmo quando ele não envia novas mensagens e persiste despacho, aceite e resultado;
- transições PostgreSQL são monotônicas de `Pending` até estado terminal, com replay terminal idempotente e rejeição de regressão ou resultado divergente;
- encerramento ou fencing da sessão cancela leituras e dequeues pendentes para impedir que um stream obsoleto consuma comandos futuros;
- abertura de sessão reclama no PostgreSQL comandos não terminais e não expirados ainda não enviados naquele fencing, preserva o fencing original e registra separadamente o fencing de redespacho;
- claim concorrente por compare-and-set entrega cada comando a apenas um reclamante, enquanto um fencing posterior permite nova tentativa para reconciliar resposta perdida;
- worker do agente mantém stream mTLS, serializa metadata e sequência em um único writer, envia heartbeats durante mutações e reporta aceite antes do resultado terminal;
- replay de comando aceito sem resultado retoma a mutação; replay com resultado persistido não repete o efeito no engine;
- teste ponta a ponta com Kestrel executa o worker real do agente e comprova `Dispatched` → `Accepted` → `Succeeded` através do stream;
- sweep periódico e claim de sessão terminalizam comandos vencidos com `expired_command`: `Pending` nunca despachado torna-se `Failed`, enquanto `Dispatched`, `Accepted` ou `Pending` já reclamado tornam-se `Indeterminate` porque o efeito pode ter ocorrido;
- teste PostgreSQL real comprova a expiração, preservação de estados terminais e comandos futuros e idempotência do sweep;
- `POST /api/v1/environments/{environmentId}/containers/{containerId}/commands` aceita somente `start`, `stop`, `restart` e `delete`, exige `Idempotency-Key` UUID e deriva no servidor o hash canônico e o fencing da sessão ativa;
- o caso de uso autoriza o scope específico antes de consultar o ambiente, falha fechado quando a autorização está indisponível e não enfileira comandos para ambiente sem scope ou agente offline;
- replay com a mesma intenção permanece idempotente após renovação do fencing da sessão, preservando o token original para auditoria e usando separadamente o fencing de redespacho;
- contrato OpenAPI documenta submissão assíncrona, resposta `202` e erros `400`, `401`, `403`, `404`, `409` e `503` sem expor metadados internos do agente;
- `GET /api/v1/environments/{environmentId}/commands/{commandId}` consulta o estado durável somente após autorização `environment:read` e retorna ação, alvo, revisões, estado, resultado e timestamps sem expor hash ou fencing;
- consulta inexistente retorna `404` somente após autorização; decisões negadas, indisponíveis ou malformadas não acessam a persistência nem revelam a existência do comando;
- intenção autorizada e resultado terminal são registrados na auditoria append-only com correlação por `commandId`; criação/transição e evento correspondente são atômicos no PostgreSQL;
- replay de criação ou resultado terminal não duplica auditoria, e falha do writer desfaz a mutação do comando;
- expiração gera evento terminal pelo ator técnico `control-plane`, com outcome `Failed` para comando nunca despachado e `Indeterminate` quando o efeito pode ter ocorrido;
- resultado definitivo tardio reconcilia somente `Indeterminate` originado por `expired_command`, preservando os eventos de expiração e resultado na trilha append-only;
- testes de domínio: 28 aprovados; testes de aplicação do agente: 23 aprovados; testes de infraestrutura do agente: 4 aprovados;
- testes da aplicação do control plane: 29 aprovados; testes da API e integrações: 76 aprovados, incluindo 18 testes de schema e persistência com PostgreSQL real;
- UI Angular de lifecycle implementada em rota lazy por ambiente, com inventário paginado, idade da projeção, estados loading/vazio/erro/forbidden/indisponível, ações filtradas por scope e confirmação contextual de exclusão;
- cliente OpenAPI gerado opera same-origin pelo BFF, obtém antiforgery antes de mutações, envia chave idempotente e acompanha o comando por polling cancelável até estado terminal;
- testes frontend: 10 aprovados, cobrindo catálogo, cadastro, sessão expirada, fachada de lifecycle, erro `403`, refresh após resultado terminal e headers/corpo efetivamente enviados pelo cliente gerado; build Angular de produção aprovado;
- fluxo black-box autenticado e screenshots contra a stack E2E permanecem pendentes.
- estratégia de particionamento/retenção da tabela de comandos e garantia de persistência do rename do journal contra queda de energia permanecem bloqueios operacionais antes de produção.

### P-06: Hardening e release candidata

**Status:** not-started  

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
**Responsável:** Lincoln Zocateli  
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
| 2026-09-08 | P-01 | in-progress | in-progress | serialização por ambiente/alvo e validação do mapeamento Protobuf para domínio comprovadas por 28 testes backend | IA assistida |
| 2026-09-08 | P-01 | in-progress | in-progress | host HTTPS/HTTP2, cliente gRPC com certificado e fencing por ambiente compilados; handshake end-to-end, revogação ativa, perda de resposta e reconexão permanecem pendentes | IA assistida |
| 2026-09-08 | P-01 | in-progress | in-progress | dois testes focados adicionados e aprovados; handshake Kestrel end-to-end, revogação ativa, perda de resposta e reconexão permanecem pendentes | IA assistida |
| 2026-09-17 | P-03 | not-started | in-progress | caso de uso, persistência de ambientes e endpoint protegido adicionados; integração real PostgreSQL/Keycloak e testes horizontais permanecem pendentes | IA assistida |
| 2026-09-21 | P-04 | not-started | in-progress | reconciliação de deltas, projeção PostgreSQL e solicitação de snapshot por lacuna integradas ao stream; prova PostgreSQL real depende da configuração do ambiente | IA assistida |
| 2026-09-21 | P-04 | in-progress | in-progress | snapshots paginados, substituição atômica, consulta REST autorizada e invalidação SignalR implementados; validações de integração e carga permanecem pendentes | IA assistida |
| 2026-09-21 | P-04 | in-progress | in-progress | transporte mTLS de snapshot completo e invalidação SignalR validados; carga nominal permanece NOT RUN | IA assistida |
| 2026-09-21 | P-05 | not-started | in-progress | replay durável de rejeições determinísticas validado após reabertura do journal, sem nova execução da engine | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | fila PostgreSQL adicionada e validada sob enqueue concorrente, replay idêntico e hash divergente | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | revisão de código e segurança endureceu fencing, deadline, cancelamento e envelope canônico; particionamento/retenção e crash durability seguem pendentes | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | despacho bidirecional, aceite e resultado persistente validados por mTLS e PostgreSQL real; reconciliação após restart segue pendente | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | recuperação de comandos não terminais por fencing de redespacho validada com mTLS e claim concorrente no PostgreSQL real | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | worker real do agente integrado ao stream e validado ponta a ponta até resultado terminal; expiração, API, auditoria e UI seguem pendentes | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | expiração terminal periódica e durante claim implementada; PostgreSQL real comprovou estados seguros e idempotência; API, auditoria e UI seguem pendentes | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | API REST autorizada de lifecycle implementada com idempotência entre reconexões, contrato OpenAPI e testes de falha fechada; auditoria, consulta de estado e UI seguem pendentes | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | consulta REST autorizada do estado durável implementada sem expor hash ou fencing; auditoria atômica e UI seguem pendentes | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | auditoria append-only correlacionada por comando e atômica com intenção, resultado e expiração validada no PostgreSQL real; UI segue pendente | IA assistida |
| 2026-09-21 | P-05 | in-progress | in-progress | UI Angular de inventário e lifecycle implementada com BFF, antiforgery, scopes, polling terminal e testes; E2E autenticado e catálogo de ambientes seguem pendentes | IA assistida |
| 2026-09-21 | P-03 | in-progress | in-progress | catálogo autorizado e cadastro de ambientes adicionados à tela inicial; provisionamento Keycloak e E2E horizontal seguem pendentes | IA assistida |