$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$backupDir = Join-Path $root "backups"
New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$out = Join-Path $backupDir "paybot-$stamp.sql"

$dump = @( & docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d paybot --no-owner --no-acl' )
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump falhou com codigo $LASTEXITCODE"
}

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllLines($out, $dump, $utf8)

if (-not (Test-Path $out) -or (Get-Item $out).Length -eq 0) {
    throw "pg_dump nao gerou o arquivo $out"
}

Write-Host "Backup salvo em $out"
