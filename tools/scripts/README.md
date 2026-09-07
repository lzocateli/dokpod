# Scripts do repositório

Este diretório concentra automações globais de infraestrutura, administração, manutenção e validação do Dokpod.

Antes de adicionar ou alterar um script, consulte [Scripts e automação](../../.github/SCRIPTING.md). Ferramentas Python usam o projeto compartilhado `tools/pyproject.toml`, criado junto da primeira ferramenta Python, e são executadas exclusivamente com `uv`.

## Governança do GitHub

Audite sem alterações:

```powershell
./tools/scripts/configure-github-governance.ps1
```

Reconcilie as configurações remotas de forma explícita:

```powershell
./tools/scripts/configure-github-governance.ps1 -Mode Apply
```

O script exige PowerShell 7 e GitHub CLI autenticado como administrador. Consulte a [governança do repositório](../../.github/GOVERNANCE.md) para o contrato aplicado.
