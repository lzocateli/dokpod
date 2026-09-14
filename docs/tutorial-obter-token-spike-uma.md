# Tutorial: obter `DOKPOD_SPIKE_USER_ACCESS_TOKEN`

## Objetivo

Este tutorial mostra como obter um access token temporário de usuário para executar:

```powershell
./tools/scripts/test-keycloak-authorization-spike.ps1 `
  -UserAccessToken $token `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -Scope environment:read `
  -Expected allowed
```

O token é usado somente para testar a decisão UMA do Keycloak. Ele não é client secret, não é senha administrativa e não deve ser usado para configurar o BFF.

## O que os prints confirmam

No Admin Console mostrado:

- o realm atual é `dokpod`;
- existem os clients `dokpod-api`, `dokpod-authorization-spike`, `dokpod-bff`, `dokpod-lab` e `dokpod-provisioner`;
- existem os grupos `dokpod/administrators`, `dokpod/auditors`, `dokpod/operators` e `dokpod/readers`.

Para obter um token de usuário, use o client público `dokpod-lab` ou `dokpod-authorization-spike`. Não use `admin-cli`, `dokpod-bff` ou `dokpod-provisioner`.

## Pré-requisitos no Keycloak

No realm `dokpod`, confirme no client de laboratório:

- **Client authentication:** desabilitado;
- **Standard flow:** habilitado;
- **Direct access grants:** desabilitado, salvo decisão explícita para laboratório;
- **PKCE:** `S256`;
- **Valid redirect URI:** exatamente `http://127.0.0.1:8765/callback/`;
- **Web origins:** somente a origem necessária ao laboratório;
- **Audience:** o access token precisa conter `dokpod-api` quando for avaliado pela API.

O usuário de teste também precisa:

- existir no realm `dokpod`;
- estar habilitado;
- ter credencial configurada;
- pertencer ao grupo/policy que concede acesso ao recurso UMA;
- possuir permissão para o recurso `urn:dokpod:environment:{EnvironmentId}` e o scope escolhido, por exemplo `environment:read`.

A existência do grupo `readers` sozinha não prova que a permissão UMA foi criada. A policy e a permission do client `dokpod-api` também precisam apontar para o recurso e scope corretos.

## Fluxo recomendado: Authorization Code + PKCE

O token deve ser obtido por login interativo. Não copie senha administrativa, client secret ou token de outro produto.

### 1. Gerar o desafio PKCE

No PowerShell 7, em um terminal temporário:

```powershell
$bytes = [byte[]]::new(64)
[Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$verifier = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))
$challenge = [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$stateBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($stateBytes)
$state = [Convert]::ToBase64String($stateBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
```

Mantenha `$verifier` e `$state` somente nesse processo. Não os grave em arquivo.

### 2. Abrir o login do realm

Substitua apenas o `client_id` se o client escolhido tiver outro nome. A URL abaixo não contém senha nem secret:

```powershell
$authority = 'https://localhost:7443/realms/dokpod'
$redirectUri = 'http://127.0.0.1:8765/callback/'
$query = [System.Web.HttpUtility]::ParseQueryString('')
$query['client_id'] = 'dokpod-lab'
$query['redirect_uri'] = $redirectUri
$query['response_type'] = 'code'
$query['scope'] = 'openid profile email'
$query['code_challenge'] = $challenge
$query['code_challenge_method'] = 'S256'
$query['state'] = $state
$authorizationUri = "$authority/protocol/openid-connect/auth?$($query.ToString())"
```

Faça login com o usuário de teste do realm `dokpod`. Depois do login, o Keycloak redirecionará para algo semelhante a:

```text
http://127.0.0.1:8765/callback/?code=...&state=...
```

O `code` é de uso único e expira rapidamente. Não publique a URL completa.

### 3. Capturar o callback sem persistir o code

Agora inicie o listener temporário e abra o login no browser a partir do mesmo bloco:

