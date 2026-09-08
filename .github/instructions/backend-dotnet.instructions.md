---
name: "Backend e agente .NET"
description: "Use ao criar ou alterar C#, .NET 10, ASP.NET Core, API, agente, domínio, aplicação ou infraestrutura."
applyTo: "backend/**"
---

# .NET 10

- Use .NET 10 LTS, nullable, implicit usings e analyzers como erro no CI.
- Use tipos imutáveis para comandos, resultados, eventos e value objects.
- Propague `CancellationToken`; não use `.Result`, `.Wait()` ou `async void` fora de callback exigido.
- Mantenha `Program.cs` pequeno e organize configuração, DI, middleware e endpoints por responsabilidade.
- API e BFF são hosts distintos e sempre executam em containers.
- O BFF usa Authorization Code com PKCE, cookies protegidos e tokens fora do browser.
- A API valida tokens e aplica decisões do Keycloak como PEP antes de acessar recursos.
- O agente Linux executa em container; o mesmo agente em Windows é Worker Service publicado self-contained por RID homologado.
- Não vaze entidades de persistência ou payloads do engine para contratos públicos.
- Não use repository genérico sobre EF Core nem misture ORMs.
- Use `HttpClient` e transports testáveis para Unix socket e named pipe; não invoque CLI por shell no fluxo normal.
- Limite respostas, concorrência e duração das chamadas ao engine.
- Commands mutáveis possuem ID idempotente, deadline e resultado reconciliável.
- Retry só cobre falha transitória e nunca presume que timeout significa ausência de efeito.
- API usa REST versionado, OpenAPI e `application/problem+json`.
- gRPC transporta o protocolo do agente; SignalR apenas notifica o browser.
- Health checks distinguem liveness, readiness e dependências.
- Logs são estruturados e não contêm tokens, secrets, variáveis nem logs integrais de container.
- Testes de adapter usam Docker/Podman reais; Windows valida serviço, RID, named pipe, ACL e host sem runtime .NET.

Consulte `docs/backend.md`.