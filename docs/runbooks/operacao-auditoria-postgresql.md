# Runbook: operação da auditoria PostgreSQL

## Escopo

A migration `202609120001_CreateAuditEvents` cria a tabela `audit_events` particionada, o registro global de chaves `audit_event_keys` e a role sem login `dokpod_runtime`. A migration é forward-only: rollback destrutivo não é permitido porque apagaria evidência.

O usuário de migration deve ser diferente do usuário de runtime. O usuário de runtime precisa receber membership em `dokpod_runtime`; essa role possui somente `SELECT` nas tabelas e `EXECUTE` na função controlada `dokpod_append_audit_event`. A role não recebe `INSERT` direto nas tabelas. No laboratório compartilhado atual, a conexão usa a instância local em `localhost:5432` e `Search Path=dokpod`; tabelas, partições, funções e histórico EF ficam no schema `dokpod`.

## Aplicação

No diretório raiz do Dokpod, injete uma connection string de laboratório por processo e execute a migration com `dotnet-ef`:

```powershell
$env:DOKPOD_CONTROLPLANE_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=keycloak;Username=migration_user;Search Path=dokpod'
dotnet ef database update `
  --project backend/libs/Dokpod.ControlPlane.Infrastructure/Dokpod.ControlPlane.Infrastructure.csproj `
  --startup-project backend/apps/Dokpod.ControlPlane.Api/Dokpod.ControlPlane.Api.csproj `
  --configuration Release
Remove-Item Env:DOKPOD_CONTROLPLANE_CONNECTION
```

O valor acima é fictício. Não registre connection strings, senhas ou secrets em arquivos, logs ou argumentos persistidos.

## Rollover

A migration cria partições mensais antecipadamente e uma partição `DEFAULT` de segurança. Antes do primeiro dia de cada mês, o operador deve:

1. criar a partição do mês seguinte com `CREATE TABLE ... PARTITION OF audit_events`;
2. confirmar que a faixa não sobrepõe partições existentes;
3. confirmar que a partição `DEFAULT` não contém linhas daquela faixa;
4. verificar os índices locais e o pruning com `EXPLAIN` em uma consulta autorizada;
5. registrar a alteração no histórico operacional.

A partição `DEFAULT` evita perda silenciosa, mas não substitui o rollover. Linhas nela exigem diagnóstico e criação da faixa correta antes de qualquer movimentação controlada.

## Verificação

Consultar a estrutura sem conteúdo sensível:

```sql
SELECT tablename
FROM pg_tables
WHERE schemaname = 'dokpod'
  AND tablename LIKE 'audit_events%'
ORDER BY tablename;

SELECT grantee, privilege_type
FROM information_schema.role_table_grants
WHERE table_schema = 'dokpod'
  AND table_name IN ('audit_events', 'audit_event_keys')
ORDER BY grantee, table_name, privilege_type;

SELECT routine_schema, routine_name
FROM information_schema.routines
WHERE routine_schema = 'dokpod'
  AND routine_name IN ('dokpod_append_audit_event', 'dokpod_reject_audit_mutation')
ORDER BY routine_name;
```

Resultado esperado: partições mensais planejadas, `audit_events_default`, funções no schema `dokpod` e somente `SELECT` nas tabelas para `dokpod_runtime`; escrita runtime ocorre por `EXECUTE` em `dokpod.dokpod_append_audit_event`, não por `INSERT` direto.

Os testes `PostgresAuditSchemaIntegrationTests` usam uma conexão PostgreSQL efêmera somente quando
`DOKPOD_TEST_POSTGRES_CONNECTION` está injetada no processo. Sem essa variável, os testes são pulados
explicitamente. A migration deve ser aplicada antes da execução.

No laboratório compartilhado, use `Search Path=dokpod` na connection string de teste para validar o mesmo namespace lógico usado pela aplicação.

`UPDATE` e `DELETE` devem falhar por privilégio ou pelo trigger append-only. Falha de migration, ausência de partição planejada ou linhas inesperadas na partição `DEFAULT` bloqueiam a promoção e devem ser escaladas para o responsável pela operação do banco.

## Recuperação

Não execute `Down` para remover auditoria. Em caso de rollback da aplicação, preserve as tabelas e desabilite apenas o fluxo novo até corrigir a versão. Restauração deve ocorrer em uma cópia verificada, com backup e teste de integridade antes de qualquer decisão operacional.
