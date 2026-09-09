# Laboratório Keycloak do Dokpod

Este diretório contém a configuração local do realm `dokpod` para a instância
administrativa compartilhada de NGINX/Keycloak/PostgreSQL da empresa Altivy. A
stack usa o mesmo nome do laboratório genérico exibido no Docker Desktop:
`altivy-keycloak-lab`, com os containers `altivy-nginx-proxy`,
`altivy-postresql` e `altivy-keycloak`.
Por padrão, o Compose reutiliza `%APPDATA%\keycloak\postgres-data` para os dados
do PostgreSQL e `%APPDATA%\keycloak\backup` para exports locais. Defina
`ALTIVY_KEYCLOAK_DEPLOY_ROOT` somente se a cópia local do AltivyNotes estiver em
outro caminho e for necessário montar o tema ou o realm do Altivy.

O objetivo deste diretório é versionar o que pertence ao Dokpod: realm, clients,
grupos, roles e tema de login `dokpod`. Usuários, senhas, secrets de clients,
exports e dados do PostgreSQL não são versionados.

Não use este Compose em produção. Ele executa o Keycloak em `start-dev`, usa
HTTP local e lê credenciais exclusivamente de arquivo externo ou variáveis de
ambiente injetadas pelo processo.

## Imagens

- `lzocateli/keycloak:26.7.0`;
- `lzocateli/postgresql:18.4-pgvector0.8.6-bookworm`.

## Pré-requisitos

- PowerShell 7;
- Docker Desktop com containers Linux;
- portas locais `8080` e `9000` livres;
- porta local `7443` livre para o `nginx-proxy` compartilhado;
- rede Docker externa `altivy-edge` criada;
- diretórios `$env:APPDATA\keycloak\postgres-data` e `$env:APPDATA\keycloak\backup` criados;
- tema Altivy já gerado em `AltivyNotes/deploy/keycloak/.build/keycloak-theme`;
- certificado e chave TLS locais informados em `ALTIVY_E2E_TLS_CERTIFICATE_PATH` e `ALTIVY_E2E_TLS_KEY_PATH`;
- pelo menos 2 GiB de memória disponível.

Execute os comandos a partir de `deploy/keycloak`:

```powershell
Set-Location ./deploy/keycloak
```

## Secrets externos

Use o arquivo externo do Dokpod para valores específicos do produto:

```powershell
$SecretsDirectory = Join-Path $env:APPDATA 'Microsoft\UserSecrets\Dokpod'
New-Item -ItemType Directory -Force $SecretsDirectory | Out-Null
notepad (Join-Path $SecretsDirectory '.env')
```

Variáveis do Dokpod reconhecidas:

- `DOKPOD_KEYCLOAK_ADMIN_USERNAME`, opcional e normalmente vazio na stack compartilhada; quando preenchido, sobrescreve o usuário administrativo usado pelo script de reconciliação;
- `DOKPOD_KEYCLOAK_ADMIN_PASSWORD`, opcional e normalmente vazio na stack compartilhada; quando preenchido, sobrescreve a senha administrativa usada pelo script de reconciliação;
- `DOKPOD_BFF_CLIENT_SECRET`, opcional para fixar o secret do client confidencial;
- `DOKPOD_PROVISIONER_CLIENT_SECRET`, opcional para fixar o secret do client de provisionamento;
- `DOKPOD_ADMIN_EMAIL`, opcional para criar o usuário inicial `dokpod-admin`;
- `DOKPOD_ADMIN_TEMPORARY_PASSWORD`, opcional e com mínimo de 14 caracteres;
- `DOKPOD_SMTP_*` e `DOKPOD_IDP_*`, opcionais para SMTP e provedores sociais.

Não defina valores em `DOKPOD_KEYCLOAK_ADMIN_USERNAME` e
`DOKPOD_KEYCLOAK_ADMIN_PASSWORD` quando a intenção for usar a conta
administrativa compartilhada do AltivyNotes. Deixe essas variáveis vazias ou
ausentes para o script usar `ALTIVY_KEYCLOAK_ADMIN_USERNAME` e
`ALTIVY_KEYCLOAK_ADMIN_PASSWORD`.

Use o arquivo externo do AltivyNotes para valores administrativos da stack
compartilhada de Keycloak/PostgreSQL. Esses nomes permanecem com prefixo
`ALTIVY_` porque pertencem à instância comum, não ao realm do Dokpod:

```powershell
$SharedSecretsDirectory = Join-Path $env:APPDATA 'Microsoft\UserSecrets\Altivy.Notes'
notepad (Join-Path $SharedSecretsDirectory '.env')
```

Variáveis compartilhadas reconhecidas:

