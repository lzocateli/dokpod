# Backend-For-Frontend do Dokpod

## Objetivo

O BFF (`backend/apps/Dokpod.Bff`) é a única fronteira HTTP do browser com o plano de controle. Ele inicia o login OIDC, mantém os tokens no servidor, emite somente um cookie técnico de sessão e protege operações mutáveis contra CSRF. O Keycloak continua sendo a autoridade de identidade; a API continua sendo o Policy Enforcement Point (PEP).

A implementação segue a mesma separação de responsabilidades validada no AltivyNotes, mas não copia código nem contratos entre os repositórios.

## Fluxo de autenticação

1. O browser chama `GET /bff/login` com um `returnUrl` opcional.
2. O BFF normaliza o retorno para um caminho local e inicia Authorization Code com PKCE no realm `dokpod`.
3. O callback OIDC troca o código no backchannel do BFF. Redirecionamentos HTTP automáticos são desabilitados e o uso de HTTP só é aceito para autoridade loopback em Development.
4. O BFF salva access token, refresh token e expiração no ticket server-side. O browser recebe somente `__Host-Dokpod.Session`, com `Secure`, `HttpOnly`, `SameSite=Lax` e validade de oito horas.
5. Próximo da expiração, `CookieTokenRefreshEvents` coordena uma única renovação por refresh token, atualiza o ticket e nunca registra os valores dos tokens.
6. `GET /bff/session` expõe apenas o estado mínimo da sessão para a SPA.
7. `GET /bff/antiforgery` emite o token de requisição. `POST /bff/logout` exige esse token, encerra o cookie local e inicia o logout OIDC.

## Proteções

- Fallback policy exige usuário autenticado, exceto health checks, login e sessão.
- A rota de login possui rate limit por endereço remoto: 10 tentativas por 60 segundos, sem fila.
- Métodos diferentes de `GET`, `HEAD` e `OPTIONS`, além de WebSocket, exigem uma origem configurada em `Security:AllowedOrigins`.
- O cookie antiforgery é host-only, `Secure`, `HttpOnly`, `SameSite=Strict` e usa o header configurado em `Bff:AntiforgeryHeaderName`.
- As mutações REST e o `POST` de negotiate do hub exigem validação antiforgery; leituras permanecem separadas das operações mutáveis.
- Headers encaminhados só são aceitos de `Security:TrustedProxies` e `Security:TrustedNetworks`.
- Data Protection usa um key ring persistente. Fora de Development, `Security:DataProtectionCertificatePath` e `Security:DataProtectionCertificatePassword` são obrigatórios e protegem o key ring com certificado.
- O BFF não aceita URL de retorno absoluta, não expõe tokens ao browser e não registra secrets ou tokens.
- Falhas de conexão e timeout do downstream retornam 502/504 sem detalhes sensíveis; redirects e cookies do downstream não são repassados ao browser.
- O relay usa allowlist de headers para evitar spoofing de `X-Forwarded-*`, `Proxy-*` e headers de transporte; respostas sem `Content-Length` também são limitadas antes de serem enviadas ao browser.

## Rotas atuais

| Rota | Acesso | Finalidade |
| --- | --- | --- |
| `/health/live` | anônimo | liveness sem dependências externas |
| `/health/ready` | anônimo | readiness incluindo discovery do Keycloak |
| `/bff/login` | anônimo, limitado | inicia o login OIDC |
| `/bff/session` | anônimo | retorna o estado sanitizado da sessão |
| `/bff/antiforgery` | autenticado | emite token antiforgery |
| `/bff/logout` | autenticado + antiforgery | encerra sessão local e no Keycloak |

## Estado atual da integração

O BFF possui relay REST `/api/v1` e relay WebSocket server-side `/hubs`, com o Bearer inserido somente no salto BFF -> API. A API agora expõe a primeira superfície HTTP protegida (`GET /api/v1/session`) e o hub `/hubs/control-plane`.

Essa é uma primeira fatia de integração, não o fechamento do fluxo funcional completo: a autorização horizontal por ambiente ainda precisa ser aplicada antes de permitir grupos do hub, e os endpoints de inventário/comandos ainda dependem dos casos de uso do plano de controle.
A aplicação já possui a porta `IEnvironmentAuthorizationDecider` e o `EnvironmentAccessService`, mas o adapter externo do Keycloak ainda não está registrado na API. O hub deve permanecer sem liberação operacional de grupos até essa decisão ser aplicada por recurso e scope, com falha fechada.

