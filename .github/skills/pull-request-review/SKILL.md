---
name: pull-request-review
description: "Executa revisão rigorosa de branch, diff ou pull request do Dokpod. Use para code review, segurança, arquitetura, OpenAPI/Protobuf, agentes, engines, migrations, testes e risco operacional."
argument-hint: "PR, branch, diff e requisito relacionado"
---

# Pull Request Review

## 1. Preparar contexto

1. Leia requisito, issue, ADR e documentação citados.
2. Determine base/head e leia o diff completo, inclusive contratos, migrations e arquivos gerados.
3. Liste áreas afetadas: frontend, backend, protocolo, engines, dados, segurança, deployment e docs.
4. Carregue as instruções correspondentes.

## 2. Entender comportamento

1. Trace call sites e consumidores alterados entre browser, BFF, API, agente e engine.
2. Compare comportamento anterior e proposto.
3. Identifique invariantes de autorização, identidade, comandos, reconciliação e auditoria.
4. Verifique compatibilidade de API, Protobuf N/N-1, schema, configuração, imagem e plataforma.

## 3. Revisar por dimensão

- **Correção:** critérios completos; erros, cancelamento, concorrência, idempotência e bordas tratados.
- **Arquitetura:** dependências respeitam limites; regras estão no módulo proprietário; complexidade nova é justificada.
- **Segurança:** autorização precede revelação; mTLS, allowlists, sockets, secrets e operações destrutivas estão protegidos.
- **Dados/operação:** migrations são compatíveis; projeções reconciliam; auditoria, health, rollout e rollback existem.
- **Engines/protocolo:** capabilities não presumem equivalência; tags não são reutilizadas; replay e reconexão são seguros.
- **Frontend:** contrato, estado, UX de erro, acessibilidade e responsividade permanecem alinhados.
- **Testes:** cada risco tem evidência no nível correto; engines e PostgreSQL reais são usados quando necessário.

## 4. Confirmar e relatar

Um achado deve ter localização, cenário válido, impacto e correção plausível. Execute validação estreita quando ela puder confirmar o risco. Não relate estilo coberto por formatter/linter nem preferência sem violação concreta.

Liste achados por `Crítica`, `Alta`, `Média`, `Baixa`, seguidos de dúvidas, lacunas de teste, risco residual e resumo curto. Se não houver achados, declare explicitamente e indique o que não foi validado.