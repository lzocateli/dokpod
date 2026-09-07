# ADR 2026-0001: Arquitetura inicial do plano de controle e agentes

**Status:** accepted  
**Data:** 2026-09-06  
**Responsáveis:** Lincoln Zocateli  
**Origem:** humano, com elaboração por IA assistida  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** plano do MVP

## Contexto

O Dokpod precisa operar engines Docker e Podman em hosts Linux e Windows sem expor seus sockets pela rede. A solução deve tolerar desconexões, versões diferentes de agente e repetição de mensagens sem repetir efeitos destrutivos.

## Forças de decisão

- segurança equivalente a uma ferramenta com privilégio administrativo no host;
- backend, BFF, frontend e agente Linux sempre em containers; agente Windows como Worker Service self-contained;
- conectividade a partir de redes privadas e NAT;
- compatibilidade gradual entre servidor e agentes;
- implementação em .NET 10 e Angular 22;
- simplicidade operacional de um produto self-hosted.

## Opções consideradas

### Conexão direta do servidor ao engine

É simples em rede plana, mas exige expor sockets ou SSH, amplia credenciais do plano de controle e não atende bem a redes restritas.

### Proxy genérico no agente

Maximiza compatibilidade com a API do engine, mas transforma qualquer falha de autorização no servidor em execução arbitrária e acopla o contrato público aos engines.

### Agente com comandos de domínio e conexão de saída

Reduz a superfície, atravessa NAT, permite capability discovery e aplica validação próxima ao engine. Exige protocolo próprio, deduplicação e reconciliação.

## Decisão

Adotar monólito modular para o plano de controle e um agente .NET separado, com camadas de aplicação e infraestrutura próprias para cada processo. O agente inicia canal gRPC bidirecional com mTLS e expõe somente comandos de domínio versionados. PostgreSQL mantém comandos, projeções e auditoria; o agente mantém journal durável mínimo e usa fencing de sessão. REST/OpenAPI atende o frontend Angular e SignalR transporta notificações.

Docker e Podman usam adapters independentes. Docker Linux é o primeiro alvo estável; Podman Linux e Docker Windows dependem das provas técnicas documentadas. Keycloak é a autoridade externa de identidade e autorização; o BFF mantém tokens fora do browser e a API aplica as decisões como PEP.

O plano de controle inicia com uma instância de API, adequada à carga de referência de 56 agentes, aproximadamente 1.120 containers e 30 usuários simultâneos. Inventário usa deltas e snapshots de reconciliação; SignalR publica notificações segmentadas, não estado durável. Escala horizontal da API exige ownership de streams por lease e roteamento durável de comandos, definidos em ADR próprio antes da adoção.

Builds, testes e imagens finais Linux usam como baseline as imagens versionadas do projeto `lzocateli/containers`. As versões são uma decisão operacional atualizável em `docs/distribuicao.md`; releases do Dokpod registram digest e origem, sem depender de tags mutáveis.

Esta decisão foi aceita explicitamente pelo responsável pelo produto em 2026-09-06.

## Consequências

### Positivas

- sockets não são expostos pela rede;
- segurança e compatibilidade são modeladas no protocolo;
- indisponibilidade do plano de controle não interrompe workloads;
- monólito modular reduz custo operacional inicial.

### Negativas e trade-offs

- protocolo e atualização dos agentes tornam-se responsabilidades do produto;
- nem toda função do engine estará imediatamente disponível;
- operação at-least-once exige idempotência e reconciliação;
- Windows requer pacote self-contained, instalação, testes e suporte separados.

## Segurança e privacidade

Cada agente possui identidade revogável. Toda mutação é autorizada no servidor, revalidada tecnicamente no agente e auditada. O protocolo não aceita chamadas arbitrárias ao engine. Inventário omite secrets e variáveis por padrão.

## Dados, compatibilidade e migração

O inventário é reconstruível. Contratos evoluem por adição compatível e negociação de capabilities. A janela N/N-1 será testada antes da release. Nenhuma migração de sistema existente é necessária.

## Observabilidade e operação

Medir conexões ativas, idade do inventário, fila, latência e resultado dos comandos, falhas por engine e versão dos agentes. Health checks distinguem processo, banco e prontidão para aceitar agentes.

## Validação

A arquitetura-base é falsificada se não for possível recuperar o canal mTLS sem replay ou reconciliar resposta perdida sem repetir efeito em Docker Linux. Falha no named pipe de Docker Windows ou no Podman rootless reprova somente a respectiva capability e não bloqueia o MVP Docker Linux.

Antes da primeira release, o teste de capacidade deve sustentar 100 agentes, 2.000 containers e 50 usuários simultâneos, acima da carga nominal informada, sem perda de comandos, crescimento ilimitado de filas ou divergência de inventário.

## Rollout e rollback

Liberar primeiro Docker Linux em ambiente de teste, depois Podman Linux e Windows como capacidades separadas. Uma capacidade pode ser desabilitada no servidor sem remover o agente. Rollback preserva comandos e auditoria para reconciliação pela versão anterior compatível.

## Referências

- [Viabilidade técnica](../viabilidade.md)
- [Arquitetura](../arquitetura.md)
- [Distribuição e operação](../distribuicao.md)
- [Docker Engine API](https://docs.docker.com/reference/api/engine/)
- [Podman system service](https://docs.podman.io/en/latest/markdown/podman-system-service.1.html)