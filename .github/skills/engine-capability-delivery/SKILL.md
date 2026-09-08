---
name: engine-capability-delivery
description: "Implementa ou evolui uma capability Docker/Podman no Dokpod. Use para adapters, protocolo agente-servidor, operações de container, journal, idempotência, fencing, reconciliação e compatibilidade N/N-1."
argument-hint: "Capability, engines/plataformas alvo e comportamento esperado"
---

# Engine Capability Delivery

## 1. Definir o contrato

1. Especifique a operação, entrada, saída, deadline, idempotência e efeitos proibidos.
2. Declare a matriz Docker/Podman e Linux/Windows; `unsupported` é resultado válido.
3. Confirme que a operação pertence à allowlist e não cria proxy genérico do engine.
4. Se o Protobuf mudar, preserve N/N-1, não reutilize tags e mantenha campos removidos como `reserved`.

## 2. Implementar no domínio correto

1. Mantenha regra e estado de comando independentes do SDK do engine.
2. Isole detalhes do engine em adapter de infraestrutura.
3. Use API estruturada por socket Unix ou named pipe local, nunca shell para operação normal.
4. Valide capability, identificadores, deadline e fencing antes do efeito.
5. Persista journal antes de confirmar aceite e reconcilie resultado após falha parcial.
6. Nunca remova volumes implicitamente ao excluir container.

## 3. Provar comportamento

- unidade: transições, idempotência, deadline e fencing;
- contrato: serialização, evolução N/N-1 e valores desconhecidos;
- engine real: caminho feliz, inexistente, conflito e indisponibilidade;
- transporte: duplicação, resposta perdida, reconexão e ordem;
- segurança: agente falso/revogado, operação fora da allowlist e entrada malformada;
- plataforma: smoke Linux containerizado e Windows Service quando aplicável.

Mocks podem testar lógica local, mas não comprovam comportamento Docker/Podman.

## 4. Revisar e concluir

Solicite review de segurança e código. Informe contrato, matriz de capabilities, engines/plataformas testadas, evidências, incompatibilidades, rollout e riscos residuais. Não faça deploy, push ou publicação.