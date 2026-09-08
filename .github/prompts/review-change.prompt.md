---
name: "Revisar Mudança"
description: "Revisa diff, branch ou pull request do Dokpod com foco em bugs, segurança, contratos, agentes, engines, compatibilidade, operação e testes."
argument-hint: "Diff, branch ou PR, requisito e base de comparação"
agent: "Dokpod Code Reviewer"
---

Revise a mudança solicitada sem editar arquivos.

1. Leia requisito, diff completo, call sites, contratos e testes.
2. Confirme comportamento anterior e proposto.
3. Priorize bypass de autorização, exposição de socket/secret, operação destrutiva, replay, incompatibilidade N/N-1, diferenças entre engines e falha de reconciliação.
4. Execute validações estreitas somente quando ajudarem a confirmar um risco.
5. Ignore preferências estilísticas já cobertas por ferramentas.

Apresente achados primeiro por severidade, com localização, cenário, impacto, correção e teste. Depois liste dúvidas e lacunas. Se não houver achados, declare isso claramente.