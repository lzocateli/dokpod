# Critérios de testes de carga e desempenho

Este documento define a evidência mínima para avaliar regressões de desempenho e
capacidade no Dokpod. Os testes usam cargas sintéticas, ambientes reproduzíveis e
a imagem fixada `lzocateli/k6:2.1.0-node24.15.0-bookworm`.

## Objetivos

- verificar que o plano de controle permanece responsivo sob a carga declarada;
- detectar perda, duplicação de efeito, divergência e filas sem limite;
- comparar versões somente em ambientes e cenários equivalentes;
- impedir promoção quando capacidade ou invariantes operacionais regredirem.

## Perfis obrigatórios

| Perfil | Agentes | Containers | Usuários simultâneos | Duração mínima | Uso |
| --- | ---: | ---: | ---: | ---: | --- |
| Smoke | 5 | 100 | 5 | 3 minutos | pull request que altera caminho crítico |
| Nominal | 56 | 1.120 | 30 | 30 minutos | release candidate |
| Capacidade | 100 | 2.000 | 50 | 30 minutos | antes da primeira release e em mudança arquitetural |
| Soak | 56 | 1.120 | 30 | 2 horas | execução periódica e investigação de degradação |

O perfil de capacidade deriva do
[ADR da arquitetura inicial](../docs/adr/2026-0001-arquitetura-inicial.md).
Engine e sistema operacional são dimensões explícitas; aprovação em Docker Linux
não comprova Podman Linux nem Docker Windows.

## Cenários mínimos

- conexão inicial, inventário completo e deltas subsequentes;
- leitura concorrente do inventário por usuários autorizados;
- start, stop e restart com command IDs distintos e repetidos;
- timeout, resposta perdida, reconexão e reconciliação;
- desconexão e reconexão simultânea dos agentes;
- agente revogado, payload inválido e operação não autorizada;
- recuperação do backlog após o pico sem crescimento permanente.

Testes que montam socket ou named pipe de engine executam somente em ambiente
isolado e confiável. Pull requests não confiáveis não recebem acesso privilegiado
ao engine.

## Critérios de aprovação

Os seguintes critérios são bloqueadores em todos os perfis:

- nenhuma operação sem autorização;
- nenhum comando perdido nem efeito mutável duplicado para o mesmo command ID;
- nenhuma divergência persistente entre engine e projeção após reconciliação;
- nenhum crescimento ilimitado de fila, memória, conexões ou tarefas;
- taxa de erro inesperado e latências p95/p99 dentro do baseline aprovado para o cenário;
- regressão de throughput ou latência não superior a 10% contra o baseline comparável.

Cada cenário deve declarar antes da execução seus thresholds absolutos de latência,
taxa de erro e tempo de convergência. Enquanto não existir baseline aprovado para
um cenário, o resultado é evidência exploratória e não autoriza declarar o gate
como aprovado.

## Ambiente e métricas

Registre versão do Dokpod, commit, engine, sistema operacional, CPU, memória,
latência de rede, configuração do banco e gerador de carga. Colete no mínimo:

- latência p50, p95 e p99 por operação;
- throughput e taxa de erro por cenário;
- conexões ativas, reconexões e idade do inventário;
- profundidade e idade do backlog;
- CPU, memória, threads, handles e conexões de banco;
- comandos emitidos, deduplicados, expirados, reconciliados e concluídos.

Não registre tokens, certificados, variáveis de ambiente, conteúdo integral de
logs de containers ou payloads do usuário.

## Evidências e exceções

Preserve resumo, thresholds, resultados brutos sanitizados e identificação do
ambiente. Evidências de pull request seguem a retenção dos artifacts de CI;
evidências de release acompanham a release correspondente.

Exceção temporária exige responsável humano, risco aceito, prazo de expiração e
plano de correção registrados em issue, plano ou ADR. Uma execução não realizada
é `NOT RUN`, nunca aprovação.

Consulte também a [matriz de distribuição](../docs/distribuicao.md) e as
[instruções de testes](instructions/testing.instructions.md).
