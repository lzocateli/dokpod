# Prontidão para Release do Dokpod

**Data da avaliação:** 2026-09-25
**Veredito:** **NO-GO**  
**Escopo avaliado:** MVP Docker Linux, agente Linux em container e agente Windows self-contained  
**Base:** branch `development`, commit `b25cdb2`
**Plano relacionado:** [MVP do Dokpod](plan/mvp.md)  

**Atualização desta avaliação:** 2026-09-25, após lifecycle e autorização horizontal E2E.

## Resumo executivo

O núcleo técnico do Dokpod está funcional em laboratório. A stack de controle sobe com web, BFF, API, PostgreSQL e Keycloak; o agente Linux em container e o agente Windows self-contained conseguem acessar Docker Desktop, negociar mTLS e abrir sessão gRPC.

A entrega oficial ainda não está pronta. O bloqueio principal não é compilação: são gates de produto, segurança, carga, distribuição e operação que ainda não foram executados ou formalmente aceitos.

Resumo de maturidade:

- núcleo funcional Docker Linux validado ponta a ponta em laboratório;
- candidata de release ainda bloqueada por carga, supply chain assinada e operação;
- matriz Windows/Podman ainda não qualificada para suporte estável.

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
- Lifecycle real executado pela UI sobre container sintético dedicado: start, restart, stop e delete confirmados, todos `Succeeded`, com remoção do alvo e auditoria persistida.
- Autorização horizontal negada com `403` para estado, inventário e comando de outro ambiente; nenhuma tentativa negada foi persistida.
- Cookie antiforgery `__Host-` validado com `Path=/`; migrations pendentes aplicadas e schema de comandos confirmado com chave global e 133 partições.
- Revogação autenticada via browser validada: estado persistido, evento auditado, stream encerrado por fencing e reconexões do certificado revogado rejeitadas; identidade sintética restaurada após a prova.
- SignalR validado pelo browser: conexão no ambiente autorizado e erro operacional no ambiente negado, com base path e antiforgery corretos.
- Screenshots finais verificadas em desktop 1440x900, mobile 390x844 e forbidden mobile, com campos sensíveis mascarados e sem sobreposição ou corte relevante.
- Suíte backend geral: **183 aprovados**; suíte PostgreSQL real: **23 aprovados, 0 skips**; frontend: **12 aprovados**.
- Suíte E2E combinada: **8 aprovados, 0 skips** (revogação executada e aprovada separadamente por ser destrutiva), incluindo logout, `404` autorizado e `409` de cadastro duplicado.
- Gitleaks `8.30.1` no histórico completo: **0 leaks**.
- Trivy `0.72.0`: quatro relatórios e quatro SBOMs CycloneDX; API/BFF/agente com 0 HIGH/CRITICAL, web com 34 HIGH registrados e 0 CRITICAL; nenhum CRITICAL corrigível.
- Backup/restore completo validado em database temporária, com 275 tabelas Dokpod e realm Keycloak presentes; artifacts temporários removidos após o exercício.
- CI configurado para migrations/integrações PostgreSQL, test/build Angular e
  build/Trivy/SBOM das quatro imagens pelo script local comum; actionlint e fluxo
  representativo API aprovados, primeira execução remota ainda pendente.

## Matriz de gates

