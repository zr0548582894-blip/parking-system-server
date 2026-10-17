# Starts the whole system locally: PostgreSQL (Docker), the API and the React client.
# No passwords or secrets are stored here. They come from dotnet user-secrets.
# Run from the server repo root:  powershell -ExecutionPolicy Bypass -File scripts\start-local.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$api = Join-Path $root 'ParkingSystem.API'
$client = Join-Path (Split-Path -Parent $root) 'parking-system-client'

Set-Location $root

# 1. database
Write-Host 'Starting PostgreSQL (docker compose)...'
docker compose up -d db
for ($i = 0; $i -lt 30; $i++) {
    $status = docker inspect --format '{{.State.Health.Status}}' parking-system-db 2>$null
    if ($status -eq 'healthy') { break }
    Start-Sleep -Seconds 2
}
if ($status -ne 'healthy') { throw 'PostgreSQL did not become healthy. Is Docker Desktop running?' }

# 2. secrets (a random JWT key is generated once if missing; the demo passwords must be set by you)
$secrets = dotnet user-secrets list --project $api
if (-not ($secrets | Select-String '^Jwt:SecretKey')) {
    Write-Host 'Generating a random JWT key into user-secrets...'
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    dotnet user-secrets set 'Jwt:SecretKey' ([Convert]::ToBase64String($bytes)) --project $api | Out-Null
}
foreach ($name in 'SeedUsers:AdminPassword', 'SeedUsers:EmployeePassword') {
    if (-not ($secrets | Select-String "^$name")) {
        Write-Warning "$name is not set, so that demo user will not be created. See the README (Local run, step 2)."
    }
}

# 3. client dependencies
if (-not (Test-Path (Join-Path $client 'node_modules'))) {
    Write-Host 'Installing client dependencies (npm ci)...'
    Push-Location $client; npm ci; Pop-Location
}

# 4. API and client, each in its own window
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$root'; dotnet run --project ParkingSystem.API --launch-profile http"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$client'; npm run dev"

Write-Host ''
Write-Host 'API:     http://localhost:5258   (Swagger: http://localhost:5258/swagger)'
Write-Host 'Client:  http://localhost:5173'
Write-Host 'Close the two new windows to stop them. Stop the database with: docker compose stop'
