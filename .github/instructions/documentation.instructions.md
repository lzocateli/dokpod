---
name: "Documentação"
description: "Use ao criar ou alterar README, documentação técnica, runbook, ADR, plano ou contrato documentado."
applyTo: "**/*.md, docs/**, contracts/**/*.yaml, contracts/**/*.json, contracts/**/*.proto"
---

# Documentação

- Escreva em português brasileiro, com termos técnicos consistentes.
- O README raiz é autoridade para visão, escopo e princípios.
- Não duplique conteúdo extenso; use links entre documentos proprietários.
- ADRs vivem em `docs/adr`, usam `AAAA-NNNN-titulo.md` e começam como `proposed`.
- Planos vivem em `docs/plan`, usam kebab-case e status por etapa.
- IA não marca ADR como `accepted`, plano como `approved` ou etapa como `completed` sem evidência humana explícita.
- Runbooks incluem sinais, diagnóstico, mitigação, recuperação e escalonamento.
- Comandos informam diretório, pré-requisitos, resultado esperado e impacto.
- Exemplos usam valores fictícios; nunca inclua secret, host ou dado real.
- Registre limitações, matriz suportada, riscos e validações não executadas.
- Links locais devem resolver e headings devem ser únicos.
- Diferencie requisito, proposta, decisão aceita e comportamento implementado.