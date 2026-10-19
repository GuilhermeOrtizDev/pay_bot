# PayBot

Bot de financas pessoais no WhatsApp. A Evolution recebe a mensagem do grupo e chama o PayBot, que conduz um menu e grava os lancamentos no Postgres.

Parar os containers nao apaga o banco. Os dados ficam no volume `postgres_data`. Eles so somem com `docker compose down -v` ou se o disco for perdido. O backup copia o database `paybot` para `./backups`.

## Subir

```powershell
copy .env.example .env
docker compose up -d --build
```

Edite `.env` antes: senha do Postgres, `EVOLUTION_API_KEY`, e depois o `GROUP_JID`.

A porta 80 do host e a Evolution. Postgres e a API ficam so na rede interna.

## Conectar o WhatsApp

Crie a instancia (uma vez):

```powershell
curl.exe -X POST http://localhost/instance/create -H "apikey: SUA_CHAVE" -H "Content-Type: application/json" -d '{"instanceName":"paybot","integration":"WHATSAPP-BAILEYS","qrcode":true}'
```

Leia o QR:

```powershell
curl.exe http://localhost/instance/connect/paybot -H "apikey: SUA_CHAVE"
```

A resposta traz o QR em base64. Escaneie no WhatsApp ligado a essa instancia. A sessao fica no volume `evolution_instances`.

Liste os grupos e copie o JID (`...@g.us`) para `GROUP_JID` no `.env`. Depois recrie o PayBot para ele ler o valor:

```powershell
curl.exe "http://localhost/group/fetchAllGroups/paybot?getParticipants=false" -H "apikey: SUA_CHAVE"
docker compose up -d paybot
```

O `GROUP_JID` so serve para cadastrar o primeiro grupo. Na subida, se esse JID ainda nao estiver no banco, ele e vinculado ao tenant `Principal`. Depois disso o bot le os grupos do banco.

## Tenants e grupos

Cada tenant tem seus gastos, entradas e cartoes. Um tenant pode ter varios grupos, e cada grupo pertence a um tenant so. Mensagens de grupo sem tenant sao ignoradas.

Para administrar, envie `*000` em um grupo que ja tenha tenant. A opcao nao aparece no menu e nao funciona no Modo IA (saia com `desativar IA` antes).

- `1 - Cadastrar tenant`: informe o nome.
- `2 - Cadastrar grupo`: informe o JID (`...@g.us`). O grupo fica sem tenant ate ser vinculado.
- `3 - Vincular grupo a tenant`: escolha o grupo na lista e depois o tenant. Se o grupo ja tinha tenant, ele passa para o novo.

`OWNER_NUMBER` e o seu numero com DDI, so digitos. Mensagens suas (a instancia e o seu WhatsApp) e desse numero sao aceitas. O resto do grupo e ignorado.

## Modo IA

Com `AI_ENABLED=true` e `OPENAI_API_KEY` no `.env`, o menu ganha a opcao `8 - Modo IA`. Nesse modo o menu numerico fica desligado: escreva em texto livre ("gastei 32,50 no mercado no pix", "resumo do mes em pdf", "apagar o mercado de ontem"). A IA pede o que faltar e o bot so grava ou apaga depois de `sim`. Para sair, envie `desativar IA`.

Com `AI_TRANSCRIBE_ENABLED=true`, audios enviados no Modo IA sao transcritos e tratados como texto. Fora do Modo IA, audio e ignorado.

Depois de mudar o `.env`, recrie o PayBot: `docker compose up -d paybot`.

## CI/CD

Publicar uma release com tag `v*` (por exemplo `v1.0.0`), ou executar o workflow manualmente, testa, gera a imagem `linux/arm64` e sobe na EC2 por SSH. Push na `main` nao dispara o deploy. Secrets do repositorio, os mesmos do microservices:

- `SSH_HOST`
- `SSH_USER`
- `SSH_PRIVATE_KEY`
- `SSH_PORT` (opcional, padrao 22)

Antes do primeiro deploy, crie `/www/.env` na EC2 (copie de `.env.example`). O pipeline nao envia esse arquivo. O `docker-compose.yml` fica em `/www/docker-compose.yml`.

## Backup

Com a stack no ar:

```powershell
./scripts/backup.ps1
```

O arquivo `backups/paybot-*.sql` fica no host. Copie esse arquivo para outro lugar se quiser uma copia fora desta maquina.