- `ALTIVY_LAB_POSTGRES_PASSWORD`, obrigatória, senha do usuário `keycloak` no PostgreSQL compartilhado;
- `ALTIVY_KEYCLOAK_ADMIN_USERNAME`, opcional, usuário administrativo de bootstrap; padrão `admin`;
- `ALTIVY_KEYCLOAK_ADMIN_PASSWORD`, obrigatória, senha administrativa de bootstrap da instância compartilhada;
- `ALTIVY_KEYCLOAK_BASE_URL`, recomendada para scripts e clientes administrativos locais; padrão esperado `http://127.0.0.1:8080`;
- `ALTIVY_KEYCLOAK_MANAGEMENT_URL`, recomendada para health e métricas locais; padrão esperado `http://127.0.0.1:9000`;
- `ALTIVY_KEYCLOAK_ISSUER_URL`, recomendada para formar o issuer interno sem a borda TLS; padrão esperado `http://localhost:8080`;
- `ALTIVY_KEYCLOAK_HOSTNAME`, opcional, hostname público anunciado pelo Keycloak;
- `ALTIVY_KEYCLOAK_ADMIN_HOSTNAME`, opcional, hostname público do Admin Console;
- `ALTIVY_KEYCLOAK_REALM`, obrigatória para consumidores do produto; use `dokpod` no arquivo externo do Dokpod e `altivy` no arquivo externo do AltivyNotes;
- `ALTIVY_KEYCLOAK_DEPLOY_ROOT`, opcional quando o repositório AltivyNotes não estiver ao lado de `dokpod`.

No arquivo externo do Dokpod, mantenha apenas as variáveis `ALTIVY_*` necessárias
para consumidores do Dokpod, como `ALTIVY_KEYCLOAK_REALM=dokpod`. O Compose usa
somente o env-file do AltivyNotes para evitar divergência de senha entre o
PostgreSQL já inicializado e o Keycloak; o script de reconciliação carrega os
dois arquivos e aplica o realm informado por parâmetro, cujo padrão é `dokpod`.
Quando `ALTIVY_KEYCLOAK_REALM=dokpod` existir no arquivo externo do Dokpod, o
script aceita esse valor; se encontrar `altivy`, ele ignora para não reconciliar
o realm errado.

Os dados e backups locais da stack compartilhada ficam fora dos repositórios:

```powershell
New-Item -ItemType Directory -Force "$env:APPDATA\keycloak\postgres-data" | Out-Null
New-Item -ItemType Directory -Force "$env:APPDATA\keycloak\backup" | Out-Null
```

Não coloque `.env`, exports de realm com usuários, dumps ou backups dentro do
repositório.

## Gerar o tema

```powershell
./build-theme.ps1
```

Resultado esperado:

```text
Artefato do tema criado em .../deploy/keycloak/.build/keycloak-theme
Arquivos: 5
```

Para confirmar que o asset canônico do Dokpod existe:

```powershell
./build-theme.ps1 -BrandRoot ../../docs/assets/brand
```

## Criar a rede compartilhada

```powershell
if (-not (docker network ls --filter name=^altivy-edge$ --format '{{.Name}}')) {
  docker network create altivy-edge
}
```

## Iniciar Keycloak/PostgreSQL compartilhados

O Compose abaixo sobe a stack `altivy-keycloak-lab` com `nginx-proxy`,
PostgreSQL, Keycloak, o tema e o import do realm `dokpod`. Em laboratório limpo,
o import cria o realm e seus clients no primeiro boot. Em laboratório já
existente, o import não sobrescreve realms; use o script de reconciliação na
etapa seguinte.

```powershell
docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Altivy.Notes\.env" `
  -f ./docker-compose-keycloak.yaml `
  config --quiet
docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Altivy.Notes\.env" `
  -f ./docker-compose-keycloak.yaml `
  up -d --wait
```

Se a instância compartilhada já estiver em execução a partir de outro diretório,
não crie outro PostgreSQL. Gere o tema neste repositório, reinicie a instância
compartilhada com o mount do tema `dokpod` e aplique a reconciliação por Admin
REST no `BaseUrl` existente.

## Reconciliar o realm Dokpod

Execute a partir da raiz do repositório:

```powershell
./tools/scripts/configure-keycloak.ps1 -DryRun
./tools/scripts/configure-keycloak.ps1 -SkipUser
```

O script cria ou atualiza:

- realm `dokpod` com login theme `dokpod`;
- clients `dokpod-api`, `dokpod-provisioner`, `dokpod-bff` e `dokpod-authorization-spike`;
- mapper de audience `dokpod-api` para os clients que chamam a API;
- roles `Administrator`, `Operator`, `Auditor` e `Reader`;
- grupos `/dokpod/administrators`, `/dokpod/operators`, `/dokpod/auditors` e `/dokpod/readers`;
- SMTP e provedores sociais quando as variáveis correspondentes existirem.

## Links locais

- Login customizado do realm `dokpod`:
  <http://localhost:8080/realms/dokpod/protocol/openid-connect/auth?client_id=dokpod-lab&redirect_uri=http%3A%2F%2Flocalhost%3A8080%2F&response_type=code&scope=openid>
- Discovery OIDC do realm:
  <http://localhost:8080/realms/dokpod/.well-known/openid-configuration>
- Health/readiness:
  <http://localhost:9000/health/ready>
- Console administrativo:
  <http://localhost:8080/admin/>

O console administrativo autentica no realm `master` e pode permanecer com o
tema padrão. Para validar a identidade visual do Dokpod, use o link de login do
realm `dokpod`.

## Parar o laboratório

```powershell
docker compose `
  --env-file "$env:APPDATA\Microsoft\UserSecrets\Altivy.Notes\.env" `
  -f ./docker-compose-keycloak.yaml `
  down
```

Esse comando preserva os dados do PostgreSQL em
`$env:APPDATA\keycloak\postgres-data`. Exports e backups locais devem ficar em
`$env:APPDATA\keycloak\backup`.