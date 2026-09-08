# Política de segurança

> **Canal em preparação:** o GitHub Private Vulnerability Reporting deve ser
> habilitado antes da primeira divulgação pública. Até sua confirmação, não
> publique detalhes técnicos de vulnerabilidades em issues ou discussões.

## Versões suportadas

O Dokpod ainda não possui release suportada. Antes da primeira versão pública,
esta seção será substituída por uma matriz de versões e datas de fim de suporte.

## Como reportar

Não abra issue pública para vulnerabilidades. Use **Report a vulnerability** na
aba **Security** do repositório. Se o recurso não estiver disponível, aguarde a
publicação de um canal privado oficial; não envie detalhes a endereços inferidos
nem por comentários públicos.

Inclua, quando possível:

- versão ou commit afetado;
- engine e sistema operacional;
- pré-condições e impacto;
- passos mínimos de reprodução;
- prova de conceito sanitizada;
- mitigação conhecida.

Não envie credenciais reais, chaves privadas, dados de terceiros ou dumps completos.

Não acesse dados de terceiros, não mantenha persistência e não execute testes
destrutivos fora de ambientes sob seu controle.

## Resposta esperada

- confirmação inicial em até 3 dias úteis;
- triagem e classificação em até 7 dias úteis;
- comunicação coordenada da correção conforme impacto e complexidade;
- crédito ao pesquisador quando solicitado e permitido.

Os prazos são objetivos operacionais, não garantia contratual.

## Prioridade

Recebem prioridade máxima vulnerabilidades que permitam:

- controlar o socket Docker/Podman ou o host;
- falsificar agente ou servidor;
- executar comando sem autorização ou reproduzi-lo;
- atravessar isolamento entre ambientes;
- obter tokens, certificados, secrets ou logs protegidos;
- comprometer imagens, atualizações ou cadeia de suprimentos.

## Resposta coordenada

Correções incluem teste de regressão, análise de variantes e atualização de
dependências, imagens, SBOM e orientação de mitigação quando aplicável. A
divulgação ocorre depois da disponibilização da correção e de prazo razoável para
atualização, salvo exploração ativa que exija comunicação imediata.

Não há programa de recompensa declarado.
