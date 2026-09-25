# Suíte E2E autenticada do Dokpod

Esta suíte valida a jornada autenticada do Dokpod através do BFF, Keycloak e API. As credenciais são fornecidas somente por variáveis de ambiente em runtime; nenhum usuário, senha, token ou certificado deve ser versionado.

## Pré-requisitos

- stack E2E do Dokpod saudável;
- Keycloak provisionado com um usuário de teste sintético;
- permissão de leitura no ambiente usado pelo teste;
- imagem `lzocateli/playwright-e2e:0.1.0`.

Variáveis:

- `DOKPOD_E2E_BASE_URL`: URL pública, padrão `https://localhost:7443/dokpod/`;
- `DOKPOD_E2E_USERNAME`: usuário sintético do Keycloak;
- `DOKPOD_E2E_PASSWORD`: senha injetada somente em runtime;
- `DOKPOD_E2E_ENVIRONMENT_ID`: UUID opcional para validar entrada, inventário e ações.

## Execução containerizada

Na raiz do repositório, com a stack já iniciada:

```powershell
$reports = Join-Path $PWD 'artifacts\e2e\playwright'
New-Item -ItemType Directory -Force $reports | Out-Null

docker run --rm `
  --network host `
  --volume "${PWD}\frontend\tests\e2e:/app/tests:ro" `
  --volume "${reports}:/app/reports" `
  --env DOKPOD_E2E_BASE_URL=https://localhost:7443/dokpod/ `
  --env DOKPOD_E2E_USERNAME `
  --env DOKPOD_E2E_PASSWORD `
  --env DOKPOD_E2E_ENVIRONMENT_ID `
  lzocateli/playwright-e2e:0.1.0 `
  --base-url=https://localhost:7443/dokpod/ `
  /app/tests
```

O hostname `localhost` deve ser preservado porque ele faz parte dos callbacks
OIDC e da configuração pública do gateway. A rede host permite que o Chromium
containerizado use essa URL canônica sem substituir o header `Host`.

O valor de `DOKPOD_E2E_PASSWORD` deve ser digitado ou injetado pelo mecanismo de secrets do ambiente. Não coloque a senha em linha de comando versionada, arquivo `.env` do repositório ou relatório.

## Cenários

- sessão autenticada disponível no BFF;
- catálogo de ambientes renderizado;
- ambiente autorizado consultável por API e UI;
- tela de containers exibindo ações autorizadas.

Os cenários que dependem de `DOKPOD_E2E_ENVIRONMENT_ID` são marcados como `skipped` quando o UUID não é fornecido. Para o gate de release, o UUID e o usuário devem ser provisionados no ambiente de homologação e o resultado não pode conter skips.

## Relatórios

Os artefatos ficam em `artifacts/e2e/playwright`, fora do pacote de produção. Preserve HTML, screenshots e traces conforme a política de evidências da release.

A carga e o teste de capacidade não pertencem a esta suíte. Use a imagem k6 em uma VM isolada, conforme os critérios de `.github/PERFORMANCE_TESTING_CRITERIA.md` e a documentação de `containers-lzo/k6`.
