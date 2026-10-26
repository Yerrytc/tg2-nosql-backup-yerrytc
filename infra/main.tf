terraform {
  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  backend "azurerm" {
    resource_group_name  = "rg-tg2-yerrytc"
    storage_account_name = "sttg2yerrytc01"
    container_name       = "tfstate"
    key                  = "tg2.tfstate"
  }
}

provider "azurerm" {
  features {}
  resource_provider_registrations = "none"
}

data "azurerm_resource_group" "rg" {
  name = "rg-tg2-yerrytc"
}

resource "azurerm_cosmosdb_account" "db" {
  name                = "cosmos-tg2-yerrytc"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  capabilities {
    name = "EnableServerless"
  }

  consistency_policy {
    consistency_level = "Session"
  }

  geo_location {
    location          = data.azurerm_resource_group.rg.location
    failover_priority = 0
  }

  backup {
    type                = "Periodic"
    interval_in_minutes = 240
    retention_in_hours  = 48
    storage_redundancy  = "Local"
  }
}

resource "azurerm_cosmosdb_sql_database" "productos" {
  name                = "productosdb"
  resource_group_name = data.azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.db.name
}

resource "azurerm_cosmosdb_sql_container" "productos" {
  name                = "productos"
  resource_group_name = data.azurerm_resource_group.rg.name
  account_name        = azurerm_cosmosdb_account.db.name
  database_name       = azurerm_cosmosdb_sql_database.productos.name
  partition_key_paths = ["/id"]
}

resource "azurerm_storage_account" "backups" {
  name                            = "sttg2backupyerry"
  location                        = data.azurerm_resource_group.rg.location
  resource_group_name             = data.azurerm_resource_group.rg.name
  account_tier                    = "Standard"
  account_replication_type        = "LRS"
  min_tls_version                 = "TLS1_2"
  allow_nested_items_to_be_public = false
}

resource "azurerm_storage_container" "backups" {
  name                  = "backups"
  storage_account_id    = azurerm_storage_account.backups.id
  container_access_type = "private"
}

resource "azurerm_storage_management_policy" "retencion" {
  storage_account_id = azurerm_storage_account.backups.id

  rule {
    name    = "borrar-respaldos-de-30-dias"
    enabled = true

    filters {
      prefix_match = ["backups/"]
      blob_types   = ["blockBlob"]
    }

    actions {
      base_blob {
        delete_after_days_since_modification_greater_than = 30
      }
    }
  }
}

resource "azurerm_service_plan" "plan" {
  name                = "plan-tg2-yerrytc"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  os_type             = "Linux"
  sku_name            = "F1"
}

resource "azurerm_linux_web_app" "app" {
  name                = "app-tg2-backup-yerrytc"
  location            = data.azurerm_resource_group.rg.location
  resource_group_name = data.azurerm_resource_group.rg.name
  service_plan_id     = azurerm_service_plan.plan.id
  https_only          = true

  site_config {
    always_on = false

    application_stack {
      dotnet_version = "10.0"
    }
  }

  app_settings = {
    Cosmos__Endpoint  = azurerm_cosmosdb_account.db.endpoint
    Cosmos__Key       = azurerm_cosmosdb_account.db.primary_key
    Cosmos__Database  = azurerm_cosmosdb_sql_database.productos.name
    Cosmos__Container = azurerm_cosmosdb_sql_container.productos.name
  }
}

output "app_url" {
  value = "https://${azurerm_linux_web_app.app.default_hostname}"
}

output "cosmos_endpoint" {
  value = azurerm_cosmosdb_account.db.endpoint
}

output "almacen_respaldos" {
  value = azurerm_storage_account.backups.name
}
