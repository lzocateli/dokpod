# Scripts e automação

Este documento define a localização e o contrato das automações de infraestrutura, administração, manutenção e validação do Dokpod.

## Estrutura

```text
tools/
├── pyproject.toml          # projeto Python/uv único, criado com a primeira ferramenta Python
├── uv.lock                 # lockfile compartilhado e versionado quando houver dependências
└── scripts/
    ├── validar.ps1         # automação PowerShell global
    └── inventario/
        └── main.py         # ferramenta Python executada por uv

deploy/, backend/ ou frontend/
└── ...                     # scripts de build, entrypoint, health check ou runtime do módulo
```

Toda automação global de infraestrutura, administração, manutenção ou validação pertence a `tools/scripts/`. Não crie scripts globais na raiz, em `.github/scripts/`, `deploy/scripts/` ou dentro de aplicações e bibliotecas.

Scripts consumidos diretamente por Dockerfiles, imagens, instaladores, serviços ou runtime permanecem junto do módulo proprietário. Essa exceção preserva o contrato e o contexto operacional do artefato.

## Escolha da linguagem

Use PowerShell 7 quando a tarefa consistir principalmente em orquestrar CLIs, containers, filesystem ou comandos do sistema e precisar funcionar em Windows e Linux.

Use Python quando houver parsing estruturado, transformação de dados, chamadas a APIs, lógica reutilizável ou necessidade de testes unitários. Ferramentas Python compartilham um único `tools/pyproject.toml`, são executadas exclusivamente com `uv` e mantêm `tools/uv.lock` versionado.

Bash fica restrito a entrypoints e scripts de runtime que dependam de shell POSIX dentro de imagens Linux.

## Python com uv

Crie `tools/pyproject.toml` junto da primeira ferramenta Python. Não adicione um projeto Python vazio, `requirements.txt`, ambientes virtuais manuais ou um `pyproject.toml` por script.

```powershell
uv add --project tools pacote
uv sync --project tools --locked
uv run --project tools python tools/scripts/inventario/main.py --help
```

Dependências e versões mínimas ficam no `pyproject.toml`; o lockfile é atualizado pelo `uv`, nunca manualmente.

## Ajuda obrigatória

Todo script criado ou refatorado aceita `--help` e apresenta, sem executar a operação principal:

- finalidade e escopo;
- pré-requisitos e versões mínimas;
- sintaxe, parâmetros e valores padrão;
- pelo menos um exemplo executável;
- caminho para documentação mais ampla, quando existir.

Erros de uso vão para stderr, retornam código diferente de zero e orientam a consultar `--help`. Scripts são não interativos por padrão, e operações destrutivas exigem opção explícita.

## Validação

Uma alteração de script somente está completa quando:

- a ajuda corresponde à implementação e retorna código `0`;
- ao menos um fluxo funcional representativo foi executado;
- formatador, linter e testes aplicáveis passaram;
- todos os consumidores afetados foram atualizados;
- nenhuma credencial, secret, conteúdo de `.env` ou dado real de infraestrutura aparece em código, exemplos ou logs.

As regras detalhadas para agentes estão em [`instructions/script-authoring.instructions.md`](instructions/script-authoring.instructions.md).
