# Stack E2E do Dokpod

Este diretório define a stack local do projeto Dokpod, consumindo a stack
compartilhada `altivy-keycloak-lab` para NGINX, Keycloak e PostgreSQL. O Compose
cria somente os serviços próprios do Dokpod que já existem no repositório.

## Serviços

- `web`: aplicação Angular estática `dokpod-web:e2e`;
- `api`: API do plano de controle `dokpod-api:e2e`, com gRPC/mTLS na porta interna `7443` e health HTTP na porta interna `8080`;
- `agent`: agente Linux opcional no perfil `agent`, com acesso explícito ao socket Docker local.

O `nginx-proxy` compartilhado publica a Web do Dokpod pelo caminho
`https://localhost:7443/dokpod/`. Essa topologia não exige alteração de DNS,
arquivo `hosts` ou resolver local.

O BFF ainda não entra nesta stack porque o host `Dokpod.Bff` não possui código
fonte versionado nesta etapa. Quando o BFF for criado, ele deve ser o único
serviço chamado pela Web e deve entrar na rede `altivy-edge` com alias
`dokpod-bff`.

## Pré-requisitos

- stack compartilhada `altivy-keycloak-lab` saudável, incluindo `altivy-nginx-proxy`, `altivy-keycloak` e `altivy-postresql`;
- rede externa `altivy-edge` criada;
- certificado PFX de laboratório para a API;
- Docker Desktop com containers Linux.

## Variáveis externas

Defina no arquivo externo `$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env` ou na
sessão atual:

- `DOKPOD_E2E_API_CERTIFICATE_PATH`, obrigatório, caminho absoluto para o PFX da API;
- `DOKPOD_E2E_API_CERTIFICATE_PASSWORD`, opcional quando o PFX não tiver senha;
- `DOKPOD_E2E_AGENT_CERTIFICATE_FINGERPRINT`, opcional para o laboratório de agentes;
- `DOKPOD_E2E_AGENT_ENVIRONMENT_ID`, opcional, GUID do ambiente associado ao certificado do agente.

Nunca versione certificados privados, senhas ou `.env` dentro do repositório.

## Criar certificado da API

A API do Dokpod exige um certificado PFX para o endpoint gRPC/mTLS. Para
laboratório local, gere o PFX fora do repositório em UserSecrets. O exemplo
abaixo reutiliza o certificado TLS criado para o `nginx-proxy` compartilhado do
AltivyNotes:

```powershell
$AltivyCertsDirectory = Join-Path $env:APPDATA 'Microsoft\UserSecrets\Altivy.Notes\certs'
$DokpodSecretsDirectory = Join-Path $env:APPDATA 'Microsoft\UserSecrets\Dokpod'
$DokpodCertsDirectory = Join-Path $DokpodSecretsDirectory 'certs'
New-Item -ItemType Directory -Force $DokpodCertsDirectory | Out-Null

docker run --rm `
  -v "${AltivyCertsDirectory}:/input:ro" `
  -v "${DokpodCertsDirectory}:/output" `
  lzocateli/nginx:1.28.0-bookworm `
  openssl pkcs12 -export `
    -in /input/tls.crt `
    -inkey /input/tls.key `
    -out /output/api.pfx `
    -passout pass:

$env:DOKPOD_E2E_API_CERTIFICATE_PATH = Join-Path $DokpodCertsDirectory 'api.pfx'
$env:DOKPOD_E2E_API_CERTIFICATE_PASSWORD = ''
```

Para um certificado de API dedicado, gere um par próprio com a autoridade local
aprovada e exporte o PFX para o mesmo diretório externo.

## Validar e iniciar

Execute a partir da raiz do repositório:

```powershell
docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env" `
  -f deploy/e2e/docker-compose-dokpod.yaml `
  config --quiet

docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env" `
  -f deploy/e2e/docker-compose-dokpod.yaml `
  up --build --wait
```

Para incluir o agente Docker local:

```powershell
docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Dokpod\.env" `
  -f deploy/e2e/docker-compose-dokpod.yaml `
  --profile agent `
  up --build --wait
```

O mount `/var/run/docker.sock:/run/docker.sock` concede privilégio elevado sobre
o host Docker local. Use o perfil `agent` somente em laboratório controlado.

## Encerrar

```powershell
docker compose -f deploy/e2e/docker-compose-dokpod.yaml down
```