# Azure PME deployment guide for CallMcp Function (azd-only in SAW)

This guide is for environments where .NET SDK is unavailable in SAW and only `azd` is allowed.

- Use `azd provision` for infrastructure.
- Use `azd deploy --from-package` for function code package upload.

## Prerequisites

- Azure Developer CLI (`azd`) installed
- No direct Azure CLI (`az`) commands required in SAW
- Permission to deploy to subscription `8b055e41-644a-4d6b-8022-812b0142e2fe`
- Existing resource group: `OSOC-ICM-MCP-RG`
- Built function ZIP package (`functionapp.zip`) created on a machine with .NET SDK

## 1. Sign in with azd

```powershell
azd auth login
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

## 4. Deploy function code using azd package deploy

Copy your prebuilt `functionapp.zip` into SAW, then run:

```powershell
azd deploy functionapp --from-package .\functionapp.zip
```

## 5. Validate deployed function

```powershell
# Get function app endpoint from azd outputs
$functionAppUri = azd env get-value SERVICE_FUNCTIONAPP_URI
Write-Host "Function host: $functionAppUri"

# If your function auth level is Function, call validation from a machine that can retrieve keys
# or temporarily switch auth level for non-production test.
```

## Notes

- Use `azd deploy functionapp --from-package` when .NET SDK is missing.
- Infrastructure files are at `azure.yaml`, `infra/main.bicep`, `infra/main.parameters.json`.
- If `MANAGEDIDENTITYRESOURCEID` is empty, Function App uses system-assigned identity.
- For user-assigned identity, set both `MANAGEDIDENTITYRESOURCEID` and `MANAGEDIDENTITYCLIENTID`.
