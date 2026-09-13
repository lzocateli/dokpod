# Convenções de EF Core

## Limite arquitetural

EF Core e Npgsql pertencem exclusivamente a projetos `Infrastructure`. O domínio e as aplicações não referenciam pacotes, namespaces, entidades ou APIs do EF Core. As portas de aplicação devem expressar contratos de caso de uso, não detalhes de persistência.

O teste `Dokpod.Architecture.Tests` verifica essa regra para Domain e Application. Qualquer nova dependência de EF fora de Infrastructure deve ser tratada como violação arquitetural e não como atalho de implementação.

## Configuração por tabela e view

Cada tabela ou view persistida possui um arquivo próprio em:

```text
backend/libs/Dokpod.ControlPlane.Infrastructure/Persistence/Configurations/
```

As configurações implementam `IEntityTypeConfiguration<TEntity>` e usam somente Fluent API. O `DbContext` não contém mapeamentos individuais; ele compõe as configurações com `ApplyConfigurationsFromAssembly`.

Convenções:

- `NomeDaEntidadeConfiguration.cs` contém o mapeamento de uma tabela;
- views somente leitura ficam em arquivos próprios e declaram explicitamente ausência de chave quando aplicável;
- entidades EF permanecem internas à Infrastructure e não são DTOs públicos;
- índices, constraints, conversões, nomes físicos e tipos PostgreSQL pertencem à configuração da entidade;
- migrations ficam em `Persistence/Migrations` ou na pasta `Migrations` do módulo, nunca em Domain/Application;
- mudanças de schema exigem migration e teste PostgreSQL real proporcional ao risco.

## Connection strings e secrets

Connection strings, senhas, certificados e demais secrets não entram no repositório, em exemplos com valores reais ou na linha de comando persistida. No desenvolvimento local, use o provider externo de User Secrets configurado para o projeto em:

```text
C:\Users\<usuario>\AppData\Roaming\Microsoft\UserSecrets\dokpod
```

O arquivo externo não deve ser lido, listado, copiado ou editado por automações. A aplicação recebe a configuração por provider seguro ou variável de ambiente injetada pelo processo. Para migrations em design-time, use `DOKPOD_CONTROLPLANE_CONNECTION` apenas como variável de processo temporária e remova-a ao terminar.

A factory de design-time deve falhar fechado quando a connection string não estiver disponível; ela nunca deve usar um valor padrão, gravar secrets ou registrar a connection string em logs.

## Validação

Depois de adicionar ou alterar uma configuração:

1. execute o teste arquitetural;
2. execute os testes de schema e aplicação;
3. aplique a migration em PostgreSQL descartável;
4. valide índices, constraints, particionamento e privilégios com uma role de runtime;
5. execute build e análise do projeto alterado.
