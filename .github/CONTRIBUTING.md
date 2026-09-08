# Contribuindo com o Dokpod

## Antes de começar

1. Leia o [README](../README.md) e os documentos da área afetada.
2. Procure issue, ADR e plano relacionados.
3. Para mudança relevante, registre resultado observável, não escopo e riscos.
4. Nunca inclua tokens, chaves, certificados privados, dados reais de infraestrutura ou logs sensíveis.

## Licenciamento de contribuições

O código da Community Edition será publicado sob [`AGPL-3.0-only`](../LICENSE), conforme a [política de licenciamento](../docs/licenciamento.md). O Dokpod pretende manter a possibilidade de oferecer uma edição Business sob licença comercial.

Até que um Contributor License Agreement ou mecanismo equivalente seja aprovado juridicamente e publicado, contribuições externas de código não serão incorporadas. Issues, relatos de falha e discussões de projeto continuam bem-vindos, mas o envio de uma contribuição não implica sua aceitação nem concede automaticamente os direitos necessários ao duplo licenciamento.

## Fluxo

1. Crie uma branch curta a partir de `main`.
2. Implemente um slice pequeno no módulo proprietário.
3. Atualize testes, contratos, documentação e telemetria junto da mudança.
4. Execute os gates aplicáveis e registre os resultados no pull request.
5. Use os [Conventional Commits](COMMIT_CONVENTIONS.md).

## Scripts e automação

Automações globais de infraestrutura, administração, manutenção e validação ficam em `tools/scripts/`. Escolha PowerShell 7 para orquestração de CLIs e sistema; escolha Python para parsing, APIs ou lógica reutilizável e execute-o exclusivamente com `uv` pelo projeto compartilhado `tools/pyproject.toml`.

Scripts de build, entrypoint, health check, instalação ou runtime permanecem no módulo proprietário. Consulte [Scripts e automação](SCRIPTING.md) antes de criar ou mover um script.

## Comandos esperados

Os comandos definitivos serão fixados com o scaffolding. Backend e frontend não dependem de SDKs instalados no host: restore, build, testes e execução usam imagens de toolchain fixadas. A interface mínima esperada é:

```powershell
# Raiz do repositório
docker compose --profile tooling run --rm dotnet-tooling dotnet restore Dokpod.slnx --locked-mode
docker compose --profile tooling run --rm dotnet-tooling dotnet format Dokpod.slnx --verify-no-changes
docker compose --profile tooling run --rm dotnet-tooling dotnet build Dokpod.slnx --no-restore
docker compose --profile tooling run --rm dotnet-tooling dotnet test Dokpod.slnx --no-build

# frontend/web
docker compose --profile tooling run --rm frontend-tooling pnpm install --frozen-lockfile
docker compose --profile tooling run --rm frontend-tooling pnpm format:check
docker compose --profile tooling run --rm frontend-tooling pnpm lint
docker compose --profile tooling run --rm frontend-tooling pnpm typecheck
docker compose --profile tooling run --rm frontend-tooling pnpm test
docker compose --profile tooling run --rm frontend-tooling pnpm build
docker compose --profile tooling run --rm frontend-tooling pnpm e2e

# Agente Windows, publicação cruzada; validar o artefato em Windows Server sem runtime .NET
docker compose --profile tooling run --rm dotnet-tooling dotnet publish backend/apps/agent --runtime win-x64 --self-contained true
```

Não declare esses comandos como disponíveis antes de os manifests correspondentes existirem. Não substitua os containers de toolchain por SDKs globais no fluxo oficial.

## Definition of Done

- critérios de aceite demonstrados;
- código formatado, analisado e compilado;
- testes reais proporcionais ao engine, sistema operacional e risco;
- contratos e clientes gerados sincronizados;
- segurança, acessibilidade, concorrência e desempenho considerados;
- logs e métricas não expõem conteúdo sensível;
- rollout, rollback e compatibilidade documentados;
- nenhum achado crítico ou alto aberto sem aceitação formal.

## Pull requests

Mantenha PRs coesos e pequenos o bastante para revisão responsável. Mudanças incompatíveis exigem issue, ADR quando aplicável, estratégia de migração e período de compatibilidade. Não misture refactor amplo com mudança funcional.