| Gate | Estado | Evidência ou pendência |
| --- | --- | --- |
| Build .NET | PASS | Build completo sem warnings ou erros. |
| Testes unitários e de aplicação | PASS | 183 testes backend, 23 PostgreSQL reais e 12 frontend aprovados. |
| Transporte gRPC/mTLS | PASS PARCIAL | Transporte, fencing e bloqueio de reconexão após revogação validados; compatibilidade N/N-1 permanece pendente. |
| Docker Linux real | PASS PARCIAL | Unix socket, inventário, sessão e quatro mutações reais validados; carga e recuperação permanecem pendentes. |
| PostgreSQL real | PASS | Migrations aplicadas, chave global e 133 partições confirmadas; 23 integrações reais aprovadas sem skips. |
| Keycloak real | PASS PARCIAL | Login autenticado, grupo e UMA do ambiente validados; autorização horizontal negativa permanece pendente. |
| E2E autenticado | PASS PARCIAL | Sessão, catálogo, ambiente, inventário, lifecycle, revogação e screenshots aprovados; indisponibilidade permanece pendente. |
| Autorização horizontal | PASS | Estado, inventário, comando e ingresso SignalR negados sem persistência ou associação ao grupo. |
| Revogação e reconexão | PASS | Revogação via browser persistida e auditada; stream fenced e reconexões rejeitadas com certificado revogado. |
| Carga nominal | NOT RUN | 56 agentes, 1.120 containers e 30 usuários por 30 minutos ainda não executados; deve rodar em VM real isolada. |
| Carga de margem | NOT RUN | 100 agentes, 2.000 containers e 50 usuários ainda não executados. |
| Angular produção | PASS | Build de produção, fluxo autenticado, realtime e screenshots desktop/mobile validados. |
| Imagens de produção | PASS PARCIAL | API, BFF, web e agente construídos e saudáveis; job CI implementado, mas inspeções de usuário, portas e mounts ainda precisam ser automatizadas. |
| SBOM e vulnerabilidades | PASS PARCIAL | Quatro SBOMs/scans locais sem CRITICAL e job CI implementado; web mantém 34 HIGH registrados e a primeira execução remota está pendente. |
| Assinatura e provenance | NOT RUN | Imagens e pacote Windows ainda não possuem gate de assinatura/proveniência validado. |
| Backup e restore | PASS | Dump completo restaurado e verificado em database temporária; cleanup concluído. |
| Windows Service real | NOT RUN | Script e DryRun existem; instalação elevada, conta dedicada, ACL, update e rollback não foram executados. |
| Podman Linux | NOT RUN | Rootless/rootful e diferenças Libpod não qualificados. |
| Compatibilidade N/N-1 | NOT RUN | Falta teste com servidor/agente de versões consecutivas. |
| Observabilidade de release | NOT RUN | Métricas, traces, alertas e diagnóstico operacional ainda não têm evidência de release. |

## Bloqueadores para entregar a stack

### 1. Autenticação e autorização

Completar jornadas negativas ainda não executadas:

- expiração/renovação de sessão;
- Keycloak indisponível, token expirado e decisão indeterminada no browser;
- cadastro concorrente de ambiente;
- indisponibilidade de PostgreSQL e recuperação da UI.

### 2. E2E funcional

Ampliar a suíte Playwright/black-box já versionada para cobrir:

- sessão expirada;
- indisponibilidade `503` de identidade e persistência;
- recuperação após retorno das dependências.

### 3. Carga e recuperação

Executar e guardar os resultados para:

- 56 agentes, aproximadamente 1.120 containers e 30 usuários por 30 minutos;
- reconexões simultâneas e resposta perdida;
- comandos concorrentes em ambientes diferentes;
- margem de 100 agentes, 2.000 containers e 50 usuários;
- restart do control plane com recuperação de comandos não terminais;
- backup, restore e verificação de auditoria após recuperação.

### 4. Hardening e cadeia de fornecimento

Concluir os itens ainda pendentes:

- threat model revisado;
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

1. Completar a jornada Playwright com indisponibilidade de dependências.
2. Executar o novo CI no GitHub e adicionar inspeções de usuário, portas e mounts.
3. Executar carga nominal e carga de margem.
4. Fechar threat model e revisão de segurança.
5. Assinar imagens e pacote Windows e gerar provenance verificável.
6. Validar Windows Service em host limpo.
7. Atualizar matriz de suporte, runbooks e critérios de rollback.
8. Fazer revisão humana final e decidir GO/NO-GO.

## Riscos residuais

- O socket Docker e o named pipe concedem poder elevado sobre o host.
- A stack ainda depende de configuração externa de Keycloak, PostgreSQL e certificados.
- O laboratório usa identidade técnica sintética; isso não substitui enrollment/provisionamento oficial.
- A execução em console do agente Windows não substitui a validação como Windows Service.

## Critério de mudança do veredito

O veredito só deve mudar para `GO` quando não houver gates obrigatórios em `NOT RUN`, não houver falhas críticas sem aceitação formal e houver evidência humana dos gates de segurança, autorização, carga, distribuição e recuperação.
