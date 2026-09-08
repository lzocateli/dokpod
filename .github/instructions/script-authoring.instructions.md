---
name: "Criação e manutenção de scripts"
description: "Use ao criar, mover ou refatorar scripts PowerShell, Bash ou Python, automações de infraestrutura, comandos uv, pyproject.toml ou ferramentas em tools do Dokpod."
applyTo: "**/*.ps1, **/*.sh, **/*.bash, **/*.py, **/pyproject.toml, **/uv.lock, tools/**"
---

# Criação e manutenção de scripts

## Localização e propriedade

- Coloque toda automação global de infraestrutura, administração, manutenção e validação do repositório em `tools/scripts/`.
- Use PowerShell (`.ps1`) para orquestração de CLIs, containers, filesystem e tarefas que precisam operar de forma consistente em Windows e Linux com PowerShell 7.
- Use Python quando a automação exigir parsing estruturado, transformação de dados, integração com APIs, lógica reutilizável ou testes unitários.
- Coloque cada ferramenta Python em `tools/scripts/<nome>/`, com diretório em kebab-case e módulos em snake_case.
- Use somente `tools/pyproject.toml` para dependências, comandos e configuração das ferramentas Python. Crie-o junto da primeira ferramenta Python e mantenha `tools/uv.lock` versionado.
- Execute Python e ferramentas exclusivamente por `uv`, por exemplo `uv run --project tools python tools/scripts/<nome>/main.py` ou por um comando declarado em `[project.scripts]`.
- Não crie `requirements.txt`, ambiente virtual manual ou um `pyproject.toml` por script.
- Não coloque automação global nova na raiz, em `.github/scripts/`, `deploy/scripts/` ou dentro de aplicações e bibliotecas.
- Scripts que são entrypoint, health check, instalação, build ou runtime de uma aplicação, imagem ou serviço permanecem no módulo proprietário, pois fazem parte do contrato desse artefato.
- Bash novo é reservado a scripts de runtime que exijam um shell POSIX dentro de imagem Linux; não o use como padrão para automação global.

## Contrato de linha de comando

- Todo script criado ou refatorado aceita `--help`, encerra com código `0` e não produz efeitos colaterais ao exibir ajuda.
- A ajuda informa finalidade, pré-requisitos, sintaxe, parâmetros e padrões, pelo menos um exemplo executável e documentação relacionada quando existir.
- Erros de uso vão para stderr, retornam código diferente de zero e orientam a consultar `--help`.
- Scripts são não interativos por padrão. Operações destrutivas exigem opção explícita e não inferem confirmação em CI.
- Nunca inclua tokens, certificados privados, conteúdo de `.env`, dados reais de infraestrutura ou logs sensíveis em argumentos, exemplos ou saída.

## PowerShell

- Exija PowerShell 7 ou superior e use `[CmdletBinding()]` com `param(...)`.
- Forneça comment-based help com `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`, `.NOTES` e `.LINK` quando houver documentação ampla.
- Além de `-?`, aceite literalmente `--help` quando o script for chamado por pessoas, workflows ou outras ferramentas.
- Trate códigos de saída de executáveis nativos explicitamente e preserve mensagens acionáveis em stderr.
- Valide com `Get-Help ./tools/scripts/<script>.ps1 -Full` e `./tools/scripts/<script>.ps1 --help`.

## Python e uv

- Use `argparse` ou dependência já declarada em `tools/pyproject.toml`; o caminho `--help` não depende de imports opcionais.
- Declare a versão mínima do Python, dependências, comandos, formatador, linter e testes em `tools/pyproject.toml`.
- Altere dependências com `uv add --project tools <pacote>` e `uv remove --project tools <pacote>`; não edite `tools/uv.lock` manualmente.
- Execute comandos com `uv run --project tools <comando>` e sincronize ambientes com `uv sync --project tools --locked` quando houver lockfile.
- Valide ao menos `uv run --project tools python tools/scripts/<nome>/main.py --help`, lint e testes da ferramenta.

## Validação da alteração

- Execute o caminho `--help` e ao menos um fluxo funcional representativo.
- Atualize workflows, Dockerfiles, Compose, documentação e demais consumidores quando caminho ou interface mudar.
- Preserve execução reproduzível e fixe versões de imagens e dependências conforme as regras do projeto.
- Consulte `.github/SCRIPTING.md` para a estrutura, critérios de escolha e comandos canônicos.