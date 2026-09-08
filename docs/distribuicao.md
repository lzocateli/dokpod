# Distribuição e operação

## Princípio

O plano de controle e o frontend do Dokpod sempre executam em containers. A única distribuição nativa prevista é o agente Windows, publicado como Worker Service self-contained para não exigir runtime .NET instalado no host.

## Matriz de artefatos

| Componente | Artefato | Execução |
| --- | --- | --- |
| Web Angular | imagem OCI Linux com assets estáticos e proxy | container |
| BFF ASP.NET Core | imagem OCI Linux | container |
| API ASP.NET Core | imagem OCI Linux | container |
| Agente Linux | imagem OCI Linux | container com socket local explícito |
| Agente Windows | pacote self-contained assinado por RID | Windows Service |
| Keycloak | imagem fixada por versão e digest | container ou serviço externo homologado |
| PostgreSQL | imagem fixada por versão e digest ou serviço externo homologado | banco do Dokpod separado do banco do Keycloak |

Build, testes integrados e execução local do backend/frontend usam toolchains e serviços containerizados. O fluxo oficial não depende de SDK .NET, Node.js, Angular CLI ou pnpm instalados globalmente no host.

## Imagens base e toolchains

O baseline inicial usa imagens mantidas no projeto público [`lzocateli/containers`](https://github.com/lzocateli/containers). Dockerfiles do Dokpod referenciam a tag exata abaixo durante desenvolvimento e recebem o digest aprovado como argumento de build na release. O manifesto da release registra nome, tag e digest efetivamente utilizados; `latest` é proibida.

| Uso no Dokpod | Imagem de referência | Papel |
| --- | --- | --- |
| restore, build, testes e publicação .NET | `lzocateli/dotnet-sdk:10.0.400-noble` | estágio de build da API, BFF e agentes |
| runtime da API, BFF e agente Linux | `lzocateli/dotnet-aspnet:10.0.11-noble` | base dos artefatos ASP.NET Core e do host do agente |
| comandos oficiais do frontend | `lzocateli/angular-cli:22.1.0-node24.15.0-bookworm` | install, lint, typecheck, testes e build Angular |
| toolchain JavaScript de apoio | `lzocateli/node:24.15.0-bookworm` | base do Angular CLI e tarefas Node sem Angular CLI |
| runtime do web e proxy público | `lzocateli/nginx:1.28.0-bookworm` | base da imagem final com assets Angular e configuração Dokpod |
| testes de carga | `lzocateli/k6:2.1.0-node24.15.0-bookworm` | cenários HTTP, SignalR e capacidade do plano de controle |
| testes E2E black-box | `lzocateli/playwright-e2e:0.1.0` | pytest/Playwright contra o deployment containerizado |
| detecção de secrets | `lzocateli/gitleaks:8.30.1` | workspace, diff e histórico no CI |
| identidade | `lzocateli/keycloak:26.7.0` | serviço Keycloak homologado pelo Dokpod |
| persistência | `lzocateli/postgresql:18.4-pgvector0.8.6-bookworm` | mesma imagem adotada pelo AltivyNotes, para instâncias independentes do Dokpod e Keycloak |

A imagem PostgreSQL inclui pgvector 0.8.6, mas o Dokpod não habilita nem depende dessa extensão. API, BFF, agente Linux e web produzem imagens próprias `dokpod/*`; as imagens acima são bases, toolchains ou serviços de infraestrutura, não substitutos dos artefatos do produto.

Versões desta tabela podem receber atualização de patch sem novo ADR quando contratos, major versions e fronteiras permanecem iguais. A atualização exige build, SBOM, scan, testes do recorte, compatibilidade e atualização desta tabela. Mudança de major version, troca de distribuição base ou substituição de tecnologia exige revisão arquitetural proporcional.

## Plano de controle containerizado

- API, BFF e web possuem Dockerfiles independentes e multi-stage.
- Os estágios .NET usam `dotnet-sdk` para build e `dotnet-aspnet` para runtime; o estágio Angular usa `angular-cli`, e a imagem final web usa `nginx` sem carregar Node.js ou Angular CLI.
- Containers executam com usuário não root, filesystem read-only e capabilities removidas quando tecnicamente possível.
- A imagem final web substitui a configuração genérica do NGINX, escuta em porta não privilegiada e prepara PID, cache e temporários para o UID não root do Dokpod.
- Somente o proxy público recebe tráfego externo; API, BFF, Keycloak e bancos usam redes internas conforme a topologia.
- Secrets entram por arquivo montado ou provider seguro, nunca por camada de imagem ou argumento de build.
- Health checks distinguem startup, liveness e readiness.
- Imagens são fixadas por digest na release, possuem labels OCI, SBOM, provenance e assinatura.
- Dados PostgreSQL e Keycloak possuem volumes, credenciais, backups e testes de restauração independentes.

## Agente Linux

O agente Linux é uma imagem OCI separada do plano de controle. O operador monta somente o socket do engine selecionado e um volume persistente para identidade e journal.

Diretórios lógicos:

```text
/app                         # binário imutável
/var/lib/dokpod-agent        # certificado, chave, journal e estado
/run/docker.sock             # exemplo Docker; configurável e validado
```

O socket nunca é publicado em porta TCP nem compartilhado com outro componente Dokpod. Podman rootless exige UID, socket e regras SELinux documentados para a distribuição homologada.

## Agente Windows

O projeto `backend/apps/agent` produz um Worker Service .NET 10. Cada release Windows é publicada com `--self-contained true` para um RID homologado, inicialmente candidato a `win-x64`. Não é necessário instalar runtime ou SDK .NET no servidor.

Separação de diretórios recomendada:

```text
%ProgramFiles%\Dokpod\Agent\       # binários versionados e somente leitura
%ProgramData%\Dokpod\Agent\        # configuração, certificados, chave e journal
%ProgramData%\Dokpod\Agent\logs\   # logs limitados e sem secrets
```

O serviço usa uma conta dedicada com menor privilégio. A matriz de suporte deve comprovar qual principal consegue abrir o named pipe do Docker. Usar `LocalSystem` somente quando tecnicamente indispensável, com risco documentado e aprovação explícita.

O pacote não contém bootstrap token, chave, certificado, hostname real ou credencial. A inscrição recebe o token por canal protegido e o elimina após consumo.

## Instalação e atualização Windows

O mecanismo exato de empacotamento será escolhido após o spike, mas deve cumprir:

1. verificar assinatura, hash, provenance e versão antes de instalar;
2. instalar binários em diretório versionado sem sobrescrever identidade ou journal;
3. registrar Windows Service com startup automático atrasado e recuperação limitada;
4. validar configuração, ACL, named pipe e conectividade antes de marcar readiness;
5. parar o serviço com timeout e shutdown gracioso;
6. ativar a nova versão por troca atômica;
7. restaurar a versão anterior sem reemitir identidade se o health check falhar;
8. preservar logs e evidência de auditoria da atualização.

Desinstalação remove serviço e binários. Dados e identidade só são removidos por opção explícita, pois sua exclusão exige novo enrollment.

## Configuração

Configuração não secreta pode vir de arquivo montado no Linux ou arquivo protegido no Windows. Secrets e chaves privadas usam arquivos com permissões mínimas ou provider seguro. Variáveis de ambiente são aceitas somente quando o ambiente operacional impedir exposição por inspeção de processo e houver justificativa.

Campos mínimos:

- URL canônica do plano de controle;
- caminho ou identificador do socket/named pipe permitido;
- engine esperado;
- diretório persistente;
- limites de concorrência, timeout e tamanho;
- autoridade/certificado confiável para mTLS.

## Gates de release

- build reproduzível das quatro imagens e do pacote Windows;
- bases e toolchains correspondem à matriz publicada, sem `latest`, e o manifesto da release contém seus digests;
- execução do backend/frontend apenas por containers;
- smoke test do agente Linux contra engine real;
- instalação do agente Windows em host limpo sem runtime .NET;
- teste de conta de serviço, ACL, reinício, recuperação, atualização e rollback;
- geração e verificação de SBOM, provenance, assinatura e vulnerabilidades;
- compatibilidade servidor N com agentes N e N-1;
- backup e restauração dos bancos do Dokpod e Keycloak.