# Guia do GitHub Copilot no Dokpod

Este guia explica como usar as customizações em `.github` para desenvolver, testar e revisar o Dokpod. Elas complementam os requisitos do pedido; não substituem critérios de aceite claros.

## Modelo mental

| Recurso | Função | Uso |
| --- | --- | --- |
| instrução | regra permanente ou específica por caminho | automática |
| agente | papel especializado e limite de autoridade | seletor de agentes |
| prompt | tarefa curta e focada | comando `/` |
| skill | workflow multi-etapas com gates | comando `/` ou descoberta automática |
| documento/template | fonte humana e formato canônico | referência durante o trabalho |

A autoridade segue esta ordem: plano mestre e ADRs aceitos, instruções globais, instruções específicas, customização selecionada e convenções locais.

## Como pedir trabalho

Inclua contexto, resultado observável, escopo, limites, plataformas e validação esperada.

```text
Contexto: operador precisa reiniciar um container em um ambiente remoto.
Resultado: comando idempotente com estado visível e auditoria.
Escopo: contrato, API, agente Docker Linux e interface.
Limites: não implementar terminal nem remover volumes.
Plataformas: Docker Engine em Linux; Podman retorna unsupported.
Validação: repetição do mesmo command ID produz um único efeito e a UI converge após resposta perdida.
```

Nunca inclua tokens, certificados privados, variáveis de ambiente ou logs integrais de containers no pedido.

## Agentes

| Situação | Agente |
| --- | --- |
| feature entre várias camadas | `Dokpod Delivery Lead` |
| arquitetura, ADR ou plano | `Dokpod Solution Architect` |
| API, BFF, domínio, aplicação ou PostgreSQL | `Dokpod .NET Engineer` |
| Docker/Podman, gRPC, mTLS, journal ou reconciliação | `Dokpod Engine & Protocol Engineer` |
| Angular, estado, acessibilidade ou UX operacional | `Dokpod Angular Engineer` |
| estratégia e implementação de testes | `Dokpod Quality Engineer` |
| threat model ou revisão de segurança | `Dokpod Security Reviewer` |
| revisão de diff ou pull request | `Dokpod Code Reviewer` |

Os reviewers são read-only. O Solution Architect só pode editar `docs/adr` e `docs/plan`. O Delivery Lead pode delegar aos demais agentes, mas não faz commit, push, publicação ou deploy sem pedido explícito.

## Prompts

| Comando | Quando usar |
| --- | --- |
| `/plan-feature` | transformar uma necessidade em plano sem implementar |
| `/implement-feature` | entregar uma feature vertical |
| `/create-adr` | analisar e registrar uma decisão arquitetural proposta |
| `/diagnose-problem` | reproduzir, corrigir e testar uma falha |
| `/generate-tests` | criar evidência orientada aos riscos |
| `/review-change` | revisar diff, branch ou pull request sem editar |

Exemplo:

```text
/diagnose-problem

Após timeout de STOP, a reconexão executa o mesmo comando novamente.
Ambiente: Docker Linux.
Esperado: o journal reconhece o command ID e reconcilia o estado sem repetir o efeito.
Validação: teste com resposta perdida e reconexão.
```

## Skills

| Comando | Quando usar |
| --- | --- |
| `/feature-delivery` | entrega complexa do requisito aos reviews |
| `/engine-capability-delivery` | criar ou evoluir capability Docker/Podman e protocolo associado |
| `/pull-request-review` | revisão multidimensional de PR |
| `/release-readiness` | decisão GO/NO-GO antes de publicar ou promover |

Use `/engine-capability-delivery` quando o comportamento cruza domínio, adapter e protocolo. A skill exige matriz explícita de engine/plataforma e aceita `unsupported` como capability válida; ela não força falsa equivalência entre Docker e Podman.

`/release-readiness` apenas avalia evidências. `NOT RUN` não é sucesso, e a skill não publica nem faz deploy.

## Exemplos por fluxo

### Capability de engine

```text
/engine-capability-delivery

Adicionar PAUSE e UNPAUSE para Docker Linux.
Podman e Windows permanecem fora do escopo nesta entrega.
O comando deve ser idempotente, expirar e reconciliar após resposta perdida.
```

### Revisão de segurança

```text
Use o Dokpod Security Reviewer para revisar o bootstrap de agente.
Trace token de uso único, chave pública, emissão do certificado, revogação e primeira conexão.
Não edite arquivos.
```

### Prontidão de release

```text
/release-readiness

Versão candidata: 0.2.0.
Base: 0.1.0.
Alvos: agente Linux em Docker e Worker Service Windows self-contained.
Não publique; produza matriz de gates e recomendação GO/NO-GO.
```

## Validações esperadas

- .NET: build e testes containerizados do projeto ou solução afetada;
- adapters: integração com engine real, nunca apenas mock;
- frontend: comandos `ng` a partir de `frontend/web`;
- contratos: geração e testes de consumidor/provedor, incluindo N/N-1;
- containers: build, inspeção e smoke test;
- Windows: instalação e execução em host sem runtime .NET;
- desempenho: perfis e evidências de `PERFORMANCE_TESTING_CRITERIA.md`;
- segurança: autorização horizontal, agente falso/revogado, replay e vazamento de secrets.

Execute primeiro a validação mais estreita que pode refutar a mudança. Amplie apenas após o recorte passar.

## Descoberta no VS Code

Selecione um agente no topo do Chat ou digite `/` para listar prompts e skills. Se um arquivo novo não aparecer, execute `Developer: Reload Window` e abra um novo chat na raiz do Dokpod.

Se uma regra não for aplicada, confira o `applyTo`, a descrição de descoberta e a raiz do workspace antes de duplicar instruções.
