---
name: "Arquitetura Dokpod"
description: "Use ao criar módulos, mover responsabilidades, adicionar dependências, integrar engines ou escrever ADRs."
applyTo: "backend/**, frontend/**, contracts/**, deploy/**"
---

# Arquitetura

- Adote monólito modular no plano de controle e processo separado para o agente.
- Hosts em `backend/apps` são composition roots; regras pertencem a `backend/libs`.
- API, BFF e web sempre executam em containers; agente Linux em container e agente Windows como Worker Service self-contained.
- Domínio não depende de ASP.NET Core, EF Core, filesystem, Docker ou Podman.
- Plano de controle e agente possuem aplicações/infraestruturas separadas; compartilham apenas domínio essencial e contratos.
- Cada aplicação define casos de uso e portas; sua infraestrutura implementa adapters.
- `contracts` é independente de C# e TypeScript e origina clientes gerados.
- O engine local é a fonte de verdade; inventário persistido é projeção reconstruível.
- Comunicação distribuída usa deduplicação durável, fencing, expiração, backpressure e reconciliação.
- Não implemente proxy genérico do engine.
- Keycloak é a autoridade obrigatória de identidade e autorização; BFF é cliente OIDC confidencial e API é PEP.
- O Dokpod não mantém senhas, memberships nem políticas próprias; agentes continuam autenticados por mTLS.
- Modele Docker e Podman por capabilities e adapters distintos.
- Não introduza broker, cache distribuído, novo datastore ou microsserviço sem ADR.
- Tabelas ou coleções com potencial de crescimento elevado devem usar Table Partitioning/Declarative Partitioning por faixa temporal ou outra estratégia equivalente do banco adotado; a decisão deve cobrir granularidade, criação antecipada, rollover, retenção, índices, pruning, backup/restore e comportamento quando faltar uma partição.
- Nova fronteira de confiança, mudança na integração Keycloak, protocolo ou mudança incompatível exige ADR.
- Dependências externas exigem licença permissiva, gratuita e verificada na versão exata.
- Testes de arquitetura impedem referências proibidas e ciclos.

Consulte `docs/arquitetura.md` e `docs/adr/2026-0001-arquitetura-inicial.md`.