---
name: "Continuar Plano"
description: "Executa o próximo slice aprovado e desbloqueado do plano do Dokpod dentro dos limites operacionais autorizados."
agent: "Dokpod Delivery Lead"
---

Continue o desenvolvimento do próximo plano aprovado e desbloqueado do Dokpod,
seguindo a documentação em `docs/` e o plano específico indicado pelo usuário.

## Autorização operacional

- Trabalhe somente no repositório Dokpod.
- Escolha o próximo slice aprovado e desbloqueado, sem alterar o escopo.
- Edite código, contratos, testes e documentação necessários ao slice.
- Execute as validações containerizadas e os testes de engine aplicáveis.
- Preserve alterações existentes e não altere outros workspaces.
- Solicite revisão de código e segurança proporcional ao risco.
- Corrija falhas pertencentes ao mesmo escopo antes de concluir.

## Limites obrigatórios

- Não faça commit, push, merge, publicação ou deploy sem solicitação explícita.
- Não altere configurações administrativas do GitHub.
- Não aceite riscos críticos ou altos.
- Não aprove ADRs, planos ou etapas em nome do revisor humano.
- Não acesse produção nem altere recursos externos reais.
- Pare diante de decisão arquitetural, jurídica, comercial ou de produto.

## Secrets

- Secrets locais existem somente em
	`$env:APPDATA\Microsoft\UserSecrets\dokpod`, fora do workspace.
- Nunca leia, liste, copie, edite, sobrescreva ou exiba esse arquivo.
- Nunca execute `Get-Content`, `type`, `cat` ou equivalente sobre ele.
- Use somente variáveis já injetadas por processo autorizado.
- Não coloque secrets em argumentos, logs, arquivos, commits ou mensagens.
- Se uma variável obrigatória não estiver disponível, falhe fechado e reporte
	apenas o nome da variável ausente.

Ao terminar, informe o plano e slice executados, critérios atendidos,
validações, arquivos alterados e bloqueios humanos restantes.
