targetScope = 'resourceGroup'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Environment name passed by azd')
param environmentName string

@description('Function App name')
param functionAppName string = 'fa-${environmentName}-${uniqueString(resourceGroup().id)}'

@description('Storage account name (3-24 chars, lowercase alphanumeric)')
param storageAccountName string = 'st${take(toLower(uniqueString(resourceGroup().id, environmentName)), 22)}'

@description('Application Insights resource name')
param applicationInsightsName string = 'appi-${environmentName}'

@description('Existing user-assigned managed identity resource ID. Leave empty to use only system-assigned identity.')
param managedIdentityResourceId string = ''

@description('Managed identity client ID used by code to request token (optional when using system-assigned identity)')
param managedIdentityClientId string = ''

@description('IcM MCP endpoint')
param mcpEndpoint string = 'https://icm-mcp-prod.azure-api.net/v1/'

@description('IcM MCP scope')
param mcpScope string = 'api://icmmcpapi-prod/.default'

@description('Optional IcM app id header')
param icmAppId string = ''

var useUserAssignedIdentity = !empty(managedIdentityResourceId)

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-${environmentName}'
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  kind: 'functionapp'
  properties: {}
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${environmentName}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: useUserAssignedIdentity
    ? {
        type: 'SystemAssigned,UserAssigned'
        userAssignedIdentities: {
          '${managedIdentityResourceId}': {}
        }
      }
    : {
        type: 'SystemAssigned'
      }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'MCP_ENDPOINT'
          value: mcpEndpoint
        }
        {
          name: 'MCP_SCOPE'
          value: mcpScope
        }
        {
          name: 'MANAGED_IDENTITY_CLIENT_ID'
          value: managedIdentityClientId
        }
        {
          name: 'ICM_APP_ID'
          value: icmAppId
        }
      ]
    }
  }
}

output SERVICE_FUNCTIONAPP_NAME string = functionApp.name
output SERVICE_FUNCTIONAPP_URI string = 'https://${functionApp.properties.defaultHostName}'
output AZURE_LOCATION string = location
