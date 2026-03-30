$ErrorActionPreference = "Stop"

# =========================
# Config - update if needed
# =========================
$SubscriptionId = "8b055e41-644a-4d6b-8022-812b0142e2fe"
$ResourceGroup = "OSOC-ICM-MCP-RG"
$Location = "canadacentral"
$FunctionAppName = "osoc-mcp-functionapp"
$EnvName = "pme"

# Leave empty if system-assigned MI only
$ManagedIdentityResourceId = "osoc-mcp-functionapp-uami"
$ManagedIdentityClientId = "27183a69-6e6f-4226-ab16-099abb5c8e8d"
$IcmAppId = ""

# Input package files in current folder
$AzdZip = ".\mcp-mi-azd-saw-minimal-v1.zip"
$CodeZip = ".\functionapp.zip"

# Working folder
$WorkRoot = ".\mcp-mi"

Write-Host "==[1/8] Validate inputs==" -ForegroundColor Cyan
if (!(Test-Path $AzdZip)) { throw "Missing $AzdZip" }
if (!(Test-Path $CodeZip)) { throw "Missing $CodeZip" }

Write-Host "==[2/8] Extract azd package==" -ForegroundColor Cyan
if (Test-Path $WorkRoot) {
    Remove-Item $WorkRoot -Recurse -Force
}
Expand-Archive $AzdZip -DestinationPath $WorkRoot -Force
Set-Location $WorkRoot

if (!(Test-Path ".\azure.yaml")) { throw "azure.yaml not found after extract" }
if (!(Test-Path ".\infra\main.bicep")) { throw "infra\main.bicep not found after extract" }

Write-Host "==[3/8] Authenticate==" -ForegroundColor Cyan
azd auth login
az login | Out-Null

Write-Host "==[4/8] Set subscription==" -ForegroundColor Cyan
az account set --subscription $SubscriptionId
az account show --query "{name:name,id:id,tenantId:tenantId,user:user.name}" -o table

Write-Host "==[5/8] Configure azd environment==" -ForegroundColor Cyan
$envList = azd env list 2>$null
if ($LASTEXITCODE -ne 0 -or -not ($envList -match "^\s*$EnvName\s")) {
    azd env new $EnvName
} else {
    azd env select $EnvName
}

azd env set AZURE_SUBSCRIPTION_ID $SubscriptionId
azd env set AZURE_RESOURCE_GROUP $ResourceGroup
azd env set AZURE_LOCATION $Location
azd env set FUNCTIONAPPNAME $FunctionAppName
azd env set MANAGEDIDENTITYRESOURCEID $ManagedIdentityResourceId
azd env set MANAGEDIDENTITYCLIENTID $ManagedIdentityClientId
azd env set ICMAPPID $IcmAppId

Write-Host "==[6/8] Provision infra (Bicep only)==" -ForegroundColor Cyan
azd provision

Write-Host "==[7/8] Deploy function code ZIP==" -ForegroundColor Cyan
# Use original path from parent folder
$CodeZipFullPath = Resolve-Path "..\functionapp.zip"
az functionapp deployment source config-zip `
  --name $FunctionAppName `
  --resource-group $ResourceGroup `
  --src $CodeZipFullPath

Write-Host "==[8/8] Validate endpoint==" -ForegroundColor Cyan
$key = az functionapp function keys list `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --function-name CallMcp `
  --query default -o tsv

if ([string]::IsNullOrWhiteSpace($key)) {
    throw "Could not retrieve function key"
}

$body = @{ toolName = "get_ai_summary"; arguments = @{ incidentId = "626495494" } } | ConvertTo-Json -Depth 5
$uri = "https://$FunctionAppName.azurewebsites.net/api/CallMcp?code=$key"

$result = Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json" -Body $body
$result | ConvertTo-Json -Depth 8

Write-Host "Deployment and validation completed successfully." -ForegroundColor Green