# ADR 2026-0002: Distribuição por plataforma e identidade com Keycloak

**Status:** accepted  
**Data:** 2026-09-06  
**Responsáveis:** Lincoln Zocateli  
**Origem:** humano  
**Revisor humano:** Lincoln Zocateli  
**Relacionado:** ADR 2026-0001 e plano do MVP

## Contexto

O plano de controle precisa ter implantação reproduzível e não depender de runtimes instalados no servidor. O agente Linux integra-se naturalmente ao engine por socket montado em container. No Windows, exigir Windows container acrescenta compatibilidade entre host e imagem sem ser necessário para cumprir o isolamento operacional. A autenticação e autorização também precisam de uma autoridade externa única.

## Forças de decisão

- operação self-hosted reproduzível;
- ausência de runtime .NET como pré-requisito no host Windows;
- acesso seguro ao Unix socket ou named pipe local;
- identidade federada, MFA, SSO e autorização por ambiente;
- tokens fora do browser e falha fechada na API;
- estrutura de monorepo compatível com o padrão validado no AltivyNotes.

## Opções consideradas

### Agente em container em todas as plataformas

Uniformiza a distribuição, mas aumenta a complexidade e a matriz de compatibilidade no Windows.

### Serviço nativo em todas as plataformas

Simplifica Windows, mas abandona a distribuição containerizada definida para Linux e amplia requisitos no host.

### Distribuição específica por plataforma

Mantém agente Linux em container e publica o agente Windows como Worker Service self-contained, compartilhando contratos e núcleo de aplicação.

### Identidade implementada no Dokpod

Reduz dependência externa, mas cria responsabilidade indevida sobre senhas, MFA, sessões e políticas.

### Keycloak externo com BFF e API como PEP

Centraliza identidade e autorização, mantém tokens fora do browser e permite políticas por ambiente e scope.

## Decisão

- Backend, BFF e frontend são sempre executados em containers.
- O agente Linux é sempre executado em container.
- O agente Windows é um Worker Service .NET 10 publicado self-contained e instalado como Windows Service, sem runtime .NET pré-instalado.
- O monorepo segue a organização geral do AltivyNotes, com hosts em `backend/apps`, bibliotecas em `backend/libs`, frontend, contratos, testes e deployment separados.
- Keycloak é a plataforma externa obrigatória para autenticação e decisão de autorização de usuários.
- O BFF é cliente OIDC confidencial; a API é o Policy Enforcement Point.
- A identidade técnica dos agentes permanece independente, por mTLS e certificado individual por ambiente.

Esta decisão foi definida explicitamente pelo responsável pelo produto em 2026-09-06.

## Consequências

### Positivas

- plano de controle reproduzível e isolado por containers;
- instalação Windows sem dependência do runtime .NET;
- menor acoplamento às limitações de Windows containers;
- identidade, MFA, sessões e políticas não são reimplementadas no Dokpod;
- frontend não recebe access ou refresh tokens.

### Negativas e trade-offs

- duas formas de empacotamento e atualização do agente;
- operação exige Keycloak e backup separado de seu banco;
- serviço Windows exige instalador, assinatura de código, ACL e rollback próprios;
- indisponibilidade do Keycloak impede decisões novas de autorização.

## Segurança e privacidade

O realm do Dokpod é separado do `master`. BFF, API e integração administrativa usam clients distintos e menor privilégio. A API autoriza antes de revelar IDs ou metadados. O Worker Service usa conta dedicada, binário assinado e diretório protegido; seus certificados e journal não ficam no diretório da aplicação.

## Dados, protocolo e compatibilidade

Keycloak e Dokpod usam bancos, credenciais, backups e ciclos de vida separados. O PostgreSQL do Dokpod pode guardar apenas o `sub` necessário à auditoria, sem copiar senhas, memberships ou políticas. Linux e Windows usam o mesmo protocolo agente-servidor e a janela N/N-1.

## Observabilidade e operação

Monitorar login/callback/logout do BFF, decisões e indisponibilidade do Keycloak, certificados dos agentes, versão do pacote Windows e estado do Windows Service. Logs não contêm tokens, cookies, bootstrap tokens ou certificados privados.

## Validação

- API nega acesso e não revela recurso sem decisão válida do Keycloak;
- browser não recebe tokens OIDC;
- indisponibilidade do Keycloak falha fechada;
- backend e frontend executam somente pelas imagens publicadas;
- agente Linux opera pela imagem OCI;
- agente Windows instala, inicia, atualiza e reverte em host sem runtime .NET.

## Rollout e rollback

Implantar primeiro Keycloak, PostgreSQL, API, BFF e web em containers. Qualificar o agente Linux antes do pacote Windows. Rollback do plano de controle preserva compatibilidade N/N-1; rollback Windows restaura o pacote assinado anterior e preserva identidade/journal.

## Referências

- [Arquitetura](../arquitetura.md)
- [Backend e agente](../backend.md)
- [Segurança](../seguranca.md)
- [Configuração do Keycloak](../configuracao-keycloak.md)