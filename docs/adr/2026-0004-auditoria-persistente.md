# ADR 2026-0004: Auditoria persistente append-only

**Status:** accepted  
**Data:** 2026-09-10  
**Responsáveis:** Lincoln Zocateli  
**Origem:** IA assistida  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** P-03 do plano MVP, plano [p03-auditoria-persistente](../plan/p03-auditoria-persistente.md), ADR 2026-0001 e ADR 2026-0002

## Contexto

O P-03 exige auditoria inicial para registrar o cadastro e a aprovação de ambientes. O domínio já possui o contrato imutável `AuditEvent` e a aplicação possui a porta `IAuditEventWriter`, mas o repositório ainda não possui infraestrutura PostgreSQL, `DbContext`, migration ou writer durável.

A auditoria precisa sobreviver a reinícios, retries, respostas perdidas e falhas parciais. Também precisa preservar a autoridade do Keycloak: o PostgreSQL do Dokpod não pode se tornar cópia de usuários, memberships, roles ou políticas.

## Forças de decisão

- integridade e não repúdio operacional dos eventos;
- idempotência para retries e concorrência;
- compatibilidade expand-contract e rollback da aplicação;
- separação entre domínio, aplicação e infraestrutura;
- menor privilégio para o usuário PostgreSQL de runtime;
- operação containerizada com PostgreSQL real nos testes;
- ausência de secrets, tokens, certificados privados ou payload integral do engine;
- crescimento previsível, pruning de consultas e retenção temporal sem apagar evidência silenciosamente.

## Opções consideradas

### Opção A: PostgreSQL no módulo de infraestrutura do Control Plane

Criar `backend/libs/Dokpod.ControlPlane.Infrastructure`, referenciado pelo host API e dependente da aplicação/domínio. Usar EF Core com Npgsql, `DbContext` próprio do plano de controle, migrations versionadas e uma entidade interna para auditoria.

Vantagens:

- segue a arquitetura já aceita e a documentação de backend;
- mantém EF Core fora do domínio e da aplicação;
- usa o datastore já aprovado para comandos, projeções e auditoria;
- permite constraints, índices, transações e testes com PostgreSQL real.

Desvantagens:

- introduz projeto, dependências e migration;
- exige composição de conexão, privilégios e operação de schema;
- requer cuidado para não expor entidades EF nos contratos.

### Opção B: tabela e SQL dentro do host API

Executar SQL diretamente no projeto da API, sem módulo de infraestrutura separado.

Vantagens:

- menor quantidade inicial de projetos;
- composição imediata no host.

Desvantagens:

- mistura composition root com persistência;
- dificulta testes e evolução de comandos, ambientes e projeções;
- cria acoplamento da API a SQL e aumenta risco de bypass arquitetural;
- não segue a separação definida em ADR 2026-0001.

### Opção C: arquivo local ou novo serviço de auditoria

Persistir eventos em arquivo ou criar serviço/banco separado.

Vantagens:

- poderia reduzir dependência inicial de PostgreSQL.

Desvantagens:

- arquivo não atende coordenação, consulta autorizada, backup e operação multi-instância;
- novo serviço/datastore contradiz o monólito modular e exige ADR adicional;
- aumenta superfície operacional sem necessidade demonstrada.

## Decisão

**Proposta:** adotar a Opção A, condicionada à aprovação humana desta ADR.

A auditoria será implementada no módulo `Dokpod.ControlPlane.Infrastructure`, usando EF Core e Npgsql com versões centralizadas e verificadas. O domínio continuará independente de persistência. A aplicação dependerá somente da porta `IAuditEventWriter`.

A tabela de auditoria deverá:

- possuir `EventId` único;
- armazenar `EnvironmentId`, `CorrelationId`, ator, ação, resultado, código de falha e timestamp UTC;
- usar hash ou comparação canônica suficiente para detectar o mesmo `EventId` com payload divergente;
- possuir índices explícitos para ambiente/tempo e correlação;
- ser particionada declarativamente por `OccurredAtUtc` usando `PARTITION BY RANGE`, com partições mensais como granularidade inicial;
- possuir partições criadas antecipadamente para a janela operacional, uma partição de segurança para datas fora da janela e procedimento testado de rollover;
- não oferecer operações de alteração ou exclusão pela porta da aplicação;
- ser protegida no banco contra `UPDATE` e `DELETE` pelo usuário de runtime;
- tratar repetição do mesmo evento e payload como idempotente;
- rejeitar conflito de payload para o mesmo `EventId`;
- aplicar limites e allowlists já definidos no contrato de domínio.

