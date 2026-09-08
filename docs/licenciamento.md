# Licenciamento e comercialização

**Status da política:** decisão aceita  
**Data:** 2026-09-06  
**Implementação atual:** nenhuma edição publicada; CE é a primeira entrega planejada

Esta é a política operacional derivada do [ADR 2026-0003](adr/2026-0003-licenciamento-e-edicoes.md). O ADR registra contexto e decisão; este documento é a referência para escopo das edições, comercialização e gates de release.

## Modelo

O Dokpod terá duas edições e uma única oferta comercial:

| Aspecto | Dokpod Community Edition | Dokpod Business |
| --- | --- | --- |
| Licença | `AGPL-3.0-only` | licença comercial proprietária |
| Preço | gratuito | US$ 99/mês ou US$ 990/ano por organização |
| Nodes gerenciados | sem limite artificial | até 100 na matriz inicialmente suportada |
| Usuários | sem limite artificial | sem cobrança ou limite artificial |
| Instalações | sem limite imposto pelo Dokpod | uma de produção e uma de homologação |
| Chave de licença | não exige | arquivo assinado, verificável offline |
| Suporte | comunidade | e-mail em horário comercial, sujeito ao SLA contratual |

A CE será implementada e estabilizada antes do início da Business. Os preços são uma decisão de produto; impostos, moeda de cobrança, meios de pagamento, SLA e contrato comercial precisam de validação jurídica e financeira antes da primeira venda.

## Community Edition

A CE será um produto self-hosted funcional, sem chave, telemetria comercial obrigatória ou dependência da infraestrutura do fornecedor. Seu baseline inclui:

- múltiplos ambientes e usuários;
- Docker Linux e demais engines promovidos pela matriz pública de suporte;
- inventário, saúde e sincronização de containers;
- iniciar, parar, reiniciar e excluir containers;
- autenticação pelo Keycloak;
- autorização administrativa, operacional e somente leitura;
- identidade mTLS dos agentes;
- auditoria básica de comandos e resultados;
- API e protocolos públicos necessários à interoperabilidade;
- backup, restauração e atualização manuais;
- correções de segurança para versões suportadas.

A CE não terá limites artificiais de nodes, usuários ou containers. Limites técnicos publicados continuam valendo como matriz testada, não como mecanismo de licenciamento.

## Business

**A Business é uma edição futura e nenhuma das funcionalidades exclusivas abaixo foi iniciada.** Quando implementada, incluirá todo o baseline da CE e concentrará valor em governança, automação e suporte:

- RBAC granular por ambiente, equipe, recurso e ação;
- retenção configurável e exportação de auditoria;
- políticas centralizadas e detecção de divergência;
- ações em lote e operações agendadas;
- alertas e integrações operacionais;
- GitOps, stacks e Compose quando essas capacidades forem implementadas;
- backup automático e políticas de retenção;
- atualização coordenada de agentes com rollout e rollback;
- relatórios operacionais e de conformidade;
- builds com ciclo de suporte estendido;
- suporte comercial por e-mail.

Esta matriz descreve intenção de produto, não funcionalidades já implementadas. Cada capability ainda depende de contrato, testes, segurança, documentação e gate de release próprios.

## Métricas comerciais

### Organização

Uma organização é a pessoa jurídica ou unidade contratante identificada no contrato e na licença. Empresas relacionadas, uso por prestador de serviço e ambientes de clientes precisam ser tratados expressamente pelo contrato comercial; não se presume direito de sublicenciamento ou operação como serviço gerenciado.

### Node

Um node é um host físico, máquina virtual ou dispositivo com engine Docker ou Podman gerenciado pelo Dokpod. A quantidade de containers, CPUs e usuários não altera a contagem. Um host com mais de um engine conta uma vez enquanto representar o mesmo ambiente computacional gerenciado.

A CE mede nodes apenas para capacidade e observabilidade local. A Business usa a contagem para validar a matriz suportada de até 100 nodes, sem transmitir inventário ao fornecedor.

## Licença da CE

O código original publicado como Dokpod Community Edition será disponibilizado sob **GNU Affero General Public License v3.0 only**, identificador SPDX `AGPL-3.0-only`.

Em termos operacionais, a licença permite usar, estudar, modificar e redistribuir a CE, inclusive comercialmente, desde que suas condições sejam cumpridas. Quando uma versão modificada oferecer interação remota pela rede, seus usuários devem receber uma forma de obter o código-fonte correspondente dessa versão, conforme a seção 13 da AGPL.

