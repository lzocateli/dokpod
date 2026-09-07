---
name: "Gerar Testes"
description: "Cria testes do Dokpod orientados a risco para comportamento, diff ou contrato, incluindo engines reais, transporte, segurança e Playwright quando aplicável."
argument-hint: "Comportamento, risco, arquivo ou diff a validar"
agent: "Dokpod Quality Engineer"
---

Produza evidência de teste para o comportamento solicitado.

1. Derive critérios e riscos observáveis.
2. Monte uma matriz curta entre risco e nível de teste.
3. Implemente primeiro o teste de maior valor e demonstre que discrimina o comportamento.
4. Cubra erros e bordas relevantes, sem duplicar testes equivalentes.
5. Use Docker/Podman e PostgreSQL reais quando o comportamento depender deles.
6. Para protocolo, inclua compatibilidade N/N-1, duplicação, deadline, fencing e reconexão quando aplicáveis.
7. Execute os testes novos e a suíte relacionada.

Reporte cobertura, comandos/resultados, plataformas verificadas e lacunas.