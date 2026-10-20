// Skeleton IaC for DocuMind on Azure: Postgres Flexible Server (pgvector), Redis, Blob storage,
// Container Apps (API, worker, frontend) and a migration job. Not deployed or validated against a
// subscription; review SKUs, networking and secrets handling before use. See docs/deployment.md.
targetScope = 'resourceGroup'

@description('Short environment name, e.g. staging or prod')
param environment string = 'staging'
param location string = resourceGroup().location

@description('Container image tags, e.g. ghcr.io/<owner>/documind-ai/api:sha-abc123')
param apiImage string
param frontendImage string

@secure()
param postgresAdminPassword string
@secure()
param sessionSecret string

var prefix = 'documind-${environment}'
var storageName = toLower(replace('dm${environment}${uniqueString(resourceGroup().id)}', '-', ''))

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${prefix}-pg'
  location: location
  sku: { name: 'Standard_D2ds_v5', tier: 'GeneralPurpose' }
  properties: {
    version: '17'
    administratorLogin: 'documind'
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 64 }
  }
}

// pgvector must be allow-listed before CREATE EXTENSION works
resource pgExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: { value: 'vector', source: 'user-override' }
}

resource db 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'documind'
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${prefix}-redis'
  location: location
  properties: {
    sku: { name: 'Basic', family: 'C', capacity: 0 }
    minimumTlsVersion: '1.2'
  }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take(storageName, 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: { allowBlobPublicAccess: false, minimumTlsVersion: 'TLS1_2' }
}

resource containers 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=documind;Username=documind;Password=${postgresAdminPassword};SSL Mode=Require'
var redisConnection = '${redis.properties.hostName}:6380,password=${redis.listKeys().primaryKey},ssl=True'
var blobConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value}'

var commonEnv = [
  { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db' }
  { name: 'Redis__ConnectionString', secretRef: 'redis' }
  { name: 'Storage__Provider', value: 'AzureBlob' }
  { name: 'Storage__AzureBlob__ConnectionString', secretRef: 'blob' }
]
var commonSecrets = [
  { name: 'db', value: connectionString }
  { name: 'redis', value: redisConnection }
  { name: 'blob', value: blobConnection }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-api'
  location: location
  properties: {
    managedEnvironmentId: containers.id
    configuration: {
      ingress: { external: false, targetPort: 8080 }
      secrets: commonSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          env: concat(commonEnv, [{ name: 'Jobs__Mode', value: 'Api' }])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
          ]
        }
      ]
      scale: { minReplicas: 2, maxReplicas: 10, rules: [{ name: 'http', http: { metadata: { concurrentRequests: '50' } } }] }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-worker'
  location: location
  properties: {
    managedEnvironmentId: containers.id
    configuration: { secrets: commonSecrets }
    template: {
      containers: [
        { name: 'worker', image: apiImage, env: concat(commonEnv, [{ name: 'Jobs__Mode', value: 'Worker' }]) }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

// Run before each rollout: az containerapp job start -n <prefix>-migrate
resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: '${prefix}-migrate'
  location: location
  properties: {
    environmentId: containers.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      secrets: commonSecrets
    }
    template: {
      containers: [{ name: 'migrate', image: apiImage, args: ['--migrate'], env: commonEnv }]
    }
  }
}

resource frontend 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${prefix}-frontend'
  location: location
  properties: {
    managedEnvironmentId: containers.id
    configuration: {
      ingress: { external: true, targetPort: 3000 }
      secrets: [{ name: 'session', value: sessionSecret }]
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: frontendImage
          env: [
            { name: 'DOCUMIND_API_URL', value: 'https://${api.properties.configuration.ingress.fqdn}' }
            { name: 'SESSION_SECRET', secretRef: 'session' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 5 }
    }
  }
}

output frontendUrl string = 'https://${frontend.properties.configuration.ingress.fqdn}'
