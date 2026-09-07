---
name: "Observabilidade"
description: "Use ao alterar logs, métricas, traces, health checks, auditoria, dashboards ou alertas."
applyTo: "backend/**/*Telemetry*, backend/**/*Observability*, backend/**/*Health*, deploy/**/*otel*, deploy/**/*prometheus*, deploy/**/*grafana*"
---

# Observabilidade

- Use OpenTelemetry e convenções semânticas estáveis.
- Propague correlation ID entre browser, API, comando e agente sem aceitar valor não validado como autoridade.
- Meça conexões, reconexões, idade do inventário, backlog, latência e resultado de comandos.
- Separe logs operacionais de auditoria append-only.
- Não use environment ID, container name ou command ID como label métrica sem controlar cardinalidade.
- Não registre tokens, certificados, variáveis, secrets ou logs integrais de containers.
- Health checks distinguem liveness, readiness, banco e capacidade de aceitar agentes.
- Alertas apontam impacto e ação; não disparam por evento isolado sem contexto.
- Defina SLOs somente após baseline medido e documente janela e orçamento de erro.