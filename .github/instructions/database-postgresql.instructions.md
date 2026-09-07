---
name: "PostgreSQL"
description: "Use ao alterar EF Core, Npgsql, migrations, SQL, schema, índices, comandos ou auditoria."
applyTo: "backend/libs/**/*DbContext*.cs, backend/libs/**/Migrations/**, backend/libs/**/*.sql, backend/tests/**/*Database*.cs"
---

# Persistência PostgreSQL

- Use EF Core e Fluent API; não use Data Annotations para schema.
- Não adicione segundo ORM nem repository genérico.
- Migrations seguem expand-contract e possuem teste com PostgreSQL real.
- Inventário é projeção reconstruível; não o trate como autoridade do engine.
- Comandos, leases, execuções e auditoria são duráveis e possuem índices explícitos.
- Use concorrência otimista e constraints para invariantes persistentes.
- Auditoria é append-only; correções geram novo evento.
- Limite retenção de snapshots e payloads antes de produção.
- SQL manual exige justificativa e plano de execução medido.
- Nunca persista chave privada do agente sem proteção apropriada nem conteúdo de secrets do engine.
- Backup, restore e compatibilidade de migration são gates de release.