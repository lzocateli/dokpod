# Protocolo do agente

O arquivo [`v1/agent.proto`](v1/agent.proto) é a fonte canônica do canal operacional gRPC entre o plano de controle e os agentes.

## Invariantes

- O transporte usa HTTP/2 com mTLS e o ambiente é derivado do certificado, nunca do payload.
- A primeira mensagem do agente é `AgentHello`; o servidor responde com `SessionEstablished` ou encerra o stream.
- Mensagens posteriores carregam versão, sessão, fencing token, sequência monotônica, timestamp UTC e correlation ID.
- O protocolo aceita somente operações de domínio enumeradas. Não transporta método, URI, header, token de usuário, variável de ambiente ou payload bruto do engine.
- Comandos são identificados por ID opaco, hash imutável, prazo e revisão esperada do container.
- `COMMAND_RESULT_STATE_INDETERMINATE` exige reconciliação; timeout ou desconexão não provam ausência de efeito.
- Valores de enum desconhecidos são rejeitados no processamento, sem encerrar o processo do agente.

## Compatibilidade

A versão inicial é `v1`. Servidor e agente negociam versões por `supported_protocol_versions`; a janela suportada é N/N-1. Campos novos são aditivos. Tags removidas devem ser reservadas e nunca reutilizadas.

## Códigos de falha iniciais

| Código | Significado |
| --- | --- |
| `unsupported_command` | comando ou capability não suportada |
| `expired_command` | deadline vencido antes da execução |
| `stale_session` | fencing token pertence a uma sessão antiga |
| `conflicting_payload` | ID já associado a outro hash |
| `stale_target` | revisão observada do container diverge da esperada |
| `target_not_found` | ID do container não existe no engine |
| `engine_unavailable` | engine local indisponível |
| `operation_failed` | engine rejeitou ou falhou ao executar a intenção |

Os códigos são estáveis para automação; detalhes operacionais ficam em logs estruturados redigidos e não atravessam o contrato como stack trace ou resposta bruta do engine.