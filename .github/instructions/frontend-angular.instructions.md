---
name: "Frontend Angular"
description: "Use ao criar ou alterar Angular 22, TypeScript, componentes, estado, rotas, acessibilidade ou UX operacional."
applyTo: "frontend/**"
---

# Angular 22

- Use Angular 22, componentes standalone, TypeScript estrito e lazy loading por feature.
- Organize a aplicação em `core`, `shell`, `data-access`, `design-system` e `features`.
- Features não importam detalhes internos de outras features.
- Componentes de apresentação não chamam HTTP ou SignalR diretamente.
- O Angular acessa API e SignalR pelo BFF e nunca recebe access token ou refresh token.
- Login, logout, MFA, cadastro, senha, recuperação, grupos e políticas pertencem ao Keycloak.
- Gere o cliente do OpenAPI; não duplique DTOs manualmente.
- Use Signals para estado local/derivado e RxJS em bordas assíncronas e cancelamento.
- Trate loading, vazio, erro, offline, forbidden, inventário desatualizado e comando pendente.
- Trate sessão expirada e indisponibilidade do Keycloak sem fallback de autorização.
- Atualizações em tempo real não movem foco nem reordenam listas inesperadamente.
- Exclusão exige confirmação contextual e nunca inclui volumes implicitamente.
- Use control flow nativo e `track` estável em listas.
- Atenda WCAG 2.2 AA, teclado completo, foco visível e movimento reduzido.
- Use ícones de biblioteca permissiva aprovada e tooltip em ações não óbvias.
- Evite cards aninhados, grandes raios, glassmorphism, gradientes decorativos e estética genérica de SaaS.
- Teste com Vitest e Playwright; valide desktop, mobile e console.

Consulte `docs/frontend.md`.