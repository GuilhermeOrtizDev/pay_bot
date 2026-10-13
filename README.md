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

`OWNER_NUMBER` e o seu numero com DDI, so digitos. Mensagens suas (a instancia e o seu WhatsApp) e desse numero sao aceitas. O resto do grupo e ignorado.

## CI/CD

Push na `main` (ou execucao manual do workflow) testa, gera a imagem `linux/arm64` e sobe na EC2 por SSH. Secrets do repositorio, os mesmos do microservices:

- `SSH_HOST`
- `SSH_USER`
- `SSH_PRIVATE_KEY`
- `SSH_PORT` (opcional, padrao 22)

Antes do primeiro deploy, crie `/www/paybot/.env` na EC2 (copie de `.env.example`). O pipeline nao envia esse arquivo.

## Backup

Com a stack no ar:

```powershell
./scripts/backup.ps1
```

O arquivo `backups/paybot-*.sql` fica no host. Copie esse arquivo para outro lugar se quiser uma copia fora desta maquina.
