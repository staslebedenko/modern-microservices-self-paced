# modern-microservices
modern microservices workshop

## Steps
The workshop is build around many steps.

0. Deployment of required Azure infrastructure, databases for step one and two, along with Container App environment and AKS
1. Preparing monolith for split and splitting it into two services with containerization.
2. Overview of the existing Azure resources and deployment of solution from Visual Studio and GitHub
3. Basics of Container Apps
4. Adding DAPR to the Container App and explanation of approaches and benefits
5. Create AKS manifest, setting up DAPR in Azure Kubernetes cluster and deploying solution to Cloud.
6. Adding DAPR pubsub component and RabbitMQ container. Changing solution code to work with a pubsub.

## Prerequisites

1. Visual Studio or Visual Studio Code with .NET Framework 6.
2. Docker Desktop to run the containerized application locally.
https://www.docker.com/products/docker-desktop
3. DAPR CLI installed on a local machine.
https://docs.dapr.io/getting-started/install-dapr-cli/
4. AZ CLI tools installation(for cloud deployment)
https://aka.ms/installazurecliwindows
5. Azure subscription, if you want to deploy applications to Kubernetes(AKS).
https://azure.microsoft.com/en-us/free/
6. Kubectl installation https://kubernetes.io/docs/tasks/tools/install-kubectl-windows/#install-kubectl-binary-with-curl-on-windows
7. Good mood :)

(optional)Kompose tool for Kubernetes manifest generation.
https://kompose.io/getting-started/ 

## Step 0. Azure infrastructure
Script below should be run via Azure Portal bash console. 
You will receive database connection strings with setx command as output of this script. Along with Application Insights key
Please add a correct name of your subscription to the first row of the script. 

As result of this deployemt you should open the local command line as admin and execute output strings from the script execution to set environment variables.
It is also good to store them in the text file for the future usage

You might need to reboot your PC so secrets will be available from the OS.

For the start the preferrable way is to use Azue CLI bash console via Azure portal.

