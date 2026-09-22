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
- Tabelas com potencial de crescimento elevado ou retenção temporal usam Declarative Partitioning/Table Partitioning por faixa de data; para auditoria, prefira partições mensais, defina criação antecipada, rollover, partição de segurança, índices locais e retenção antes de produção.
- Teste criação de partições, roteamento de linhas, consultas com pruning, rollover, partição de segurança e comportamento quando a partição esperada não existe.
- SQL manual exige justificativa e plano de execução medido.
- Migrations C# apenas orquestram operações EF e a ordem dos scripts. Procedures, functions, triggers, views, extensões, grants, blocos `DO`, DDL/DML manual e qualquer conteúdo enviado por `migrationBuilder.Sql` ficam em arquivos PostgreSQL `.sql` separados; não mantenha SQL literal, interpolado ou raw string dentro da migration.
- Organize scripts em `Migrations/Script/<MigrationId_Nome>/<Up|Down>/NN-descricao.sql`, com ordem explícita pelo prefixo numérico. Use um arquivo por responsabilidade ou objeto programável e carregue-o por `MigrationSqlScriptLoader.Load(...)`.
- Inclua `Migrations/Script/**/*.sql` como `EmbeddedResource` no projeto de infraestrutura para que migrations publicadas e executadas em container não dependam do diretório de trabalho. O loader deve falhar para recurso ausente ou vazio.
- Scripts são PostgreSQL/PL/pgSQL, não SQL Server: use `timestamptz`, `plpgsql`, `CREATE OR REPLACE FUNCTION` quando aplicável, identificadores do schema `dokpod` e sintaxe suportada pela versão PostgreSQL homologada.
- Declare `suppressTransaction: true` somente quando o comando PostgreSQL realmente não puder executar na transação da migration e documente o impacto de falha parcial e retomada.
- Migrations antigas seguem imutáveis após publicação. Ao adotar ou corrigir o padrão em ambiente já promovido, crie nova migration; não altere script embutido já aplicado.
- Nunca persista chave privada do agente sem proteção apropriada nem conteúdo de secrets do engine.
- Backup, restore e compatibilidade de migration são gates de release.