# Backend e agente

## Stack

- .NET 10 LTS e C# com nullable habilitado;
- ASP.NET Core para REST, gRPC, SignalR e health checks;
- EF Core com PostgreSQL para estado durável;
- OpenTelemetry para traces, métricas e correlação;
- xUnit para testes e Testcontainers para integrações reais.

O baseline de build usa `lzocateli/dotnet-sdk:10.0.400-noble`; API, BFF e agente Linux derivam de `lzocateli/dotnet-aspnet:10.0.11-noble`. A release fixa também os digests conforme [Distribuição e operação](distribuicao.md#imagens-base-e-toolchains). Pacotes NuGet continuam exigindo versão centralizada e verificação prévia de licença e manutenção.

## Projetos alvo

```text
backend/apps/Dokpod.Api
backend/apps/Dokpod.Bff
backend/apps/Dokpod.Agent
backend/libs/Dokpod.Domain
backend/libs/Dokpod.ControlPlane.Application
backend/libs/Dokpod.ControlPlane.Infrastructure
backend/libs/Dokpod.Agent.Application
backend/libs/Dokpod.Agent.Infrastructure
backend/tests/
```

O agente e o plano de controle compartilham somente domínio realmente comum e contratos versionados. API, BFF e agente possuem composition roots, configurações e permissões próprias. API e BFF são sempre executados em containers. O agente usa uma imagem OCI em Linux e publicação self-contained como Windows Service em Windows.

## Transporte do agente

Cada agente mantém um único stream gRPC bidirecional HTTP/2 com mTLS para a API. O contrato Protocol Buffers v1 negocia versão e capabilities e transporta apresentação, heartbeat, deltas, snapshots paginados, comandos, aceite e resultado. Inscrição e emissão inicial de certificado usam HTTPS separado; REST não substitui o canal operacional e o agente não expõe proxy genérico do engine.

Comandos são persistidos antes do envio. O stream aplica filas limitadas, sequência monotônica, fencing, deadlines e retomada por reconciliação; desconexão nunca autoriza repetir cegamente uma mutação.

## Identidade e autorização

- Keycloak é a autoridade externa obrigatória para usuários, credenciais, MFA, federação, SSO, sessões, grupos, roles, recursos, scopes e políticas.
- O Dokpod não implementa cadastro, senha, recuperação, diretório de usuários, memberships ou política de autorização própria.
- O BFF é um cliente OIDC confidencial, usa Authorization Code com PKCE, mantém tokens fora do browser e protege operações mutáveis contra CSRF.
- A API valida assinatura, issuer, audience e expiração e atua como Policy Enforcement Point antes de revelar ambiente ou container.
- Ambientes são registrados como recursos no Keycloak por integração idempotente e reconciliável; scopes iniciais incluem `environment:read`, `container:start`, `container:stop`, `container:restart`, `container:delete` e `audit:read`.
- Decisão negativa omite o recurso; indisponibilidade da autorização falha fechada com `503 application/problem+json`.
- Agentes usam certificados mTLS próprios e não recebem tokens de usuário ou credenciais administrativas do Keycloak.

## Adapters de engine

A porta de engine expõe apenas capacidades necessárias ao produto: inspeção, listagem, início, parada, reinício e exclusão. Docker e Podman possuem adapters separados.

- Docker Linux: HTTP sobre Unix domain socket.
- Docker Windows: HTTP sobre named pipe, sujeito à prova técnica.
- Podman Linux: API compatível Docker para núcleo e Libpod quando necessário.
- Chamadas negociam versão/capacidade e têm timeout e limite de resposta.

Não executar CLI por shell para operações normais e não encaminhar caminhos arbitrários da API do engine.

## Persistência

PostgreSQL armazena ambientes, certificados/revogação, capacidades, projeções, comandos, execuções e auditoria. O estado atual do engine continua sendo autoridade sobre containers em execução.

Commands e resultados têm chave composta por ambiente e ID, hash imutável do payload, deadline e fencing token. Escrita no banco e chamada ao engine não formam uma transação distribuída; estados intermediários são reconciliados por observação posterior.

O agente mantém em volume persistente protegido sua chave privada, certificados e um journal mínimo de comandos/resultados. A rotação grava novo material de forma atômica antes da troca; ausência ou permissão insegura no volume impede startup.

No Windows, o mesmo host `Dokpod.Agent` usa integração de Worker Service e é publicado self-contained para cada RID homologado. Binários e dados mutáveis ficam separados; a conta de serviço possui somente `Log on as a service`, acesso ao diretório do agente e ao named pipe necessário. O instalador não embute bootstrap token, certificado ou outro secret.

## Concorrência

- métodos assíncronos propagam `CancellationToken`;
- filas são limitadas e aplicam backpressure;
- cada container possui serialização lógica de mutações, lease renovável e fencing monotônico;
- ambientes distintos podem executar em paralelo;
- retries usam jitter e somente falhas transitórias; operações como `restart` dependem de deduplicação durável e não são declaradas naturalmente idempotentes;
- timeouts não são interpretados automaticamente como falha da mutação;
- nenhum lock permanece ativo durante I/O externo sem necessidade comprovada.

## API pública

- REST orientado a recursos, versionado em `/api/v1`;
- OpenAPI 3.1 como fonte para o cliente Angular;
- `application/problem+json` para erros;
- paginação por cursor em coleções extensas;
- ETag quando atualização concorrente de configuração for possível;
- SignalR apenas para invalidar/atualizar visualizações.

## Qualidade mínima

- testes unitários de domínio e aplicação;
- contract tests contra Docker e Podman reais;
- integração PostgreSQL real;
- matriz de imagem Linux e pacote self-contained Windows suportados;
- testes de desconexão, replay, timeout e resposta perdida;
- build, format, analyzers e testes em CI;
- smoke test das imagens sem privilégios adicionais além dos documentados.