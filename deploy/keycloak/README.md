# Laboratório Keycloak do Dokpod

Este diretório inicia Keycloak e PostgreSQL isolados para desenvolvimento. O realm importado é `dokpod`, sem usuários, credenciais de clients ou secrets versionados.

Os secrets locais ficam exclusivamente em `%APPDATA%\Microsoft\UserSecrets\dokpod\.env` no Windows. O arquivo já contém as chaves necessárias; preencha `DOKPOD_KEYCLOAK_DB_PASSWORD` e `DOKPOD_KEYCLOAK_ADMIN_PASSWORD` com valores locais distintos. Nunca crie, copie ou versione arquivos `.env` dentro do repositório.

Execute a partir deste diretório:

```powershell
docker compose --env-file "$env:APPDATA\Microsoft\UserSecrets\dokpod\.env" config --quiet
docker compose --env-file "$env:APPDATA\Microsoft\UserSecrets\dokpod\.env" up -d --wait
```

Os serviços não publicam portas no host. Conecte BFF e API pela rede `dokpod-keycloak-lab_keycloak-internal`; exponha Keycloak somente por proxy HTTPS configurado, com hostname canônico e redirects exatos. Para limpar os dados locais do laboratório, execute `docker compose --env-file "$env:APPDATA\Microsoft\UserSecrets\dokpod\.env" down` e remova `./.data` explicitamente.

Consulte [configuração do Keycloak](../../docs/configuracao-keycloak.md) para o contrato de identidade e autorização.
