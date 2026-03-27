$ErrorActionPreference = "Stop"

# PME deployment settings
$SubscriptionId = "8b055e41-644a-4d6b-8022-812b0142e2fe"
$ResourceGroup = "OSOC-ICM-MCP-RG"
$FunctionAppName = "osoc-mcp-functionapp"
$ZipFile = "$HOME/functionapp-cloudshell.zip"
$FunctionName = "CallMcp"

Write-Host "[1/8] Setting Azure subscription..." -ForegroundColor Cyan
az account set --subscription $SubscriptionId

Write-Host "[2/8] Verifying resource group..." -ForegroundColor Cyan
az group show --name $ResourceGroup --query "{name:name, location:location, provisioningState:properties.provisioningState}" --output table

Write-Host "[3/8] Verifying function app..." -ForegroundColor Cyan
az functionapp show --name $FunctionAppName --resource-group $ResourceGroup --query "{name:name, state:state, defaultHostName:defaultHostName, location:location}" --output table

Write-Host "[4/8] Deploying zip package..." -ForegroundColor Cyan
az functionapp deployment source config-zip `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --src $ZipFile

Write-Host "[5/8] Restarting function app..." -ForegroundColor Cyan
az functionapp restart --name $FunctionAppName --resource-group $ResourceGroup

Write-Host "[6/8] Checking app state..." -ForegroundColor Cyan
az functionapp show --name $FunctionAppName --resource-group $ResourceGroup --query "state" -o tsv

Write-Host "[7/8] Getting function key for smoke test..." -ForegroundColor Cyan
$FunctionKey = az functionapp function keys list `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --function-name $FunctionName `
  --query default -o tsv

if ([string]::IsNullOrWhiteSpace($FunctionKey)) {
  throw "Could not retrieve function key for '$FunctionName'."
}

Write-Host "[8/8] Running curl smoke test..." -ForegroundColor Cyan
$Url = "https://$FunctionAppName.azurewebsites.net/api/$FunctionName?code=$FunctionKey"
$RequestBody = '{"toolName":"get_ai_summary","arguments":{"incidentId":"626495494"}}'
$ResponseFile = Join-Path $HOME "function-smoke-response.json"

$HttpCode = & curl -sS -o $ResponseFile -w "%{http_code}" `
  -X POST "$Url" `
  -H "Content-Type: application/json" `
  -d "$RequestBody"

$HttpCodeInt = [int]$HttpCode
if ($HttpCodeInt -lt 200 -or $HttpCodeInt -ge 300) {
  Write-Host "Smoke test failed. Response body:" -ForegroundColor Yellow
  Get-Content $ResponseFile -ErrorAction SilentlyContinue
  throw "Function smoke test failed with HTTP status $HttpCodeInt"
}

Write-Host "Smoke test passed with HTTP status $HttpCodeInt" -ForegroundColor Green
Get-Content $ResponseFile -ErrorAction SilentlyContinue

Write-Host "Deployment script completed." -ForegroundColor Green
