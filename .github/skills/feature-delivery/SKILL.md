---
name: feature-delivery
description: "Entrega uma feature vertical do Dokpod do requisito à validação. Use para funcionalidades que envolvem arquitetura, OpenAPI/Protobuf, Angular, .NET, PostgreSQL, agentes, engines, testes, segurança e documentação."
argument-hint: "Feature, problema do usuário e critérios de aceite"
---

# Feature Delivery

## 1. Enquadrar

1. Leia o [plano mestre](../../../README.md) e os documentos do domínio afetado.
2. Defina ator, problema, resultado, não escopo e critérios observáveis.
3. Identifique projetos, contratos, capabilities, dados e fronteiras de confiança afetados.
4. Registre plataformas suportadas, premissas e perguntas realmente bloqueantes.

## 2. Decidir

1. Verifique se a mudança exige ADR.
2. Preserve o engine local como fonte de verdade e PostgreSQL como projeção reconstruível.
3. Preserve Keycloak como autoridade e compatibilidade N/N-1 do protocolo.
4. Escolha o menor desenho que suporte erro, retry, idempotência, reconexão e recuperação.
5. Defina rollout e rollback antes de mudança irreversível.

## 3. Planejar slices

Ordene slices pequenos, cada um com validação discriminante:

1. contrato e exemplos;
2. regra de domínio e caso de uso;
3. adapter de engine ou persistência;
4. transporte, endpoint ou evento;
5. data access e estado frontend;
6. interface e acessibilidade;
7. observabilidade, operação e documentação.

## 4. Implementar

1. Comece pelo teste ou código proprietário do comportamento.
2. Declare hipótese local e check discriminante antes da primeira edição.
3. Faça a menor edição coerente e execute imediatamente a validação mais estreita.
4. Corrija o mesmo recorte antes de ampliar.
5. Atualize clientes gerados, migrations e docs somente quando o contrato exigir.
6. Preserve alterações existentes e não reformate fora do escopo.

## 5. Verificar por risco

- **Autorização:** autorize antes de revelar ambiente, container ou metadado; Keycloak indisponível falha fechado.
- **Operações:** comando é idempotente, expira, audita e reconcilia após resposta perdida.
- **Agentes:** mTLS, revogação, fencing, sequência e reconexão são preservados.
- **Engines:** capabilities são explícitas; adapters são testados contra Docker/Podman real.
- **Experiência:** loading, vazio, erro, forbidden, indisponibilidade e operação pendente são claros.
- **Operação:** logs não vazam secrets; health, métricas, rollout e rollback cobrem o fluxo.

## 6. Gates e reviews

Execute conforme o risco: format, análise estática, build, testes unitários, integração real, contratos N/N-1, Playwright, segurança, build e smoke de containers e agente Windows sem runtime instalado.

Solicite revisão de código do diff completo e revisão de segurança para identidade, autorização, protocolo, engine, operação destrutiva, secret ou deployment. Corrija achados válidos e reexecute os gates afetados.

## 7. Resultado

Informe critérios atendidos, decisões/ADRs, arquivos e contratos, engines/plataformas verificadas, comandos/resultados, reviews e riscos residuais. Não faça commit, push, publicação ou deploy sem solicitação explícita.