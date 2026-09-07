---
name: "Segurança Dokpod"
description: "Use em identidade, autorização, agentes, engines, secrets, logs, proxy, dependências e deployment."
applyTo: "backend/**, frontend/**, contracts/**, deploy/**"
---

# Segurança

- Trate acesso ao socket Docker/Podman como privilégio administrativo do host.
- Autorize antes de revelar ambiente, container, nome, label ou estado.
- Keycloak é a autoridade externa para usuários, credenciais, MFA, sessões, roles, recursos, scopes, políticas e decisões.
- Use realm `dokpod`; nunca use `master` como realm da aplicação nem conceda administração global ao runtime.
- O BFF usa Authorization Code com PKCE e mantém tokens fora do browser; a API aplica decisões como PEP.
- Não implemente cadastro, senha, recuperação, membership ou política no Dokpod.
- Agentes usam identidade individual por mTLS e nunca recebem token de usuário.
- Bootstrap é de uso único, curto, armazenado como hash e vinculado ao ambiente e à chave pública.
- O servidor deriva o ambiente do certificado, valida EKU/revogação e impede sessões concorrentes por fencing.
- Valide issuer, audience, assinatura, expiração, revogação e recurso.
- Use allowlist de comandos; nunca aceite método, URL ou payload bruto do engine.
- Proteja contra replay com journal durável, ID/hash imutável, deadline, alvo por ID/revisão e fencing.
- Proteja contra SSRF, redirects, confused deputy, command injection e autorização horizontal.
- Faça escaping de nomes, labels e logs; nunca os trate como HTML confiável.
- Secrets vêm de provider seguro ou arquivo montado, nunca de Git ou imagem.
- Logs e traces usam IDs técnicos e redaction; não coletam env vars ou logs integrais por padrão.
- Dependências exigem licença permissiva, manutenção, vulnerabilidades e proveniência verificadas.
- Releases do agente são aceitas somente após verificar assinatura, provenance, emissor e digest não revogado.
- O Worker Service Windows usa conta dedicada, ACL mínima e dados persistentes separados do binário.
- Nova fronteira ou privilégio exige threat model e testes negativos.
- Achado crítico ou alto bloqueia release sem aceitação formal de risco.

Consulte `docs/seguranca.md`.