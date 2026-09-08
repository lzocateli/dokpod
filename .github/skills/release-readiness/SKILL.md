---
name: release-readiness
description: "Avalia prontidão de release do Dokpod. Use antes de versionar, publicar imagens ou promover ambiente para validar qualidade, segurança, contratos N/N-1, agentes Linux/Windows, engines, migrations e operação."
argument-hint: "Versão candidata, base anterior, plataformas e ambiente-alvo"
---

# Release Readiness

Esta skill reúne evidências. Não publica, envia imagens ou faz deploy sem solicitação explícita.

## 1. Definir release

- versão SemVer e base comparada;
- features, correções, breaking changes e deprecações;
- versões de servidor/agente N/N-1 e plataformas suportadas;
- engines/capabilities declaradas e riscos aceitos formalmente.

## 2. Validar produto

- restore/install reproduzível, format, lint e análise estática;
- builds de produção Angular e .NET;
- testes unitários, integração real, contrato, arquitetura e Playwright;
- reconexão, replay, fencing, idempotência e operações destrutivas;
- nenhum teste ignorado novo ou flakiness não explicado.

## 3. Validar segurança e dados

- autorização horizontal, Keycloak indisponível e agente falso/revogado testados;
- nenhum secret, token, certificado privado ou dado sensível em Git, logs ou imagens;
- migrations expand-contract testadas em PostgreSQL real;
- reconciliação do inventário a partir dos engines verificada;
- dependências/licenças, SBOM, vulnerabilidades e provenance revisados.

## 4. Validar distribuição

- imagens passam BuildKit, usam referências reproduzíveis, usuário não root e filesystem read-only quando aplicável;
- entrypoint, labels, portas, mounts e health checks foram inspecionados;
- agente Linux passa smoke test com engine real;
- agente Windows instala, executa e remove em host sem runtime .NET, com conta e ACL corretas;
- upgrade/rollback preserva identidade, journal e compatibilidade N/N-1.

## 5. Validar operação

- configuração, secrets, certificados e rotação estão documentados;
- health, métricas, traces e alertas reconhecem falhas novas;
- rollout, pós-deploy, rollback, backup e restore têm evidência;
- somente superfícies previstas estão expostas; sockets de engine permanecem locais.

## 6. Decisão

Classifique cada gate como `PASS`, `FAIL`, `WAIVED` ou `NOT RUN`. `GO` exige ausência de `FAIL`, ausência de `NOT RUN` obrigatório e waivers com responsável e prazo; caso contrário, recomende `NO-GO`.

Informe matriz de gates, evidências/comandos, bloqueios, waivers e risco residual.