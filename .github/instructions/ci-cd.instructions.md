---
name: "CI e release"
description: "Use ao criar ou alterar GitHub Actions, Dependabot, build, release, SBOM, proveniência ou publicação de imagens."
applyTo: ".github/workflows/**, .github/dependabot.yml"
---

# CI e release

- Fixe actions por SHA e registre a versão humana em comentário.
- Use permissões mínimas por job e `permissions: {}` como padrão.
- Pull requests não recebem secrets de ambientes protegidos.
- Nenhum secret, token, senha, certificado privado ou connection string com credencial
	pode ser versionado em código, configuração, documentação, artefato ou workflow.
- Workflows devem consumir credenciais por secrets do GitHub, preferencialmente de um
	Environment protegido; nunca coloque valores reais, placeholders parecidos com
	credenciais ou defaults de autenticação em `env`, `with`, `run`, services ou scripts.
- O Environment `ci` usa `DOKPOD_CI_CONTROLPLANE_CONNECTION`,
	`DOKPOD_CI_TEST_POSTGRES_CONNECTION` e `DOKPOD_CI_POSTGRES_PASSWORD`. Em pull
	requests, use somente dados efêmeros não secretos, porque secrets protegidos não
	são disponibilizados a PRs.
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