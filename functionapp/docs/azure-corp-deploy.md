# Azure Corp deployment guide for CallMcp Function

This guide deploys the Function App and configures Managed Identity to call IcM MCP.

## Prerequisites

- Azure CLI logged in to your corporate tenant
- Access to the target subscription/resource group
- A user-assigned managed identity (recommended) with permission to request tokens for `api://icmmcpapi-prod/.default`

## 1. Set deployment variables
$miClientId = "3bc62a4d-a65e-48ed-af39-f70577ab184c"
# Azure PME deployment guide for CallMcp Function (azd)

This guide deploys the Function App with azd and configures Managed Identity to call IcM MCP.

## Prerequisites

- Azure Developer CLI (`azd`) installed
- Azure CLI (`az`) installed
- Permission to deploy to subscription `8b055e41-644a-4d6b-8022-812b0142e2fe`
- Existing resource group: `OSOC-ICM-MCP-RG`
- User-assigned managed identity resource ID in PME (if using UAMI)

## 1. Sign in and set subscription

```powershell
azd auth login
az login
az account set --subscription 8b055e41-644a-4d6b-8022-812b0142e2fe
az account show --query "{name:name,id:id,tenantId:tenantId}" -o table
```

## 2. Configure azd environment

From repository root (`C:\Code\MCP-MI`):

```powershell
azd env new pme
azd env set AZURE_SUBSCRIPTION_ID 8b055e41-644a-4d6b-8022-812b0142e2fe
azd env set AZURE_RESOURCE_GROUP OSOC-ICM-MCP-RG
azd env set AZURE_LOCATION canadacentral
```

Set required parameter values for infrastructure:

```powershell
azd env set MANAGEDIDENTITYRESOURCEID "<uami-resource-id-in-pme-or-empty>"
azd env set MANAGEDIDENTITYCLIENTID "3bc62a4d-a65e-48ed-af39-f70577ab184c"
azd env set ICMAPPID "<optional-icm-app-id-or-empty>"
```

## 3. Provision Azure infrastructure

```powershell
azd provision
```

What this creates (via `infra/main.bicep`):

- Storage account
- App Service consumption plan
- Log Analytics workspace
- Application Insights
- Function App with managed identity + app settings for MCP

## 4. Deploy function code

```powershell
azd deploy
```

## 5. Validate deployment

Get function host name and function key:

```powershell
$functionAppName = azd env get-value SERVICE_FUNCTIONAPP_NAME
$key = az functionapp function keys list `
  --resource-group OSOC-ICM-MCP-RG `
  --name $functionAppName `
  --function-name CallMcp `
  --query default -o tsv
```

Call endpoint:

```powershell
$body = @{ toolName = "get_ai_summary"; arguments = @{ incidentId = "626495494" } } | ConvertTo-Json -Depth 5
Invoke-RestMethod `
  -Uri "https://$functionAppName.azurewebsites.net/api/CallMcp?code=$key" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

## Notes

- Infrastructure uses files at repo root: `azure.yaml`, `infra/main.bicep`, `infra/main.parameters.json`.
- If `MANAGEDIDENTITYRESOURCEID` is empty, the Function App uses system-assigned identity.
- For your PME user-assigned identity, set both:
  - `MANAGEDIDENTITYRESOURCEID`
  - `MANAGEDIDENTITYCLIENTID`
- The function normalizes tool names with prefix `mcp_icm-mcp_` automatically.
  --function-name CallMcp `
