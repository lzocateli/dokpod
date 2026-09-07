# Frontend

## Objetivo

O frontend é um console operacional Angular 22 para leitura rápida, comparação entre ambientes e execução deliberada de ações com impacto.

## Arquitetura

- componentes standalone e lazy loading por feature;
- TypeScript estrito;
- Signals para estado local e derivado;
- RxJS para HTTP, SignalR, cancelamento e fluxos assíncronos;
- cliente HTTP gerado de `contracts/openapi`;
- sessão mediada pelo BFF; o Angular nunca lê access token ou refresh token;
- componentes de apresentação sem acesso direto à rede;
- bibliotecas compartilhadas sem dependência interna da aplicação.

Estrutura inicial:

```text
frontend/web/src/app/
  core/
  shell/
  data-access/
  design-system/
  features/environments/
  features/containers/
  features/audit/
frontend/libs/
frontend/tests/
```

Features integram por rotas, contratos públicos e fachadas pequenas; não importam detalhes internos umas das outras.

## Experiência operacional

- densidade informacional adequada a tarefas repetidas;
- estado de conexão e idade do inventário sempre visíveis;
- ações destrutivas distinguem container, volumes e efeitos;
- confirmação contextual para exclusão;
- loading, vazio, erro, offline, forbidden e estado desatualizado tratados;
- login, logout, sessão expirada e acesso negado seguem o fluxo do Keycloak pelo BFF;
- atualização em tempo real não move foco nem reordena listas inesperadamente;
- filtros e seleção são preservados durante refresh.

## Design system

O visual deve ser utilitário, preciso e próprio do Dokpod. Usar tokens semânticos, bordas discretas, baixa curvatura e tipografia adequada a dados operacionais. Evitar estética genérica de SaaS, gradientes decorativos, glassmorphism, sombras difusas e cards aninhados.

Ícones vêm de uma biblioteca permissiva aprovada e sempre possuem nome acessível ou tooltip quando o significado não é óbvio.

## Acessibilidade e desempenho

- WCAG 2.2 AA;
- navegação completa por teclado e foco visível;
- região viva para resultado de comandos sem anúncios excessivos;
- tabelas e listas extensas com virtualização quando medição justificar;
- respeito a preferência de movimento reduzido;
- layouts sem sobreposição em desktop e mobile;
- logs carregados sob demanda, truncados e nunca renderizados como HTML confiável.

O frontend não implementa tela de senha, cadastro, recuperação, MFA, grupos ou políticas. Essas jornadas pertencem ao Keycloak e ao provedor federado configurado.

## Testes

- Vitest para componentes, estado e data access;
- `lzocateli/angular-cli:22.1.0-node24.15.0-bookworm` para install, lint, typecheck, testes e build;
- `lzocateli/playwright-e2e:0.1.0` para testes black-box de cadastro de ambiente, inventário e ciclo de vida do container;
- testes de acessibilidade dos fluxos críticos;
- screenshots desktop/mobile para mudanças visuais;
- build de produção, lint, typecheck e ausência de erros inesperados no console.

Os assets de produção são servidos por uma imagem Dokpod derivada de `lzocateli/nginx:1.28.0-bookworm`; Node.js e Angular CLI não fazem parte da imagem final. A matriz completa e a política de digest estão em [Distribuição e operação](distribuicao.md#imagens-base-e-toolchains).