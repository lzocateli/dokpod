---
name: "Testes Dokpod"
description: "Use ao criar ou alterar testes unitários, integração, contrato, Playwright, segurança ou desempenho."
applyTo: "backend/tests/**, frontend/tests/**, frontend/**/*.spec.ts, frontend/**/*.test.ts, **/*Tests.cs"
---

# Testes

- Teste comportamento observável e falhas, não detalhes privados.
- Use xUnit no .NET, Vitest no Angular e Playwright em jornadas.
- Domínio usa testes unitários; adapters usam engines e PostgreSQL reais.
- Não use mocks como evidência principal de compatibilidade Docker/Podman.
- Cubra sucesso, cancelamento, timeout, replay, resposta perdida e reconexão.
- Verifique que duas mutações no mesmo container são serializadas e ambientes independentes progridem em paralelo.
- Contract tests cobrem versões de API e capabilities ausentes.
- Segurança inclui Keycloak indisponível/revogado, token inválido, CSRF, autorização horizontal, agente falso, certificado revogado, SSRF e payload hostil.
- Testes Windows declaram versão do host, RID, conta do serviço e engine e executam sem runtime .NET instalado.
- Fixtures usam apenas dados sintéticos e nunca contêm credenciais.
- Testes de carga e capacidade seguem `.github/PERFORMANCE_TESTING_CRITERIA.md`.
- Teste alterado deve falhar sem a correção e passar com ela.
- Registre comandos e resultados no pull request; não marque gate sem executar.