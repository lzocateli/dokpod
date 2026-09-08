---
name: "Planejar Feature"
description: "Transforma uma necessidade do Dokpod em plano verificável com arquitetura, contratos, segurança, slices, testes, rollout e rollback, sem implementar."
argument-hint: "Necessidade, critérios, restrições e plataformas"
agent: "Dokpod Delivery Lead"
---

Planeje a feature solicitada sem editar código.

1. Leia o plano mestre e os documentos do domínio.
2. Defina ator, problema, resultado, critérios de aceite e não escopo.
3. Identifique contratos, módulos, engines/plataformas e fronteiras de confiança afetados.
4. Consulte o Solution Architect se houver decisão estrutural ou ADR necessário.
5. Divida o trabalho em slices verticais ordenados, cada um com validação capaz de refutá-lo.
6. Inclua compatibilidade N/N-1, migração, rollout, rollback, observabilidade e segurança.
7. Crie ou atualize `docs/plan/<tema>.md` pelo template apenas se solicitado.

Não marque plano como `approved` nem etapas como `completed` sem decisão ou evidência humana explícita.