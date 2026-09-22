---
name: "Frontend PO UI"
description: "Use ao criar, alterar ou revisar telas, formulários, tabelas, navegação e design system Angular do Dokpod com a biblioteca PO UI."
applyTo: "frontend/**"
---

# PO UI

- PO UI (`@po-ui/ng-components`, com `@po-ui/ng-templates` quando um page template resolver o layout) é a única biblioteca de componentes de UI aprovada para o Dokpod. Não instale nem invente componente visual alternativo (Material, PrimeNG, Bootstrap, CSS customizado que duplica um componente PO UI existente).
- Antes de implementar qualquer tela, formulário, tabela, modal, navegação ou feedback, procure o componente PO UI equivalente. Não escreva markup ou CSS que reproduz comportamento que a biblioteca já resolve (foco, teclado, ARIA, validação, i18n).
- Consulte a documentação PO UI otimizada para IA antes de assumir uma API de componente:
  - índice: `https://po-ui.io/llms.txt`;
  - documentação completa: `https://po-ui.io/llms-full.txt`;
  - por componente/serviço/interface/enum: `https://po-ui.io/llms-generated/<slug>.md` (ex.: `po-table.md`, `po-modal.md`, `po-dialog-service.md`).
- Se o servidor MCP `po-ui` estiver configurado (`.vscode/mcp.json`), prefira as ferramentas `list_components`, `search_docs`, `get_component_docs`, `get_guide` e `get_component_examples` a adivinhar props, eventos ou comportamento.
- Se nenhum componente do portfólio PO UI atender à necessidade (interação, layout ou estado sem equivalente pronto), pare a implementação, explicite a lacuna encontrada e pergunte ao responsável antes de criar alternativa. Não presuma aprovação de componente fora do portfólio.
- `design-system` compõe e tematiza componentes PO UI (tokens, copy e wrappers finos para o contrato do Dokpod); não reimplementa comportamento interno do componente.
- Ícones vêm de `po-icon`; não adicione outra biblioteca de ícones.
- Formulários usam componentes `po-*` (ex.: `po-input`, `po-select`, `po-combo`, `po-datepicker`, `po-checkbox`) com Reactive Forms. Tabelas e listas densas usam `po-table`/`po-page-list-detail` conforme volume e paginação. Diálogos usam `po-modal`/`PoDialogService`. Feedback assíncrono usa `PoNotificationService`/`po-toaster`.
- Layout de página segue os page templates PO UI (`po-page-default`, `po-page-list`, `po-page-dynamic-*`) coerentes com a navegação operacional do Dokpod; não crie shell de página do zero quando um template cobrir o caso.
- Personalize tema e densidade por tokens/CSS custom properties do PO UI para refletir a identidade do Dokpod; não sobreponha estilos que quebrem foco, contraste ou estados já resolvidos pela biblioteca.
- Toda tela nova documenta, no pull request, qual(is) componente(s) PO UI foram usados e por quê. Se a resposta for "nenhum equivalente", isso aparece como pergunta explícita ao responsável, não como decisão silenciosa.

Consulte `frontend-angular.instructions.md` e `docs/frontend.md`.
