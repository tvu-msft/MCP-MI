$ErrorActionPreference = "Stop"

# PME deployment settings
$SubscriptionId = "8b055e41-644a-4d6b-8022-812b0142e2fe"
$ResourceGroup = "OSOC-ICM-MCP-RG"
$FunctionAppName = "osoc-mcp-functionapp"
$ZipFile = "$HOME/functionapp-cloudshell.zip"

Write-Host "[1/6] Setting Azure subscription..." -ForegroundColor Cyan
az account set --subscription $SubscriptionId

Write-Host "[2/6] Verifying resource group..." -ForegroundColor Cyan
az group show --name $ResourceGroup --output table

Write-Host "[3/6] Verifying function app..." -ForegroundColor Cyan
az functionapp show --name $FunctionAppName --resource-group $ResourceGroup --output table

Write-Host "[4/6] Deploying zip package..." -ForegroundColor Cyan
az functionapp deployment source config-zip `
  --resource-group $ResourceGroup `
  --name $FunctionAppName `
  --src $ZipFile

Write-Host "[5/6] Restarting function app..." -ForegroundColor Cyan
az functionapp restart --name $FunctionAppName --resource-group $ResourceGroup

Write-Host "[6/6] Checking app state..." -ForegroundColor Cyan
az functionapp show --name $FunctionAppName --resource-group $ResourceGroup --query "state" -o tsv

Write-Host "Deployment script completed." -ForegroundColor Green
