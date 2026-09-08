# Segredos locais

Segredos de desenvolvimento do Dokpod ficam exclusivamente fora do repositório, em `%APPDATA%\Microsoft\UserSecrets\dokpod\.env` no Windows. Nunca crie, copie, mova ou versione arquivo `.env` na árvore do projeto.

O arquivo local contém as seguintes chaves:

- `DOKPOD_KEYCLOAK_DB_PASSWORD`;
- `DOKPOD_KEYCLOAK_ADMIN_USERNAME`;
- `DOKPOD_KEYCLOAK_ADMIN_PASSWORD`;
- `DOKPOD_KEYCLOAK_HOSTNAME`;
- `DOKPOD_BFF_CLIENT_SECRET`;
- `DOKPOD_PROVISIONER_CLIENT_SECRET`.

Preencha as senhas e secrets diretamente no arquivo local. Não os transmita por argumentos de processo, logs, issues, pull requests, imagens ou arquivos versionados.

Para executar o laboratório Keycloak:

```powershell
docker compose --env-file "$env:APPDATA\Microsoft\UserSecrets\dokpod\.env" --file deploy/keycloak/compose.yaml up -d --wait
```

Consulte [Segurança](../docs/seguranca.md) e o [laboratório Keycloak](../deploy/keycloak/README.md).
