# Azure PME deployment guide for CallMcp Function (SAW without .NET SDK)

This guide is for environments where .NET SDK is unavailable in SAW.

- Use `azd` for infrastructure only (`azd provision`).
- Use ZIP deploy (`az functionapp deployment source config-zip`) for function code.

## Prerequisites

- Azure Developer CLI (`azd`) installed
- Azure CLI (`az`) installed
- Permission to deploy to subscription `8b055e41-644a-4d6b-8022-812b0142e2fe`
- Existing resource group: `OSOC-ICM-MCP-RG`
- Built function ZIP package (`functionapp.zip`) created on a machine with .NET SDK

## 1. Sign in and set subscription

```powershell
azd auth login
az login
az account set --subscription 8b055e41-644a-4d6b-8022-812b0142e2fe
az account show --query "{name:name,id:id,tenantId:tenantId}" -o table
```

## 2. Configure azd environment

From repository root:

```powershell
azd env new pme
azd env set AZURE_SUBSCRIPTION_ID 8b055e41-644a-4d6b-8022-812b0142e2fe
azd env set AZURE_RESOURCE_GROUP OSOC-ICM-MCP-RG
azd env set AZURE_LOCATION canadacentral
azd env set FUNCTIONAPPNAME osoc-mcp-functionapp
azd env set MANAGEDIDENTITYRESOURCEID "<uami-resource-id-in-pme-or-empty>"
azd env set MANAGEDIDENTITYCLIENTID "3bc62a4d-a65e-48ed-af39-f70577ab184c"
azd env set ICMAPPID "<optional-icm-app-id-or-empty>"
```

## 3. Provision infrastructure (Bicep only)

```powershell
azd provision
```

## 4. Deploy function code using ZIP package

Copy your prebuilt `functionapp.zip` into SAW, then run:

```powershell
az functionapp deployment source config-zip `
  --name osoc-mcp-functionapp `
  --resource-group OSOC-ICM-MCP-RG `
  --src .\functionapp.zip
```

## 5. Validate deployed function

```powershell
$functionAppName = 'osoc-mcp-functionapp'
$key = az functionapp function keys list `
  --resource-group OSOC-ICM-MCP-RG `
  --name $functionAppName `
  --function-name CallMcp `
  --query default -o tsv

$body = @{ toolName = 'get_ai_summary'; arguments = @{ incidentId = '626495494' } } | ConvertTo-Json -Depth 5
Invoke-RestMethod `
  -Uri "https://$functionAppName.azurewebsites.net/api/CallMcp?code=$key" `
  -Method Post `
  -ContentType 'application/json' `
  -Body $body
```

## Notes

- Do not run `azd deploy` in SAW when .NET SDK is missing.
- Infrastructure files are at `azure.yaml`, `infra/main.bicep`, `infra/main.parameters.json`.
- If `MANAGEDIDENTITYRESOURCEID` is empty, Function App uses system-assigned identity.
- For user-assigned identity, set both `MANAGEDIDENTITYRESOURCEID` and `MANAGEDIDENTITYCLIENTID`.
