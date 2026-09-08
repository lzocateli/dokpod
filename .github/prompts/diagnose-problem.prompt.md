---
name: "Diagnosticar Problema"
description: "Reproduz, localiza a causa raiz e corrige um problema do Dokpod com teste de regressão e validação do menor recorte."
argument-hint: "Sintoma, ambiente, passos, resultado esperado e evidências"
agent: "agent"
---

Diagnostique e corrija o problema solicitado.

1. Preserve logs, mensagens, plataforma e passos fornecidos sem expor secrets.
2. Localize o código que controla o comportamento e formule uma hipótese falsificável.
3. Reproduza com o teste ou comando mais estreito disponível.
4. Faça a menor correção na causa raiz e valide imediatamente.
5. Adicione teste de regressão no nível adequado; use engine/PostgreSQL real quando a integração for determinante.
6. Verifique impactos em autorização, contrato, N/N-1, idempotência e reconciliação.
7. Execute a suíte relacionada e separe falhas preexistentes.

Não altere código fora do escopo nem faça commit, push ou deploy.