```powershell
$listener = [Net.HttpListener]::new()
$listener.Prefixes.Add($redirectUri)
$listener.Start()
Start-Process $authorizationUri
$context = $listener.GetContext()
$callback = $context.Request.Url
$listener.Stop()

if ($callback.Query -notmatch '(^|&)state=([^&]+)') {
    throw 'Callback sem state.'
}
$receivedState = [System.Web.HttpUtility]::ParseQueryString($callback.Query)['state']
if ($receivedState -ne $state) {
    throw 'State inválido.'
}
$code = [System.Web.HttpUtility]::ParseQueryString($callback.Query)['code']
if ([string]::IsNullOrWhiteSpace($code)) {
    throw 'Callback sem authorization code.'
}
```

O listener deve estar ativo antes de executar o login. Se o browser mostrar erro de conexão, inicie o listener e repita o fluxo com um novo code.

### 4. Trocar o code por access token

```powershell
$tokenResponse = Invoke-RestMethod `
  -Method Post `
  -Uri "$authority/protocol/openid-connect/token" `
  -ContentType 'application/x-www-form-urlencoded' `
  -Body @{
    grant_type = 'authorization_code'
    client_id = 'dokpod-lab'
    code = $code
    redirect_uri = $redirectUri
    code_verifier = $verifier
  }

$accessToken = $tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw 'Keycloak não retornou access_token.'
}
```

Não use `Write-Host $accessToken`, `Write-Output $accessToken`, `Out-File`, clipboard ou log.

### 5. Executar o spike sem persistir o token

A forma preferida é passar o token como `SecureString` ao script:

```powershell
$secureToken = ConvertTo-SecureString $accessToken -AsPlainText -Force
./tools/scripts/test-keycloak-authorization-spike.ps1 `
  -UserAccessToken $secureToken `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -Scope environment:read `
  -Expected allowed
```

Como alternativa, somente no processo atual e por tempo curto:

```powershell
$env:DOKPOD_SPIKE_USER_ACCESS_TOKEN = $accessToken
./tools/scripts/test-keycloak-authorization-spike.ps1 `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -Scope environment:read `
  -Expected allowed
Remove-Item Env:DOKPOD_SPIKE_USER_ACCESS_TOKEN
```

A variável de ambiente não deve ser adicionada ao perfil do PowerShell, `.env`, Compose, Git ou configuração permanente.

## Testar negação

Use um usuário sem a policy do ambiente ou remova temporariamente a permissão UMA no client `dokpod-api`. Execute:

```powershell
$secureToken = ConvertTo-SecureString $accessToken -AsPlainText -Force
./tools/scripts/test-keycloak-authorization-spike.ps1 `
  -UserAccessToken $secureToken `
  -EnvironmentId 00000000-0000-0000-0000-000000000001 `
  -Scope environment:read `
  -Expected denied
```

Restaure a policy depois do teste. Não altere o realm `master`.

## Diagnóstico

| Sintoma | Causa provável |
| --- | --- |
| `invalid_redirect_uri` | redirect URI não está exatamente cadastrada no client |
| `unauthorized_client` | Standard Flow/PKCE/client público configurado incorretamente |
| `invalid_grant` | code expirado, reutilizado ou `code_verifier` diferente |
| resultado `denied` | usuário, grupo, policy, permission, recurso ou scope não correspondem |
| resultado `indeterminate` | Keycloak indisponível, timeout ou JSON inválido |
| audience ausente | mapper/scope de audience `dokpod-api` não aplicado ao client |
| `401` na API | token expirado, issuer incorreto ou assinatura inválida |

## Higiene após o teste

- revogue a sessão do usuário de laboratório no Keycloak quando necessário;
- remova a variável `DOKPOD_SPIKE_USER_ACCESS_TOKEN` se ela tiver sido usada;
- não salve o token no histórico, arquivos, clipboard ou logs;
- não envie o token em issue, chat, commit ou captura de tela;
- tokens expiram, mas trate qualquer token exposto como comprometido e revogue a sessão.