O adapter segue o fluxo UMA usado no AltivyNotes: a API envia ao endpoint de token do realm `dokpod` um `grant_type=uma-ticket`, a `audience` da API e a permissão `resource#scope`, usando o access token recebido do BFF somente no header server-side. Respostas negadas resultam em `Denied`; timeout, transporte indisponível ou JSON inválido resultam em `Indeterminate` e nunca liberam o recurso.

Configuração da API: `Authentication:Keycloak:Authority`, `Authentication:Keycloak:Audience` e `Authentication:Keycloak:DecisionTimeoutSeconds` (padrão de 15 segundos). A API valida authority absoluta sem query/fragmento, audience não vazia e timeout entre 1 e 30 segundos antes de usar o adapter.

O hub `control-plane` usa o `sub` apenas como identidade do ator, consulta `urn:dokpod:environment:{id}` com `environment:read` e só então adiciona a conexão ao grupo. O browser nunca recebe nem fornece esse Bearer.

O adapter rejeita antes da rede qualquer resource fora do namespace `urn:dokpod:environment:{guid}` ou scope fora de `EnvironmentResourceScopes.Supported`. Chamadores futuros devem derivar o ator do principal autenticado e manter o access token correspondente ao mesmo contexto; não é permitido combinar identidade de um usuário com token de outro.

### O que é o adapter UMA

UMA (`User-Managed Access`) é o fluxo de autorização do Keycloak usado para obter uma decisão sobre um recurso e um scope específicos. No Dokpod, o adapter `KeycloakAuthorizationDecisionService` traduz a porta de aplicação `IEnvironmentAuthorizationDecider` para a API HTTP do Keycloak.

Ele não autentica o usuário novamente, não cria políticas locais e não substitui o BFF. Sua responsabilidade é:

1. receber da API o recurso, o scope, o ator autenticado e o access token server-side;
2. enviar ao endpoint de token do realm um pedido `uma-ticket` com `audience` e `permission` no formato `resource#scope`;
3. interpretar a decisão `result` retornada pelo Keycloak;
4. devolver `Allowed`, `Denied` ou `Indeterminate` para o caso de uso da API;
5. registrar somente resultado, recurso, scope, correlation ID e duração, nunca o token.

Exemplo do pedido lógico:

```text
grant_type=urn:ietf:params:oauth:grant-type:uma-ticket
audience=dokpod-api
permission=urn:dokpod:environment:{environmentId}#environment:read
response_mode=decision
Authorization: Bearer <token recebido server-side>
```

`Allowed` permite a operação. `Denied` produz negação, normalmente HTTP 403. `Indeterminate` representa indisponibilidade, timeout ou resposta inválida do Keycloak e deve falhar fechada, sem revelar o recurso ou adicionar a conexão a um grupo SignalR.

O adapter é usado pela API, inclusive no `ControlPlaneHub`, depois que o BFF encaminha a requisição. O frontend nunca chama UMA, nunca conhece a `audience`, nunca monta `permission` e nunca recebe o token usado na decisão.

A superfície inicial foi validada por testes focados de composição: as rotas de sessão e hub exigem autorização, o grupo de ambiente é determinístico e identificadores inválidos são rejeitados. Isso não substitui a validação E2E com Keycloak e navegador real.

A integração E2E deve ser habilitada somente quando existirem, em conjunto:

- endpoints HTTP versionados da API (`/api/v1`) para os recursos do MVP;
- relay server-side que copie apenas headers permitidos e acrescente o Bearer token da sessão;
- hub SignalR/WebSocket com autorização por ambiente, validação de Origin e fechamento correto de conexão;
- testes de autorização horizontal, token expirado, resposta 401/403, timeout e indisponibilidade da API;
- stack Compose/NGINX com BFF como único upstream do browser.

Não se deve apontar o browser diretamente para a API para contornar essa pendência.

### SignalR/WebSocket e momento de implementação

SignalR/WebSocket é importante para a operação diária porque reduz o polling e a latência de eventos de reconexão de agentes, alterações de inventário, mudanças de estado de comandos, fencing e reconciliação. Ele não é necessário para login, sessão, logout, operações REST ou para manter tokens fora do frontend.

