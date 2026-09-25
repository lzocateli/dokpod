# Prontidão para Release do Dokpod

**Data da avaliação:** 2026-09-23  
**Veredito:** **NO-GO**  
**Escopo avaliado:** MVP Docker Linux, agente Linux em container e agente Windows self-contained  
**Base:** branch `development`, commit `f013471`  
**Plano relacionado:** [MVP do Dokpod](plan/mvp.md)  

**Atualização desta avaliação:** 2026-09-24, após E2E autenticado da stack candidata.  

## Resumo executivo

O núcleo técnico do Dokpod está funcional em laboratório. A stack de controle sobe com web, BFF, API, PostgreSQL e Keycloak; o agente Linux em container e o agente Windows self-contained conseguem acessar Docker Desktop, negociar mTLS e abrir sessão gRPC.

A entrega oficial ainda não está pronta. O bloqueio principal não é compilação: são gates de produto, segurança, carga, distribuição e operação que ainda não foram executados ou formalmente aceitos.

Estimativa de maturidade:

- Núcleo funcional: aproximadamente 80%.
- Release Docker Linux: aproximadamente 60%.
- Release multiplataforma de produção: abaixo de 40%.

## Evidências confirmadas

- Build completo da solução .NET em container: **0 warnings, 0 erros**.
- Testes focados de transporte: **17 aprovados**.
- Stack E2E de web, BFF e API construída e saudável.
- PostgreSQL e Keycloak reais disponíveis no laboratório.
- Gateway web retornando HTTP `200`.
- Rota protegida sem sessão redirecionando para login com HTTP `302`.
- PKI de laboratório armazenada fora do repositório em UserSecrets.
- Agente Linux em container validado contra Docker Desktop real, usando Unix socket, Docker API `1.47`, inventário e sessão gRPC mTLS.
- Agente Windows self-contained `win-x64` validado em console, usando named pipe `docker_engine`, Docker API `1.47`, inventário e sessão gRPC mTLS.
- Script de gerenciamento do Windows Service implementado com `Install`, `Start`, `Stop`, `Restart`, `Status`, `Remove` e `DryRun`.
- Invalidação ativa de sessão após revogação de identidade implementada e coberta por testes.
- Stack candidata reconstruída localmente com web, BFF e API saudáveis.
- Smoke pelo gateway: web `200` e rota protegida sem sessão `302`.
- Imagem `lzocateli/k6:2.1.0-node24.15.0-bookworm` validada (`k6 v2.1.0`).
- Imagem `lzocateli/playwright-e2e:0.1.0` validada e disponível para a suíte.
- Usuário sintético, sessão BFF e autorização UMA provisionados pelo fluxo administrativo suportado, com credenciais somente em UserSecrets.
- Agente Linux publicou snapshot inicial paginado pelo stream mTLS; a projeção PostgreSQL convergiu para 16 containers reais.
- Suíte Playwright autenticada executada pela URL canônica do gateway: **4 testes aprovados, 0 skips**, cobrindo sessão, catálogo, ambiente, inventário e visibilidade das quatro ações autorizadas.

## Matriz de gates

| Gate | Estado | Evidência ou pendência |
| --- | --- | --- |
| Build .NET | PASS | Build completo sem warnings ou erros. |
| Testes unitários e de aplicação | PASS PARCIAL | Recortes principais aprovados; falta consolidar suíte completa como gate de release. |
| Transporte gRPC/mTLS | PASS PARCIAL | Transporte e agentes validados em laboratório; reconexão após revogação ponta a ponta ainda pendente. |
| Docker Linux real | PASS PARCIAL | Unix socket, inventário e sessão validados; ciclo completo de quatro mutações precisa permanecer registrado em execução E2E final. |
| PostgreSQL real | PASS PARCIAL | Migrations e vários testes reais aprovados; suíte não é executada integralmente em todo ciclo local. |
| Keycloak real | PASS PARCIAL | Login autenticado, grupo e UMA do ambiente validados; autorização horizontal negativa permanece pendente. |
| E2E autenticado | PASS PARCIAL | 4 testes aprovados sem skips para sessão, catálogo, ambiente, inventário e ações visíveis; mutações e cenários negativos permanecem pendentes. |
| Autorização horizontal | NOT RUN | Falta provar que usuário não acessa outro ambiente, inventário, comando ou SignalR. |
| Revogação e reconexão | NOT RUN | Falta prova real PostgreSQL/Keycloak de bloqueio de reconexão após revogação. |
| Carga nominal | NOT RUN | 56 agentes, 1.120 containers e 30 usuários por 30 minutos ainda não executados; deve rodar em VM real isolada. |
| Carga de margem | NOT RUN | 100 agentes, 2.000 containers e 50 usuários ainda não executados. |
| Angular produção | PASS PARCIAL | Build de produção e fluxo autenticado validados; screenshots responsivos ainda pendentes. |
| Imagens de produção | NOT RUN | Falta gate automatizado para build, smoke, portas, mounts, usuário e health de todas as imagens. |
| SBOM e vulnerabilidades | NOT RUN | Falta SBOM, Trivy e política formal de bloqueio/aceite. |
| Assinatura e provenance | NOT RUN | Imagens e pacote Windows ainda não possuem gate de assinatura/proveniência validado. |
| Backup e restore | NOT RUN | Falta exercício real de backup e recuperação de PostgreSQL/Keycloak. |
| Windows Service real | NOT RUN | Script e DryRun existem; instalação elevada, conta dedicada, ACL, update e rollback não foram executados. |
| Podman Linux | NOT RUN | Rootless/rootful e diferenças Libpod não qualificados. |
| Compatibilidade N/N-1 | NOT RUN | Falta teste com servidor/agente de versões consecutivas. |
| Observabilidade de release | NOT RUN | Métricas, traces, alertas e diagnóstico operacional ainda não têm evidência de release. |

