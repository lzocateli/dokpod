# Runbook: partições e retenção de comandos PostgreSQL

## Escopo

A migration `202609220001_PartitionAgentCommands` converte `dokpod.agent_commands` em uma tabela particionada mensalmente por `created_at_utc`. A tabela `dokpod.agent_command_keys` mantém a identidade global `(environment_id, command_id)` separada das partições para preservar deduplicação entre meses.

A partição `dokpod.agent_commands_default` é uma proteção contra perda de escrita quando a faixa mensal ainda não existe. Linhas nessa partição indicam falha de rollover e exigem correção operacional.

## Sinais

Verifique semanalmente:

- existência de partições para o mês atual e os cinco meses seguintes;
- ausência de comandos na partição `DEFAULT`;
- ausência de comandos não terminais em partições candidatas à retenção;
- uso de pruning em consultas temporais;
- crescimento de `agent_command_keys` e das partições mensais.

```sql
SELECT child.relname AS partition_name,
       pg_size_pretty(pg_total_relation_size(child.oid)) AS total_size
FROM pg_inherits inheritance
JOIN pg_class parent ON parent.oid = inheritance.inhparent
JOIN pg_namespace namespace ON namespace.oid = parent.relnamespace
JOIN pg_class child ON child.oid = inheritance.inhrelid
WHERE namespace.nspname = 'dokpod'
  AND parent.relname = 'agent_commands'
ORDER BY child.relname;

SELECT count(*) AS rows_outside_planned_ranges
FROM dokpod.agent_commands_default;
```

## Rollover

As migrations criam partições mensais de janeiro de 2026 até dezembro de 2036.
Execute a função versionada com a credencial exclusiva de migration antes de o
horizonte restante ficar abaixo de seis meses:

```sql
SELECT dokpod.dokpod_ensure_monthly_partitions(DATE '2038-01-01');
```

Antes de ampliar:

1. confirme que a faixa não sobrepõe outra partição;
2. confirme que `agent_commands_default` não contém linhas da faixa;
3. mova linhas da faixa existentes na `DEFAULT` em uma transação controlada;
4. execute a função em uma janela controlada;
5. valide índices locais e pruning com `EXPLAIN (FORMAT JSON)`;
6. registre a alteração no histórico operacional.

Não anexe uma nova partição enquanto a `DEFAULT` contiver linhas da mesma faixa; o PostgreSQL recusará a operação ou exigirá validação bloqueante.

## Retenção

A duração de retenção é uma decisão operacional humana ainda não aprovada. Até essa aprovação:

- não descarte partições de comandos;
- não remova linhas de `agent_command_keys`;
- preserve comandos terminais e não terminais em backup e restore;
- nunca use retenção para resolver fila acumulada ou comando indeterminado.

Quando uma política for aprovada, descarte somente partições compostas integralmente por comandos terminais e anteriores ao limite aprovado. Comandos `Pending`, `Dispatched`, `Accepted` ou `Indeterminate` bloqueiam o descarte até reconciliação.

A remoção de uma partição não executa triggers por linha. Por isso, os registros correspondentes em `agent_command_keys` permanecem como tombstones e impedem reutilização silenciosa de IDs. A expiração desses tombstones exige política separada, backup verificado e teste explícito da janela máxima de replay.

## Verificação

A migration deve ser aplicada em PostgreSQL descartável antes da promoção. Execute os testes de integração com `DOKPOD_TEST_POSTGRES_CONNECTION`; o resultado esperado inclui:

- roteamento para a partição mensal e para a `DEFAULT`;
- replay concorrente idempotente, inclusive com `created_at_utc` divergente;
- pruning de partições fora da faixa consultada;
- `dokpod_runtime` sem `INSERT`, `UPDATE` ou `DELETE` em `agent_command_keys`;
- triggers de reserva e liberação executados como `SECURITY DEFINER`;
- lifecycle, expiração e auditoria sem regressão.

## Recuperação

A migration é forward-only. Em falha durante rollout:

1. interrompa novas submissões de comandos;
2. preserve `agent_commands_unpartitioned`, caso a transaction ainda não tenha concluído;
3. reverta a aplicação para a versão compatível sem executar `Down`;
4. restaure em banco separado e compare contagens por estado, ambiente e mês;
5. retome somente após validar identidade global, auditoria e comandos não terminais.

Nunca recrie `agent_command_keys` apenas a partir das partições retidas: isso permitiria reutilizar IDs removidos pela retenção.
