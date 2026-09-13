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
- Headers encaminhados só são aceitos de `Security:TrustedProxies` e `Security:TrustedNetworks`.
- Data Protection usa um key ring persistente. Fora de Development, `Security:DataProtectionCertificatePath` e `Security:DataProtectionCertificatePassword` são obrigatórios e protegem o key ring com certificado.
- O BFF não aceita URL de retorno absoluta, não expõe tokens ao browser e não registra secrets ou tokens.

## Rotas atuais

| Rota | Acesso | Finalidade |
| --- | --- | --- |
| `/health/live` | anônimo | liveness sem dependências externas |
| `/health/ready` | anônimo | readiness incluindo discovery do Keycloak |
| `/bff/login` | anônimo, limitado | inicia o login OIDC |
| `/bff/session` | anônimo | retorna o estado sanitizado da sessão |
| `/bff/antiforgery` | autenticado | emite token antiforgery |
| `/bff/logout` | autenticado + antiforgery | encerra sessão local e no Keycloak |

## Limite atual de integração

O BFF já possui `Downstream:Api:BaseUrl` e validação da URL, mas ainda não publica relay `/api/v1` nem `/hubs`. Isso é intencional nesta etapa: a API do Dokpod ainda expõe apenas health checks e o transporte gRPC do agente, sem uma superfície REST/SignalR pública para o BFF encaminhar.

A integração E2E deve ser habilitada somente quando existirem, em conjunto:

- endpoints HTTP versionados da API (`/api/v1`);
- relay server-side que copie apenas headers permitidos e acrescente o Bearer token da sessão;
- relay SignalR/WebSocket com validação de Origin e fechamento correto de conexão;
- testes de autorização horizontal, token expirado, resposta 401/403, timeout e indisponibilidade da API;
- stack Compose/NGINX com BFF como único upstream do browser.

Não se deve apontar o browser diretamente para a API para contornar essa pendência.

## Configuração mínima

Os valores sensíveis são injetados por secret provider ou arquivo montado fora do repositório. O `appsettings.json` contém somente defaults não secretos.

- `Authentication:Keycloak:Authority`: issuer do realm dedicado `dokpod`;
- `Authentication:Keycloak:ClientId`: client confidencial `dokpod-bff`;
- `Authentication:Keycloak:ClientSecret`: secret injetado em runtime;
- `Security:DataProtectionKeysPath`: diretório persistente das chaves;
- `Security:AllowedOrigins`: origens HTTPS exatas do browser;
- `Security:TrustedProxies` e `Security:TrustedNetworks`: proxies/rede autorizados;
- `Security:DataProtectionCertificatePath` e `Security:DataProtectionCertificatePassword`: certificado e senha do key ring, obrigatórios fora de Development;
- `Downstream:Api:BaseUrl`: reservado para o relay futuro e não usado enquanto a API HTTP não existir;
- `Bff:TokenRefreshTimeoutSeconds` e `Bff:TokenRefreshMaxResponseContentBufferSize`: limites do refresh;
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
| Relay REST/SignalR | pendente com a API HTTP | implementado |
| E2E do BFF na stack publicada | pendente | disponível na stack do Altivy |

A diferença de relay e E2E não deve ser tratada como falha de autenticação já corrigida; é uma dependência funcional do próximo slice da API e do deployment.

## Disponibilidade da sessão

O ticket store e o rate limit usam memória local nesta etapa. Isso é suficiente
para uma única instância de laboratório, mas não para escala horizontal: uma
réplica diferente não compartilha tickets, limite de login ou coordenação de
refresh. Antes de habilitar múltiplas réplicas, deve existir uma decisão de
arquitetura para armazenamento compartilhado de sessão/rate limit, preservando
Data Protection, expiração e revogação sem expor tokens.
