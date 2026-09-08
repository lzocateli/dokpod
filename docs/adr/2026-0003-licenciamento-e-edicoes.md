# ADR 2026-0003: Licenciamento e edições do produto

**Status:** accepted  
**Data:** 2026-09-06  
**Responsáveis:** Lincoln Zocateli  
**Origem:** humano  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** ADR 2026-0001, ADR 2026-0002 e plano do MVP

## Contexto

O Dokpod precisa permitir adoção gratuita e auditável sem reproduzir a estrutura comercial complexa de concorrentes. Ao mesmo tempo, o projeto precisa sustentar desenvolvimento, distribuição e suporte por meio de uma oferta paga simples. Segurança essencial não pode depender de assinatura, e a edição gratuita precisa ser um produto funcional, não apenas uma demonstração.

## Forças de decisão

- adoção self-hosted sem chave ou dependência de serviço de licenciamento;
- proteção contra apropriação fechada do trabalho comunitário oferecido pela rede;
- distinção comercial fácil de explicar e operar;
- segurança, correções e interoperabilidade disponíveis na edição comunitária;
- receita baseada em governança, automação e suporte;
- possibilidade de operação desconectada;
- preservação do direito de oferecer uma distribuição proprietária futura.

## Opções consideradas

### Edição comunitária permissiva e recursos pagos

Licenças como Apache-2.0 reduzem barreiras de adoção, mas permitem que terceiros distribuam forks fechados e concorram sem compartilhar melhorias.

### Edição comunitária AGPL-3.0-only e edição comercial

A AGPL-3.0-only exige disponibilização do código-fonte correspondente das versões modificadas usadas para prestar serviço pela rede. A edição comercial pode ser oferecida separadamente pelo titular dos direitos, desde que o Dokpod mantenha direitos suficientes sobre todas as contribuições incorporadas.

### Produto exclusivamente proprietário

Simplifica a proteção comercial, mas reduz transparência, contribuição comunitária e adoção inicial.

## Decisão

O Dokpod adotará um modelo open core com duas edições:

1. **Dokpod Community Edition (CE):** primeira edição implementada e publicada, licenciada sob GNU Affero General Public License v3.0 only (`AGPL-3.0-only`), sem cobrança, chave, limite artificial de nodes ou usuários.
2. **Dokpod Business:** edição futura, distribuída sob licença comercial proprietária, com um único preço público de **US$ 99 por mês ou US$ 990 por ano por organização**.

A assinatura Business cobrirá uma instalação de produção, uma instalação de homologação e até 100 nodes gerenciados. Usuários não serão cobrados separadamente. Implantação acima de 100 nodes ficará fora da matriz inicialmente suportada até validação técnica e revisão desta decisão.

A CE permanecerá plenamente utilizável para administrar containers. A Business venderá governança de frota, automação operacional e suporte. Autenticação, autorização básica, mTLS, correções de segurança, backup manual e auditoria mínima não serão removidos da CE nem usados como mecanismo de coerção comercial.

Esta decisão foi definida explicitamente pelo responsável pelo produto em 2026-09-06. Preço, impostos, meios de pagamento, SLA e texto da licença comercial ainda exigem validação financeira e jurídica antes da venda.

## Consequências

### Positivas

- proposta comercial simples de comunicar e comprar;
- CE sem telemetria ou servidor de licença obrigatório;
- proteção copyleft também para uso de versões modificadas pela rede;
- ausência de cobrança por usuário ou por container;
- incentivo econômico concentrado em automação, governança e suporte.

### Negativas e trade-offs

- AGPL-3.0-only pode impedir adoção em organizações com política contrária a copyleft forte;
- contribuições externas podem impedir relicenciamento comercial se os direitos necessários não forem obtidos;
- manter duas edições aumenta custo de build, testes, documentação e suporte;
- preço único transfere para o fornecedor o risco de clientes muito pequenos ou próximos do limite de 100 nodes;
- módulos proprietários não podem ser simplesmente incorporados a uma distribuição AGPL presumindo que permaneçam fechados.

## Segurança e privacidade

As duas edições compartilham o mesmo baseline de segurança. Correções de vulnerabilidades suportadas são publicadas para a CE e para a Business sem atraso comercial deliberado. A validação de licença da Business será local e offline por arquivo assinado; nenhuma edição enviará inventário, conteúdo de logs, nomes de containers, credenciais ou dados pessoais para licenciamento.

## Dados, protocolo e compatibilidade

Contratos OpenAPI e agente-servidor necessários à interoperabilidade da CE permanecem públicos. A Business não cria um protocolo de agente incompatível para forçar migração. Dados persistidos pela Business devem continuar exportáveis, e expiração de assinatura não pode apagar, corromper ou bloquear backup dos dados do cliente.

O titular pode distribuir seu próprio código sob AGPL-3.0-only e sob licença comercial. Para preservar essa capacidade, contribuições externas somente serão incorporadas após aceite de um CLA aprovado juridicamente ou sob outro mecanismo que conceda explicitamente os direitos necessários. Código de terceiros sob AGPL sem direito de relicenciamento não será incluído na distribuição proprietária.

## Observabilidade e operação

A CE não depende de chave. A Business aceitará um arquivo assinado contendo cliente, edição, emissão, expiração, limite de nodes, instalações permitidas e funcionalidades. A aplicação conterá apenas a chave pública de verificação. Indisponibilidade do sistema de venda ou emissão não interromperá uma licença válida já instalada.

## Validação

- publicar o texto integral da AGPL-3.0-only junto à CE e os avisos de terceiros;
- disponibilizar código-fonte correspondente e instruções reproduzíveis da CE;
- validar com assessoria jurídica a política de contribuições, o CLA e a licença Business;
- provar que a CE funciona sem chave e sem conexão com infraestrutura Dokpod;
- provar que uma licença Business válida pode ser verificada offline;
- testar expiração, adulteração, relógio incorreto, excesso de nodes e recuperação sem perda de dados;
- revisar a matriz de recursos antes de implementar qualquer gate comercial.

## Rollout e rollback

A CE será implementada, testada e publicada antes da Business. A separação interna usará contratos e capacidades explícitas, sem espalhar verificações de edição pelas regras de domínio. A Business só será iniciada após estabilização do MVP CE e revisão jurídica.

Se a edição Business não for lançada, a CE continuará funcional sob AGPL-3.0-only. Alterar a licença de versões futuras exige nova ADR e não revoga licenças já concedidas para versões publicadas.

## Referências

- [Política de licenciamento e comercialização](../licenciamento.md)
- [Plano do MVP](../plan/mvp.md)
- [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html)
- [GNU AGPL FAQ](https://www.gnu.org/licenses/gpl-faq.html#AGPLv3InteractingRemotely)