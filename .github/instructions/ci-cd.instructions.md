---
name: "CI e release"
description: "Use ao criar ou alterar GitHub Actions, Dependabot, build, release, SBOM, proveniência ou publicação de imagens."
applyTo: ".github/workflows/**, .github/dependabot.yml"
---

# CI e release

- Fixe actions por SHA e registre a versão humana em comentário.
- Use permissões mínimas por job e `permissions: {}` como padrão.
- Pull requests não recebem secrets de ambientes protegidos.
- Separe restore, format/lint, build, unit, integração, contrato, E2E e imagens.
- Builds e testes oficiais do backend/frontend usam as mesmas toolchains containerizadas do desenvolvimento.
- Cacheie somente conteúdo reprodutível e use lockfiles em modo imutável.
- Construa imagens para API, BFF, web e agente Linux; publique o agente Windows self-contained por RID homologado, sem Dockerfile Windows.
- Execute a matriz Windows em host sem runtime .NET e valide instalação, serviço, named pipe, atualização e rollback.
- Gere SBOM e proveniência; faça scan de dependências, secrets e imagens.
- Assine imagens por digest e pacotes Windows por versão/hash, sem sobrescrever artefato imutável.
- Deploy exige aprovação humana, ambiente protegido e rollback documentado.
- Não adicione workflow antes de existir comando local equivalente e validado.
- Falha de segurança crítica ou alta bloqueia release.