```bash
subscriptionID=$(az account list --query "[?contains(name,'Microsoft')].[id]" -o tsv)
echo "Test subscription ID is = " $subscriptionID
az account set --subscription $subscriptionID
az account show

location=northeurope
postfix=$RANDOM

#----------------------------------------------------------------------------------
# Database infrastructure
#----------------------------------------------------------------------------------

export dbResourceGroup=dcc-modern-data$postfix
export dbServername=dcc-modern-sql$postfix
export dbPoolname=dbpool
export dbAdminlogin=FancyUser3
export dbAdminpassword=Sup3rStr0ng52$postfix
export dbPaperName=paperorders
export dbDeliveryName=deliveries

az group create --name $dbResourceGroup --location $location

az sql server create --resource-group $dbResourceGroup --name $dbServername --location $location \
--admin-user $dbAdminlogin --admin-password $dbAdminpassword
	
az sql elastic-pool create --resource-group $dbResourceGroup --server $dbServername --name $dbPoolname \
--edition Standard --dtu 50 --zone-redundant false --db-dtu-max 50

az sql db create --resource-group $dbResourceGroup --server $dbServername --elastic-pool $dbPoolname \
--name $dbPaperName --catalog-collation SQL_Latin1_General_CP1_CI_AS
	
az sql db create --resource-group $dbResourceGroup --server $dbServername --elastic-pool $dbPoolname \
--name $dbDeliveryName --catalog-collation SQL_Latin1_General_CP1_CI_AS	

sqlClientType=ado.net

SqlPaperString=$(az sql db show-connection-string --name $dbPaperName --server $dbServername --client $sqlClientType --output tsv)
SqlPaperString=${SqlPaperString/Password=<password>;}
SqlPaperString=${SqlPaperString/<username>/$dbAdminlogin}

SqlDeliveryString=$(az sql db show-connection-string --name $dbDeliveryName --server $dbServername --client $sqlClientType --output tsv)
SqlDeliveryString=${SqlDeliveryString/Password=<password>;}
SqlDeliveryString=${SqlDeliveryString/<username>/$dbAdminlogin}

SqlPaperPassword=$dbAdminpassword

#----------------------------------------------------------------------------------
# AKS infrastructure
#----------------------------------------------------------------------------------

location=northeurope
groupName=dcc-modern-cluster$postfix
clusterName=dcc-modern-cluster$postfix
registryName=dccmodernregistry$postfix


az group create --name $groupName --location $location

az acr create --resource-group $groupName --name $registryName --sku Standard
az acr identity assign --identities [system] --name $registryName

az aks create --resource-group $groupName --name $clusterName --node-count 3 --generate-ssh-keys --network-plugin azure
az aks update --resource-group $groupName --name $clusterName --attach-acr $registryName

#----------------------------------------------------------------------------------
# Service bus queue
#----------------------------------------------------------------------------------

groupName=dcc-modern-extras$postfix
location=northeurope
az group create --name $groupName --location $location
namespaceName=dccModern$postfix
queueName=createdelivery

az servicebus namespace create --resource-group $groupName --name $namespaceName --location $location
az servicebus queue create --resource-group $groupName --name $queueName --namespace-name $namespaceName

serviceBusString=$(az servicebus namespace authorization-rule keys list --resource-group $groupName --namespace-name $namespaceName --name RootManageSharedAccessKey --query primaryConnectionString --output tsv)

#----------------------------------------------------------------------------------
# Application insights
#----------------------------------------------------------------------------------

insightsName=dccmodernlogs$postfix
az monitor app-insights component create --resource-group $groupName --app $insightsName --location $location --kind web --application-type web --retention-time 120

instrumentationKey=$(az monitor app-insights component show --resource-group $groupName --app $insightsName --query  "instrumentationKey" --output tsv)

#----------------------------------------------------------------------------------
# Azure Container Apps
#----------------------------------------------------------------------------------

az extension add --name containerapp --upgrade

az provider register --namespace Microsoft.App

az provider register --namespace Microsoft.OperationalInsights

acaGroupName=dcc-modern-containerapp$postfix
location=northeurope
logAnalyticsWorkspace=dcc-modern-logs$postfix
containerAppsEnv=dcc-environment$postfix

az group create --name $acaGroupName --location $location

az monitor log-analytics workspace create \
--resource-group $acaGroupName --workspace-name $logAnalyticsWorkspace

logAnalyticsWorkspaceClientId=`az monitor log-analytics workspace show --query customerId -g $acaGroupName -n $logAnalyticsWorkspace -o tsv | tr -d '[:space:]'`

logAnalyticsWorkspaceClientSecret=`az monitor log-analytics workspace get-shared-keys --query primarySharedKey -g $acaGroupName -n $logAnalyticsWorkspace -o tsv | tr -d '[:space:]'`

az containerapp env create \
--name $containerAppsEnv \
--resource-group $acaGroupName \
--logs-workspace-id $logAnalyticsWorkspaceClientId \
--logs-workspace-key $logAnalyticsWorkspaceClientSecret \
--dapr-instrumentation-key $instrumentationKey \
--logs-destination log-analytics \
--location $location

az containerapp env show --resource-group $acaGroupName --name $containerAppsEnv

# we don't need a section below for this workshop, but you can use it later
# use command below to fill credentials values if you want to use section below 
#az acr credential show --name $registryName 

#imageName=<CONTAINER_IMAGE_NAME>
#acrServer=<REGISTRY_SERVER>
#acrUser=<REGISTRY_USERNAME>
#acrPassword=<REGISTRY_PASSWORD>

#az containerapp create \
#  --name my-container-app \
#  --resource-group $acaGroupName \
#  --image $imageName \
#  --environment $containerAppsEnv \
#  --registry-server $acrServer \
#  --registry-username $acrUser \
#  --registry-password $acrPassword

#----------------------------------------------------------------------------------
# Azure Key Vault with secrets assignment and access setup
#----------------------------------------------------------------------------------

keyvaultName=dcc-modern$postfix
principalName=vaultadmin
principalCertName=vaultadmincert

az keyvault create --resource-group $groupName --name $keyvaultName --location $location
az keyvault secret set --name SqlPaperPassword --vault-name $keyvaultName --value $SqlPaperPassword

az ad sp create-for-rbac --name $principalName --create-cert --cert $principalCertName --keyvault $keyvaultName --skip-assignment --years 3

# get appId from output of step above and add it after --id in command below.

# az ad sp show --id 474f817c-7eba-4656-ae09-979a4bc8d844
# get object Id (located before info object) from command output above and set it to command below 

# az keyvault set-policy --name $keyvaultName --object-id f1d1a707-1356-4fb8-841b-98e1d9557b05 --secret-permissions get
#----------------------------------------------------------------------------------
# SQL connection strings
#----------------------------------------------------------------------------------

printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlPaperString:\nsetx SqlPaperString \"$SqlPaperString\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlDeliveryString:\nsetx SqlDeliveryString \"$SqlDeliveryString\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlPaperPassword:\nsetx SqlPaperPassword \"$SqlPaperPassword\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable SqlDeliveryPassword:\nsetx SqlDeliveryPassword \"$SqlPaperPassword\"\n\n"
printf "\n\nRun string below in local cmd prompt to assign secret to environment variable ServiceBusString:\nsetx ServiceBusString \"$serviceBusString\"\n\n"

echo "Update open-telemetry-collector-appinsights.yaml in Step 5 End => <INSTRUMENTATION-KEY> value with:  " $instrumentationKey
```

## Step 1. Monolith split and containerization
The Start repository contains approach for splitting monolithic app into parts inside the same solution and single database with two schemas

The End folder contains solution with the one solution and two projects.

First we adding docker containerization via context menu of each project.

Then we adding orchestration support via docker compose again to the each project

Following by adding the environment variable file to the root folder

And adding to the Order controller the method to call the Delivery container endpoint

We will also configure the Compose startup via Visual Studio so the Paper project endpoint will start as a primary starting point.

All changes available in the commit history, so it is easy to track them.

Don't forget to add env file with a secrets content along with changes to docker-compose.yaml in the root folder.

Solution will work with two containers, so there is a need to put the correct container port for Delivery service.

!! Be aware, if you have docker build exceptions in Visual studio with errors related to the File system, there is a need to configure docker desktop. 
Open Docker desktop => configuration => Resources => File sharing => Add your project folder or entire drive, C:\ for example. Dont forget to remove drive setting later on.

When you try to start the same solution from the new folder, you need to stop and delete containers via docker compose.

## Step 2. Azure infrastructure, deployment and GitHub actions

We will start this step with the overview of the infrastructure deployed in Azure

Changes to the secrets to get a proper secrets for our application
Addition of application insights nuget and custom logging
Changes to URLs

Deployment to Container Apps via Visual studio
Configuration for GitHub actions deployments

## Step 3. Basics of Container Apps 


## Step 4. Adding DAPR in the loop


## Step 5.


## Step 6.


## Step 7.
