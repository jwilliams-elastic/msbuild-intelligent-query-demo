targetScope = 'resourceGroup'
param environmentName string = 'dev'
param location string
param elasticUserEmail string
param aoaiAccount string = 'aoai'
param azureMapsAccountName string = 'azuremaps'
param aoaiCapacity int = 50
param aoiaDeployment string = 'gpt-4o'
param aoiaModel string = 'gpt-4o'
param aoiaModelVersion string = '2024-11-20'
param containerAppStackName string = 'containerapps'
param containerAppsEnvironmentName string = 'containersenv'
param applicationInsightsName string = 'appinsghts'
param logAnalyticsName string = 'loganalytics'
param applicationInsightsDashboardName string = 'dash'
param containerRegistryName string = 'acr'
param appName string = 'app'
param appContainerName string = 'appcontainer'
param appExists bool = false
param aoaiAccountExists bool = false
param appIdentityName string = 'appidentity'
param elasticName string = 'elasticsearch'

// Tags that should be applied to all resources.
// 
// Note that 'azd-service-name' tags should be applied separately to service host resources.
// Example usage:
//   tags: union(tags, { 'azd-service-name': <service name in azure.yaml> })
var tags = {
  'azd-env-name': environmentName
}

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))

resource azureMaps 'Microsoft.Maps/accounts@2024-07-01-preview' = {
  kind: 'Gen2'
  sku: {
    name: 'G2'
  }
  properties: {
    disableLocalAuth: false
    cors: {
      corsRules: [
        {
          allowedOrigins: []
        }
      ]
    }
    locations: []
  }
  name: '${azureMapsAccountName}${resourceToken}'
  location: 'eastus' //hard coded because there are few options available
  identity: {
    type: 'None'
  }
}
resource azureOpenAI 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (!aoaiAccountExists) {
  name: '${aoaiAccount}${resourceToken}'
  location: location
  sku: {
    name: 'S0'
  }
  kind: 'OpenAI'
  tags: tags
  properties: {
    networkAcls: {
      defaultAction: 'Allow'
      virtualNetworkRules: []
      ipRules: []
    }
    publicNetworkAccess: 'Enabled'
    customSubDomainName: '${aoaiAccount}${resourceToken}'
  }
}

resource azureOpenAI4oModelDeploymentNew 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: azureOpenAI
  name: aoiaDeployment
  tags: tags
  properties: {
    model: {
        format: 'OpenAI'
        name: aoiaModel
        version: aoiaModelVersion
      }
    versionUpgradeOption: 'OnceCurrentVersionExpired'
    currentCapacity: aoaiCapacity
  }
  sku: {
    name: 'GlobalStandard'
    capacity: aoaiCapacity
  }
}
module monitoring 'br/public:avm/ptn/azd/monitoring:0.1.0' = {
  name: 'monitoring'
  params: {
    applicationInsightsName: '${applicationInsightsName}${resourceToken}'
    logAnalyticsName: '${logAnalyticsName}${resourceToken}'
    applicationInsightsDashboardName: '${applicationInsightsDashboardName}${resourceToken}'
    location: location
    tags: tags
  }
}

module containerAppsStack 'br/public:avm/ptn/azd/container-apps-stack:0.1.1' = {
  name: '${containerAppStackName}-${resourceToken}'
  params: {
    tags: tags
    containerAppsEnvironmentName: '${containerAppsEnvironmentName}${resourceToken}'
    containerRegistryName: '${containerRegistryName}${resourceToken}'
    logAnalyticsWorkspaceResourceId: monitoring.outputs.logAnalyticsWorkspaceResourceId    // Non-required parameters
    appInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    acrSku: 'Basic'
    location: location
    acrAdminUserEnabled: true
    zoneRedundant: false
  }
}

module appIdentity 'br/public:avm/res/managed-identity/user-assigned-identity:0.2.1' = {
  name: appIdentityName
  params: {
    name: 'useridentityapp${resourceToken}'
    location: location
  }
}

module app 'br/public:avm/ptn/azd/container-app-upsert:0.1.1' = {
  name:  '${appName}-${resourceToken}'
  params: {
    name: '${appContainerName}${resourceToken}'
    tags: union(tags, { 'azd-service-name': 'app' })
    location: location
    containerAppsEnvironmentName: containerAppsStack.outputs.environmentName
    containerRegistryName: containerAppsStack.outputs.registryName
    ingressEnabled: true
    targetPort: 8501
    identityType: 'UserAssigned'
    identityName: appIdentity.name
    userAssignedIdentityResourceId: appIdentity.outputs.resourceId
    identityPrincipalId: appIdentity.outputs.principalId
    exists: appExists
    containerName: 'main'
    containerMinReplicas: 1
    env: [
      {
        name: 'AZURE_OPENAI_ENDPOINT'
        value: azureOpenAI.properties.endpoint
      }
      {
        name: 'AZURE_OPENAI_KEY'
        value: listKeys(azureOpenAI.id, '2022-12-01').key1
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: monitoring.outputs.applicationInsightsConnectionString
      }
      {
        name: 'AZURE_CLIENT_ID'
        value: appIdentity.outputs.clientId
      }
    ]
  }
}


resource azureOpenAIUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(azureOpenAI.id, 'Cognitive Services OpenAI User', resourceToken)
  scope: azureOpenAI
  properties: {
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd') // SQL DB Contributor
    principalId: appIdentity.outputs.principalId
  }
}


module elastic 'elastic.bicep' = {
  name: 'elastic-module'
  params: {
    name: '${elasticName}${resourceToken}'
    location: location
    elasticUserEmail: elasticUserEmail
    tags: tags
  }
}

output AZURE_OPENAI_ENDPOINT string = azureOpenAI.properties.endpoint
output AZURE_OPENAI_API_KEY string = azureOpenAI.listKeys('2022-12-01').key1
output AZURE_OPENAI_DEPLOYMENT_NAME string = azureOpenAI4oModelDeploymentNew.name
output AZURE_OPENAI_API_VERSION string = azureOpenAI4oModelDeploymentNew.properties.model.version
output APPLICATIONINSIGHTS_CONNECTION_STRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATIONINSIGHTS_NAME string = monitoring.outputs.applicationInsightsName
output AZURE_CONTAINER_ENVIRONMENT_NAME string = containerAppsStack.outputs.environmentName
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = containerAppsStack.outputs.registryLoginServer
output AZURE_CONTAINER_REGISTRY_NAME string = containerAppsStack.outputs.registryName
output AZURE_LOCATION string = location
output APP_URL string = app.outputs.uri
output ELASTIC_URL string = elastic.outputs.ELASTIC_URL
output AZURE_MAPS_API_KEY string = azureMaps.listKeys().primaryKey
