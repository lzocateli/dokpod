# Segurança do Dokpod

## Ativos e fronteiras

Os ativos críticos são controle dos hosts, identidade de agentes e usuários, credenciais de registry, configuração, trilha de auditoria e metadados de workloads. A fronteira de maior risco é o agente com acesso ao socket Docker ou Podman.

## Ameaças prioritárias

| Ameaça | Controle mínimo |
| --- | --- |
| agente falso | inscrição de uso único, aprovação e mTLS por ambiente |
| servidor falso | agente confia apenas na CA configurada e valida hostname |
| replay de comando | ID idempotente, nonce/revisão, expiração e registro durável |
| usuário opera ambiente indevido | autorização por ação e recurso antes de revelar dados |
| proxy vira execução arbitrária | comandos allowlist e DTOs estritos, sem proxy genérico |
| SSRF via configuração de endpoint | agente acessa socket local conhecido; egress do servidor restrito |
| comprometimento do agente | imagem mínima, assinatura, SBOM, patching e escopo reduzido |
| vazamento em logs | redaction, limites e proibição de secrets/corpo por padrão |
| ação destrutiva acidental | confirmação, escopo explícito e auditoria |
| supply chain | versões fixadas, proveniência, scan e licença verificada |

## Identidade

Keycloak é a autoridade externa obrigatória para usuários, credenciais, MFA, federação, SSO, sessões, grupos, roles, recursos, scopes, políticas e decisões de autorização. O Dokpod não implementa cadastro, senha, recuperação de conta, diretório de usuários, membership ou política própria. O realm `dokpod` é separado do realm administrativo `master`.

O BFF usa OIDC/OAuth 2.0 Authorization Code com PKCE como cliente confidencial. O browser recebe somente cookie `Secure`, `HttpOnly` e `SameSite` apropriado; access e refresh tokens ficam no servidor. Operações mutáveis usam proteção CSRF e conexões SignalR validam Origin. A API atua como PEP, valida token e aplica a decisão deny-by-default do Keycloak antes de listar, detalhar, operar, auditar ou assinar notificações de um ambiente.

Cada ambiente usa recurso opaco no Keycloak e scopes explícitos como `environment:read`, `container:start`, `container:stop`, `container:restart`, `container:delete` e `audit:read`. A integração que registra recursos é idempotente, reconciliável, auditável e usa credencial de serviço com menor privilégio. Decisão ausente, inválida, expirada ou indisponível falha fechada; memberships e políticas não são copiadas para o PostgreSQL do Dokpod.

Agentes não usam identidade de usuário. Cada ambiente possui certificado próprio, revogável e rotacionável, persistido em volume protegido. O servidor deriva o ambiente do certificado e valida cadeia, EKU, expiração e revogação com falha fechada. Tokens de bootstrap são aleatórios, armazenados como hash, vinculados ao ambiente e à chave pública, consumidos transacionalmente e nunca aparecem em argumentos ou logs. A aprovação mostra o fingerprint; revogação encerra streams ativos.

## Engine local

A posse do socket Docker costuma equivaler a root no host. A API Podman concede execução arbitrária como o usuário do serviço. Marcar um mount como read-only não transforma uma API mutável em somente leitura.

O agente deve:

- abrir somente o socket configurado;
- validar esquema e path permitidos;
- limitar corpo, headers, duração e concorrência;
- converter comandos para chamadas conhecidas;
- nunca aceitar URL, método ou payload bruto enviados pelo browser;
- rejeitar URI absoluta, path injection, redirects e headers não permitidos antes do socket;
- recusar operação fora das capacidades anunciadas;
- eliminar secrets de erros antes do reporte.

No Windows, o startup valida named pipe, ACL, conta do serviço, RID e versão do engine contra a matriz suportada. Pipe alternativo ou permissões mais amplas que a política são recusados. O executável self-contained é assinado; chaves, certificados e journal ficam em diretório persistente separado e protegido.

## Dados e auditoria

Inventário contém metadados potencialmente sensíveis. Variáveis de ambiente, conteúdo de secrets, arquivos e logs integrais não são coletados no MVP. Labels e nomes são tratados como não confiáveis e escapados na interface.

A telemetria usa allowlist de campos, limites e normalização de caracteres de controle. A auditoria é append-only e registra identidade, ação, recurso, horário, correlation ID e resultado. A role normal da aplicação não recebe `UPDATE` ou `DELETE` sobre auditoria; integridade e retenção serão definidas antes de produção. Acesso à auditoria também exige autorização.

## Desenvolvimento e release

- threat model atualizado para cada nova fronteira;
- testes negativos de autenticação, autorização, replay, SSRF e elevação;
- análise estática, dependências, secrets, SBOM e imagens em CI;
- imagens executadas como usuário não root quando compatível com o socket;
- instalação verifica assinatura, provenance e identidade do workflow; digest ausente, emissor incorreto ou artefato revogado é rejeitado;
- nenhuma credencial em Git, camada de imagem, variável pública ou exemplo;
- achado crítico ou alto bloqueia release até correção ou aceitação formal.

## Divulgação de vulnerabilidades

Consulte a [política de segurança](../.github/SECURITY.md). Não abra issue pública com exploit, credencial ou detalhe que facilite abuso antes da correção coordenada.

Consulte também a [configuração do Keycloak](configuracao-keycloak.md).