Sua implementação deve ocorrer depois da API REST versionada, do relay REST e do contrato de eventos autorizados por ambiente. Até lá, o frontend pode usar REST com polling limitado como fallback. O hub não substitui o REST como fonte durável do estado: eventos devem funcionar como invalidações mínimas, seguidas de consulta autorizada à API.

O relay SignalR/WebSocket será considerado completo somente com negotiate/upgrade, validação de Origin, Bearer inserido exclusivamente no salto BFF -> API, fechamento e reconexão testados, sem token em URL, Web Storage, DOM, logs ou respostas ao browser. Essa execução está planejada no P05-04 de [p05-bff-keycloak-relay.md](plan/p05-bff-keycloak-relay.md) e não deve bloquear a primeira entrega REST do BFF.

## Configuração mínima

Os valores sensíveis são injetados por secret provider ou arquivo montado fora do repositório. O `appsettings.json` contém somente defaults não secretos.

- `Authentication:Keycloak:Authority`: issuer do realm dedicado `dokpod`;
- `Authentication:Keycloak:ClientId`: client confidencial `dokpod-bff`;
- `Authentication:Keycloak:ClientSecret`: secret injetado em runtime;
- `Security:DataProtectionKeysPath`: diretório persistente das chaves;
- `Security:AllowedOrigins`: origens HTTPS exatas do browser;
- `Security:TrustedProxies` e `Security:TrustedNetworks`: proxies/rede autorizados;
- `Security:DataProtectionCertificatePath` e `Security:DataProtectionCertificatePassword`: certificado e senha do key ring, obrigatórios fora de Development;
- `Downstream:Api:BaseUrl`: URL interna da API HTTP; em Development pode usar HTTP conforme a política de rede do ambiente;
- `Downstream:Api:MaxRequestContentLengthBytes` e `Downstream:Api:MaxResponseContentLengthBytes`: limites de corpo do relay, inclusive para uploads chunked e respostas sem `Content-Length`;
- `Bff:TokenRefreshTimeoutSeconds` e `Bff:TokenRefreshMaxResponseContentBufferSize`: limites do refresh;
- `Downstream:Api:TimeoutSeconds`, `Downstream:Api:MaxRequestContentLengthBytes` e `Downstream:Api:MaxResponseContentLengthBytes`: timeout e limites do relay para a API;
- `Bff:LoginRateLimitPermitLimit`, `Bff:LoginRateLimitWindowSeconds` e `Bff:LoginRateLimitQueueLimit`: política de login.

## Validação

Na raiz do repositório, com Docker disponível:

```powershell
docker run --rm --group-add 0 `
  --volume "${PWD}:/workspace" `
  --workdir /workspace `
  lzocateli/dotnet-sdk:10.0.400-noble `
  dotnet test backend/tests/Dokpod.Bff.Tests/Dokpod.Bff.Tests.csproj `
  --configuration Release --verbosity minimal
```

O resultado esperado é a aprovação dos testes de composição do BFF. A validação não substitui o teste E2E com Keycloak real; esse teste permanece bloqueado pela ausência do BFF na stack E2E e da superfície HTTP da API.

## Comparação com AltivyNotes

| Capacidade | Dokpod | AltivyNotes |
| --- | --- | --- |
| Authorization Code + PKCE | implementado | implementado |
| Cookie server-side | implementado | implementado |
| Antiforgery e validação de Origin | implementado | implementado |
| Backchannel Keycloak sem redirects | implementado | implementado |
| Renovação coordenada de tokens | implementado | implementado |
| Rate limit de login | implementado | implementado |
| Relay REST | primeira fatia implementada | implementado |
| Relay SignalR/WebSocket | primeira fatia implementada; autorização horizontal e E2E pendentes | implementado |
| E2E do BFF na stack publicada | pendente | disponível na stack do Altivy |

A diferença de relay e E2E não deve ser tratada como falha de autenticação já corrigida; é uma dependência funcional do próximo slice da API e do deployment.

## Disponibilidade da sessão

O ticket store e o rate limit usam memória local nesta etapa. Isso é suficiente
para uma única instância de laboratório, mas não para escala horizontal: uma
réplica diferente não compartilha tickets, limite de login ou coordenação de
refresh. Antes de habilitar múltiplas réplicas, deve existir uma decisão de
arquitetura para armazenamento compartilhado de sessão/rate limit, preservando
Data Protection, expiração e revogação sem expor tokens.
