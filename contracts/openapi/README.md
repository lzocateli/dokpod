# API HTTP do browser

O arquivo [v1/openapi.yaml](v1/openapi.yaml) e a fonte canônica da API HTTP usada pelo BFF e pelo frontend. Clientes devem ser gerados a partir dele; alterações manuais em código gerado não são permitidas.

Todos os recursos administrativos exigem decisão de autorização do Keycloak. Quando ela não estiver disponível, a API falha fechada com `503 application/problem+json`.
