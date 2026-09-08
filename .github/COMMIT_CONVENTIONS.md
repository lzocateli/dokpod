# Convenção de commits

Use [Conventional Commits 1.0.0](https://www.conventionalcommits.org/pt-br/v1.0.0/).

```text
<tipo>[escopo opcional][!]: <descrição>
```

A descrição é em português brasileiro, no imperativo, começa com minúscula e não termina com ponto.

## Tipos

| Tipo | Uso |
| --- | --- |
| `feat` | comportamento novo ou ampliado |
| `fix` | correção de defeito ou vulnerabilidade |
| `docs` | somente documentação |
| `style` | somente formatação |
| `refactor` | reorganização sem alteração funcional |
| `perf` | melhoria de desempenho |
| `test` | testes sem alteração de produção |
| `build` | build, dependências e imagens |
| `ci` | workflows e automação |
| `chore` | manutenção restante |
| `revert` | reversão formal |

## Escopos

`web`, `bff`, `api`, `agent`, `keycloak`, `backend`, `contracts`, `db`, `deploy`, `security`, `observability`, `docs`, `tooling`, `deps`, `actions` e `repo`.

Exemplos:

```text
feat(agent): anuncia capacidades do engine
fix(api): impede repetição de comando expirado
docs(architecture): registra limites do protocolo
```

Mudança incompatível usa `!` e rodapé com impacto e migração:

```text
feat(contracts)!: exige revisão monotônica no inventário

BREAKING CHANGE: agentes antigos devem atualizar antes do servidor.
```