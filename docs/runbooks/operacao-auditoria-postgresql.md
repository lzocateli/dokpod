# Runbook: operação da auditoria PostgreSQL

## Escopo

A migration `202609120001_CreateAuditEvents` cria a tabela `audit_events` particionada, o registro global de chaves `audit_event_keys` e a role sem login `dokpod_runtime`. A migration é forward-only: rollback destrutivo não é permitido porque apagaria evidência.

O usuário de migration deve ser diferente do usuário de runtime. O usuário de runtime precisa receber membership em `dokpod_runtime`; essa role possui somente `SELECT` nas tabelas e `EXECUTE` na função controlada `dokpod_append_audit_event`. A role não recebe `INSERT` direto nas tabelas.

## Aplicação

No diretório raiz do Dokpod, injete uma connection string de laboratório por processo e execute a migration com `dotnet-ef`:

```powershell
$env:DOKPOD_CONTROLPLANE_CONNECTION = 'Host=127.0.0.1;Port=5432;Database=dokpod;Username=migration_user'
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
WHERE tablename LIKE 'audit_events%'
ORDER BY tablename;

SELECT grantee, privilege_type
FROM information_schema.role_table_grants
WHERE table_name IN ('audit_events', 'audit_event_keys')
ORDER BY grantee, table_name, privilege_type;
```

Resultado esperado: partições mensais planejadas, `audit_events_default` e apenas `SELECT`/`INSERT` para `dokpod_runtime`.

Os testes `PostgresAuditSchemaIntegrationTests` usam uma conexão PostgreSQL efêmera somente quando
`DOKPOD_TEST_POSTGRES_CONNECTION` está injetada no processo. Sem essa variável, os testes são pulados
explicitamente. A migration deve ser aplicada antes da execução.

`UPDATE` e `DELETE` devem falhar por privilégio ou pelo trigger append-only. Falha de migration, ausência de partição planejada ou linhas inesperadas na partição `DEFAULT` bloqueiam a promoção e devem ser escaladas para o responsável pela operação do banco.

## Recuperação

Não execute `Down` para remover auditoria. Em caso de rollback da aplicação, preserve as tabelas e desabilite apenas o fluxo novo até corrigir a versão. Restauração deve ocorrer em uma cópia verificada, com backup e teste de integridade antes de qualquer decisão operacional.