O texto integral em [LICENSE](../LICENSE) prevalece sobre este resumo. Este documento não é aconselhamento jurídico e não substitui a análise da licença para um caso concreto.

## Escopo e marcas

A AGPL cobre somente arquivos identificados como parte da CE e não altera licenças de componentes de terceiros. Imagens, dependências, fontes e assets preservam seus próprios avisos e termos.

A licença de copyright não concede direito de usar nomes, logotipos, domínios ou identidade visual do Dokpod para sugerir endosso ou origem oficial. Uma política de marcas deve ser publicada antes da primeira release pública.

Código e artefatos exclusivos da Business não serão distribuídos sob AGPL, salvo indicação explícita no próprio arquivo. A simples proximidade em um repositório, imagem ou pacote não deve ser usada para contornar obrigações da AGPL; a composição final precisa de revisão jurídica.

## Duplo licenciamento e contribuições

O titular dos direitos pode oferecer seu próprio código simultaneamente sob AGPL e sob licença comercial. Essa possibilidade depende de controlar os direitos necessários sobre todo código incorporado à distribuição proprietária.

Antes de aceitar contribuições externas, o projeto deverá publicar um CLA aprovado juridicamente ou adotar outro mecanismo explícito que permita usar a contribuição tanto na CE quanto na Business. Até essa definição:

- contribuições externas não serão incorporadas ao produto;
- dependências continuam exigindo licença compatível e verificada;
- código AGPL de terceiros sem direito adicional não será incluído na Business;
- proveniência e autoria serão preservadas em cada contribuição.

## Licença Business

A Business usará um arquivo de licença assinado digitalmente. O payload mínimo será versionado e conterá:

- identificador da licença e da organização;
- edição e versão do schema;
- datas de emissão, início e expiração;
- limite de 100 nodes;
- uma instalação de produção e uma de homologação;
- capacidades licenciadas;
- assinatura digital.

Somente a chave pública de verificação será distribuída com o produto. A chave privada permanecerá fora dos repositórios, imagens e ambientes de cliente. A validação será local e não exigirá conexão periódica com o fornecedor.

Expiração, arquivo inválido ou excesso de nodes nunca poderá parar containers, excluir dados ou bloquear backup e exportação. A política de enforcement deverá:

1. avisar administradores de forma auditável;
2. conceder período de tolerância definido no contrato;
3. impedir novas execuções de funcionalidades exclusivas após a tolerância;
4. preservar leitura, exportação, renovação e todas as funcionalidades da CE;
5. manter workloads existentes sem interferência.

O período de tolerância e o SLA serão definidos no contrato antes da implementação; não devem ser codificados sem essa decisão.

## Suporte e atualizações

- A CE recebe suporte comunitário e documentação pública.
- A Business recebe suporte por e-mail conforme SLA contratual.
- Vulnerabilidades suportadas recebem correção nas duas edições sem atraso deliberado para criar vantagem comercial.
- Recursos Business podem ter ciclo de suporte estendido, desde que isso não retire correções de segurança da CE suportada.
- O cliente mantém acesso aos próprios dados mesmo após o fim da assinatura.

## Checklist antes da primeira release CE

- preservar o texto oficial da `AGPL-3.0-only` em [LICENSE](../LICENSE);
- incluir avisos legais visíveis e link para o código-fonte na interface web;
- publicar código-fonte correspondente, scripts de build e instruções de instalação;
- inventariar licenças de dependências, imagens e assets;
- publicar política de marcas e de contribuições;
- revisar o modelo com assessoria jurídica na jurisdição de operação.

## Checklist antes da primeira venda Business

- aprovar CLA ou mecanismo equivalente;
- aprovar licença comercial, contrato, SLA, privacidade e tributação;
- definir tolerância, renovação, cancelamento e reembolso;
- validar assinatura, adulteração, expiração, relógio incorreto e recuperação;
- testar que enforcement não afeta workloads, dados ou funcionalidades CE;
- demonstrar suporte para 100 nodes na matriz publicada;
- separar artefatos, SBOMs, avisos e pipelines das duas edições.

## Referências

- [ADR 2026-0003](adr/2026-0003-licenciamento-e-edicoes.md)
- [GNU Affero General Public License v3.0](https://www.gnu.org/licenses/agpl-3.0.html)
- [SPDX: AGPL-3.0-only](https://spdx.org/licenses/AGPL-3.0-only.html)
