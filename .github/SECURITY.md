# Política de segurança

## Versões suportadas

O Dokpod ainda não possui release suportada. Esta política será atualizada antes da primeira versão pública.

## Como reportar

Não abra issue pública para vulnerabilidades. Use o recurso privado de Security Advisories do repositório no GitHub. Se ele não estiver habilitado, contate os mantenedores por um canal privado publicado na organização antes de compartilhar detalhes técnicos.

Inclua, quando possível:

- versão ou commit afetado;
- engine e sistema operacional;
- pré-condições e impacto;
- passos mínimos de reprodução;
- mitigação conhecida.

Não envie credenciais reais, chaves privadas, dados de terceiros ou dumps completos.

## Prioridade

Recebem prioridade máxima vulnerabilidades que permitam:

- controlar o socket Docker/Podman ou o host;
- falsificar agente ou servidor;
- executar comando sem autorização ou reproduzi-lo;
- atravessar isolamento entre ambientes;
- obter tokens, certificados, secrets ou logs protegidos;
- comprometer imagens, atualizações ou cadeia de suprimentos.

## Resposta coordenada

Os mantenedores confirmarão o recebimento, avaliarão impacto, prepararão correção e combinarão divulgação. Prazos públicos serão definidos quando a equipe e o canal oficial existirem. Não há programa de recompensa declarado.