param name string
param location string
param elasticUserEmail string
param tags object = {}
param skuName string = 'ess-consumption-2024_Monthly'
param kind string = 'elastic-hosted-deployment'
param monitoringStatus string = 'Enabled'
param offerID string = 'ec-azure-security'
param publisherID string = 'elastic'
param termID string = 'gmz7xq9ge3py'
param planID string = 'ess-consumption-2024'
param version string = '9.0.1'
param generateApiKey bool = true
param hostingType string = 'Hosted'

resource elasticdeployment 'Microsoft.Elastic/monitors@2025-01-15-preview' = {
  name: name
  sku: {
    name: skuName
  }
  kind: kind
  location: location
  tags: tags
  properties: {
    monitoringStatus: monitoringStatus
    elasticProperties: {
      elasticCloudUser: {}
      elasticCloudDeployment: {}
    }
    planDetails: {
      offerID: offerID
      publisherID: publisherID
      termID: termID
      planID: planID
    }
    version: version
    sourceCampaignName: ''
    sourceCampaignId: ''
    generateApiKey: generateApiKey

    hostingType: hostingType
    userInfo: {
      companyInfo: null
      companyName:  null
      emailAddress: elasticUserEmail
    }
  }
}

output ELASTIC_URL string = elasticdeployment.properties.elasticProperties.elasticCloudDeployment.elasticsearchServiceUrl
output ELASTIC_DEPLOYMENT_ID string = elasticdeployment.properties.elasticProperties.elasticCloudDeployment.deploymentId
output ELASTIC_USER_EMAIL string = elasticdeployment.properties.elasticProperties.elasticCloudUser.emailAddress
output ELASTIC_USER_ID string = elasticdeployment.properties.elasticProperties.elasticCloudUser.id

