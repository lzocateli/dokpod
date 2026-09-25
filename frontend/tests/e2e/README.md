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
- `DOKPOD_E2E_MUTATION_CONTAINER`: nome exato de um container sintético dedicado
  que a suíte pode iniciar, reiniciar, parar e excluir. Sem essa variável, o
  cenário destrutivo é ignorado.
- `DOKPOD_E2E_DENIED_ENVIRONMENT_ID`: UUID de um segundo ambiente existente,
  com resource UMA concedido exclusivamente a outro principal, usado para provar
  negação horizontal em estado, inventário e comandos.
- `DOKPOD_E2E_REVOKE_AGENT`: use `1` somente na execução isolada do cenário de
  revogação. A operação encerra a sessão ativa e exige reprovisionar a identidade
  sintética antes de reutilizar o laboratório.

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
  --env DOKPOD_E2E_MUTATION_CONTAINER `
  --env DOKPOD_E2E_DENIED_ENVIRONMENT_ID `
  lzocateli/playwright-e2e:0.1.0 `
  --base-url=https://localhost:7443/dokpod/ `
  /app/tests
```

O hostname `localhost` deve ser preservado porque ele faz parte dos callbacks
OIDC e da configuração pública do gateway. A rede host permite que o Chromium
containerizado use essa URL canônica sem substituir o header `Host`.

O valor de `DOKPOD_E2E_PASSWORD` deve ser digitado ou injetado pelo mecanismo de secrets do ambiente. Não coloque a senha em linha de comando versionada, arquivo `.env` do repositório ou relatório.

Execute `test_agent_identity_can_be_revoked` separadamente e somente no fim do
ciclo de validação. Após comprovar o bloqueio de reconexão, restaure a identidade
por um fluxo administrativo autorizado antes de iniciar novamente o agente.

## Cenários

- sessão autenticada disponível no BFF;
- catálogo de ambientes renderizado;
- ambiente autorizado consultável por API e UI;
- tela de containers exibindo ações autorizadas.

Os cenários que dependem de `DOKPOD_E2E_ENVIRONMENT_ID` são marcados como
`skipped` quando o UUID não é fornecido. O cenário de mutação também exige um
container descartável criado exclusivamente para o teste; nunca informe um
container de aplicação ou infraestrutura. Para o gate de release, UUID, usuário
e alvo sintético devem estar provisionados e o resultado não pode conter skips.

## Relatórios

Os artefatos ficam em `artifacts/e2e/playwright`, fora do pacote de produção. Preserve HTML, screenshots e traces conforme a política de evidências da release.

A carga e o teste de capacidade não pertencem a esta suíte. Use a imagem k6 em uma VM isolada, conforme os critérios de `.github/PERFORMANCE_TESTING_CRITERIA.md` e a documentação de `containers-lzo/k6`.
