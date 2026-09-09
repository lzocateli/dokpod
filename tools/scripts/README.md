# Scripts do repositório

Este diretório concentra automações globais de infraestrutura, administração, manutenção e validação do Dokpod.

Antes de adicionar ou alterar um script, consulte [Scripts e automação](../../.github/SCRIPTING.md). Ferramentas Python usam o projeto compartilhado `tools/pyproject.toml`, criado junto da primeira ferramenta Python, e são executadas exclusivamente com `uv`.

## Scripts disponíveis

- `configure-keycloak.ps1`: reconcilia o realm `dokpod`, clients, audience, roles, grupos e integrações opcionais na instância compartilhada do Keycloak.
