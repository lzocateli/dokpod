# Detecção de secrets

O Dokpod bloqueia secrets em duas camadas complementares: um hook local para o
diff staged e o check obrigatório `Gitleaks / Full History` para todo o histórico
Git alcançável no runner. Ambos executam exclusivamente a imagem fixada
`lzocateli/gitleaks:8.30.1` por Docker.

## Instalação local obrigatória

Na raiz de cada clone, com PowerShell 7, Git e Docker disponíveis:

```powershell
./tools/scripts/install-gitleaks-hook.ps1
git config --local --get core.hooksPath
git hook run pre-commit
```

O segundo comando deve retornar `.githooks`, e o smoke test deve terminar com
`no leaks found`. A configuração é local ao clone e deve ser refeita após cada
novo clone. Não ignore o hook com `--no-verify`.

## Comportamento dos controles

O hook envia `git diff --cached --no-ext-diff --no-textconv` ao Gitleaks por
`stdin`. Ele não lê arquivos fora do índice e falha fechado quando Docker não
está disponível.

O workflow faz checkout com histórico completo e usa o Git do runner para enviar
`git log --all --full-history -p --` ao mesmo scanner por `stdin`. A imagem é
minimalista e não precisa conter o executável `git`.

## Tratamento de achados

1. Interrompa o commit ou merge e não publique o valor encontrado.
2. Revogue ou rotacione imediatamente qualquer credencial real.
3. Remova o secret da árvore e, quando necessário, reconstrua o histórico.
4. Reexecute o hook e o check de histórico completo.
5. Trate falso positivo somente por exceção mínima, documentada e revisada.

Não adicione allowlists automáticas nem exceções amplas. A mera remoção do
arquivo no commit atual não elimina um secret já presente no histórico.

## Checklist administrativo

- [ ] Exigir `Gitleaks / Full History` nos rulesets de `main` e `development`.
- [ ] Habilitar secret scanning e push protection nativos do GitHub quando disponíveis.
- [ ] Confirmar `core.hooksPath=.githooks` nos clones administrativos.
- [ ] Revisar e registrar qualquer exceção antes de alterar a configuração do scanner.

As proteções nativas do GitHub complementam e não substituem o hook e o workflow.