O usuário PostgreSQL usado pelo runtime terá apenas os privilégios necessários para inserir e consultar conforme os casos de uso. Aplicação de migrations será uma responsabilidade operacional separada, com credencial distinta, conforme a configuração aprovada do deployment.

A integração do cadastro emitirá auditoria somente após a decisão de autorização do Keycloak e antes de confirmar uma operação que exige trilha. Decisão negativa, expirada ou indisponível continuará falhando fechada; nenhum escopo local de `EnvironmentRegistration` substituirá a decisão do Keycloak.

Eventos corrigidos serão novos eventos. A retenção e o arquivamento ainda exigem definição operacional, mas o particionamento temporal é requisito desta decisão para impedir crescimento descontrolado e preservar pruning de consultas.

## Consequências

### Positivas

- auditoria durável no datastore já aprovado;
- separação clara entre contrato, caso de uso e adapter;
- retry idempotente e conflito detectável;
- proteção de integridade por constraints e privilégios;
- testes reproduzíveis em PostgreSQL containerizado;
- rollback da aplicação sem apagar evidência.

### Negativas e trade-offs

- novo projeto e dependências no backend;
- migration e privilégios passam a ser parte do rollout;
- consultas de auditoria exigem autorização por ambiente;
- retenção e arquivamento ainda exigem política operacional;
- rollover ou partição ausente pode interromper gravações;
- persistência e operação do PostgreSQL tornam-se pré-requisitos do fluxo auditável.

## Segurança e privacidade

A auditoria não persistirá tokens, cookies, secrets, chaves privadas, certificados privados, variáveis de ambiente, conteúdo de logs ou payload integral de comandos do engine. IDs de ator e ambiente serão opacos e limitados. Logs de persistência conterão apenas IDs técnicos, códigos de erro e correlation ID.

A API continuará atuando como PEP: autorização ocorre antes de revelar ambiente, container ou auditoria. Indisponibilidade do Keycloak ou decisão inválida não habilita fallback local. O agente não receberá tokens de usuário.

O usuário de runtime não poderá atualizar ou apagar eventos. Correções serão novos eventos, e falhas de integridade deverão interromper a confirmação da operação auditável.

## Dados, protocolo e compatibilidade

A alteração é interna ao backend e não altera o protocolo protobuf do agente. O evento de domínio já existente permanece a fonte do contrato interno. A migration será expand-contract e deverá ser compatível com a versão anterior durante rollback.

Entidades EF serão internas à infraestrutura. API e frontend receberão DTOs públicos próprios em etapa posterior, sem exposição de modelos de persistência.

## Observabilidade e operação

A infraestrutura deverá medir contagem, latência, conflitos de idempotência e falhas de append, sem registrar conteúdo sensível. Readiness distinguirá API, PostgreSQL e Keycloak. Backup e restore do PostgreSQL serão testados antes da promoção.

A aplicação não aplicará migrations destrutivas automaticamente no startup. A operação deverá executar migration com credencial própria e validar a versão antes de habilitar o fluxo.

## Validação

A proposta será confirmada quando os seguintes checks passarem:

- build e testes na imagem `lzocateli/dotnet-sdk:10.0.400-noble`;
- migration aplicada em PostgreSQL real containerizado;
- inserção, retry idempotente e conflito de payload testados sob concorrência;
- partições mensais, partição de segurança, roteamento por data, pruning e rollover testados em PostgreSQL real;
- `UPDATE` e `DELETE` recusados para o usuário de runtime;
- cancelamento propagado sem confirmação falsa;
- falha de append impede confirmação da operação auditável;
- Keycloak indisponível, decisão negada e token expirado falham fechados;
- revisão de código e segurança sem achados altos ou críticos.

## Rollout e rollback

Aplicar primeiro a migration expand em laboratório. Publicar o writer atrás do caso de uso de cadastro e habilitar o fluxo somente após health/readiness de PostgreSQL e Keycloak. Preservar a migration durante rollback e desabilitar o fluxo novo sem apagar eventos.

Qualquer alteração destrutiva de schema, retenção automática, mudança da granularidade do particionamento ou separação em novo serviço exige nova ADR ou revisão desta decisão.

## Referências

- [Plano MVP](../plan/mvp.md)
- [Plano de auditoria persistente](../plan/p03-auditoria-persistente.md)
- [Arquitetura](../arquitetura.md)
- [Backend e agente](../backend.md)
- [Segurança](../seguranca.md)
- [Configuração do Keycloak](../configuracao-keycloak.md)
