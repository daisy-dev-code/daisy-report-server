-- Power BI Integration Tables

CREATE TABLE IF NOT EXISTS RS_POWERBI_CONFIG (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    tenant_id VARCHAR(100) NOT NULL,
    client_id VARCHAR(100) NOT NULL,
    client_secret_encrypted TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT 1,
    last_sync_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_WORKSPACE (
    id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(500) NOT NULL,
    description TEXT NULL,
    type VARCHAR(50) NULL,
    state VARCHAR(50) NULL,
    is_read_only BOOLEAN NOT NULL DEFAULT 0,
    is_on_dedicated_capacity BOOLEAN NOT NULL DEFAULT 0,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_REPORT (
    id VARCHAR(100) PRIMARY KEY,
    workspace_id VARCHAR(100) NOT NULL,
    name VARCHAR(500) NOT NULL,
    description TEXT NULL,
    web_url TEXT NULL,
    embed_url TEXT NULL,
    dataset_id VARCHAR(100) NULL,
    report_type VARCHAR(50) NULL,
    created_date_time DATETIME NULL,
    modified_date_time DATETIME NULL,
    local_report_id BIGINT NULL,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_workspace (workspace_id),
    INDEX idx_dataset (dataset_id),
    INDEX idx_local_report (local_report_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_DATASET (
    id VARCHAR(100) PRIMARY KEY,
    workspace_id VARCHAR(100) NOT NULL,
    name VARCHAR(500) NOT NULL,
    web_url TEXT NULL,
    is_refreshable BOOLEAN NOT NULL DEFAULT 0,
    is_effective_identity_required BOOLEAN NOT NULL DEFAULT 0,
    configured_by VARCHAR(200) NULL,
    created_date DATETIME NULL,
    local_datasource_id BIGINT NULL,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_workspace (workspace_id),
    INDEX idx_local_ds (local_datasource_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_DASHBOARD (
    id VARCHAR(100) PRIMARY KEY,
    workspace_id VARCHAR(100) NOT NULL,
    display_name VARCHAR(500) NOT NULL,
    web_url TEXT NULL,
    embed_url TEXT NULL,
    is_read_only BOOLEAN NOT NULL DEFAULT 0,
    local_dashboard_id BIGINT NULL,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_workspace (workspace_id),
    INDEX idx_local_dash (local_dashboard_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_DATASOURCE (
    id VARCHAR(100) PRIMARY KEY,
    dataset_id VARCHAR(100) NOT NULL,
    gateway_id VARCHAR(100) NULL,
    datasource_type VARCHAR(100) NOT NULL,
    server VARCHAR(500) NULL,
    database_name VARCHAR(500) NULL,
    url TEXT NULL,
    path TEXT NULL,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_dataset (dataset_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_GATEWAY (
    id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(500) NOT NULL,
    type VARCHAR(50) NOT NULL,
    public_key TEXT NULL,
    synced_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_POWERBI_SYNC_LOG (
    id BIGINT AUTO_INCREMENT PRIMARY KEY,
    sync_type VARCHAR(50) NOT NULL,
    status VARCHAR(20) NOT NULL,
    items_created INT NOT NULL DEFAULT 0,
    items_updated INT NOT NULL DEFAULT 0,
    items_deleted INT NOT NULL DEFAULT 0,
    items_errored INT NOT NULL DEFAULT 0,
    error_message TEXT NULL,
    started_at DATETIME NOT NULL,
    completed_at DATETIME NULL,
    INDEX idx_sync_type (sync_type),
    INDEX idx_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