## Bloqueadores para entregar a stack

### 1. Autenticação e autorização

Implementar e executar jornadas autenticadas reais:

- login e logout pelo browser;
- sessão BFF sem tokens no browser;
- cadastro de ambiente autorizado;
- autorização horizontal entre dois ambientes;
- inventário, comandos e SignalR protegidos por recurso;
- CSRF;
- Keycloak indisponível, token expirado e decisão indeterminada;
- agente falso, identidade revogada e reconexão bloqueada.

### 2. E2E funcional

Adicionar uma suíte Playwright/black-box versionada cobrindo:

- login;
- catálogo e entrada de ambiente;
- inventário e idade da projeção;
- start, stop, restart e delete;
- confirmação de exclusão;
- polling até estado terminal;
- erros `403`, `404`, `409` e `503`;
- screenshots dos estados relevantes.

### 3. Carga e recuperação

Executar e guardar os resultados para:

- 56 agentes, aproximadamente 1.120 containers e 30 usuários por 30 minutos;
- reconexões simultâneas e resposta perdida;
- comandos concorrentes em ambientes diferentes;
- margem de 100 agentes, 2.000 containers e 50 usuários;
- restart do control plane com recuperação de comandos não terminais;
- backup, restore e verificação de auditoria após recuperação.

### 4. Hardening e cadeia de fornecimento

Concluir:

- threat model revisado;
- SBOM de imagens e pacote Windows;
- scan de vulnerabilidades;
- assinatura de imagens e pacote;
- provenance verificável;
- tags e digests reproduzíveis;
- revisão de dependências e licenças;
- nenhum secret, certificado privado ou dado real em Git, logs ou imagens.

### 5. Distribuição Windows

Executar em host Windows limpo, sem runtime .NET:

- publish self-contained por RID suportado;
- instalação real como Windows Service;
- conta dedicada, ACL do named pipe e ACL do diretório de dados;
- inicialização atrasada e recuperação limitada;
- atualização atômica;
- rollback sem perder identidade ou journal;
- remoção do serviço preservando dados por padrão.

### 6. Capabilities e matriz suportada

Publicar inicialmente somente a combinação comprovada. A recomendação atual é:

- Docker Linux: candidato a suportado após fechar os gates de release;
- Docker Windows/Desktop: experimental até concluir Windows Service e ACL;
- Podman Linux: adiado até testes rootless/rootful;
- Podman Windows: fora do escopo inicial, tratado como engine Linux dentro de VM quando aplicável.

## Sequência mínima recomendada

1. Completar a jornada Playwright com mutações, confirmação de exclusão e cenários negativos.
2. Provar autorização horizontal e revogação/reconexão com PostgreSQL real.
3. Executar todos os testes PostgreSQL no pipeline.
4. Executar carga nominal e carga de margem.
5. Fechar threat model e revisão de segurança.
6. Gerar SBOM, executar scans e registrar correções ou aceites formais.
7. Automatizar build e smoke das imagens API, BFF, web e agente.
8. Executar backup/restore.
9. Validar Windows Service em host limpo.
10. Atualizar matriz de suporte, runbooks e critérios de rollback.
11. Fazer revisão humana final e decidir GO/NO-GO.

## Riscos residuais

- O socket Docker e o named pipe concedem poder elevado sobre o host.
- A stack ainda depende de configuração externa de Keycloak, PostgreSQL e certificados.
- O laboratório usa identidade técnica sintética; isso não substitui enrollment/provisionamento oficial.
- A execução em console do agente Windows não substitui a validação como Windows Service.
- O README raiz ainda comunica um estado mais antigo de scaffolding e deve ser alinhado antes da release.

## Critério de mudança do veredito

O veredito só deve mudar para `GO` quando não houver gates obrigatórios em `NOT RUN`, não houver falhas críticas sem aceitação formal e houver evidência humana dos gates de segurança, autorização, carga, distribuição e recuperação.
