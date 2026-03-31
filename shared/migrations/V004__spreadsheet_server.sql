-- V004: Spreadsheet Server tables

CREATE TABLE IF NOT EXISTS RS_SAVED_QUERY (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    description TEXT NULL,
    datasource_id BIGINT NOT NULL,
    query_type ENUM('SQL','VISUAL') NOT NULL DEFAULT 'SQL',
    sql_text LONGTEXT NULL,
    visual_model JSON NULL,
    parameters JSON NULL,
    created_by BIGINT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_sq_ds (datasource_id),
    INDEX idx_sq_creator (created_by)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_ERP_CONNECTOR_CONFIG (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    datasource_id BIGINT NOT NULL,
    erp_type VARCHAR(50) NOT NULL DEFAULT 'GENERIC',
    gl_balance_query LONGTEXT NULL,
    gl_detail_query LONGTEXT NULL,
    gl_range_query LONGTEXT NULL,
    account_table VARCHAR(200) NULL,
    account_column VARCHAR(200) NULL,
    period_table VARCHAR(200) NULL,
    fiscal_year_start_month INT NOT NULL DEFAULT 1,
    segment_format VARCHAR(500) NULL,
    config JSON NULL,
    UNIQUE KEY uq_erpc_ds (datasource_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS RS_EXCEL_TEMPLATE (
    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(200) NOT NULL,
    description TEXT NULL,
    file_name VARCHAR(255) NOT NULL,
    file_data LONGBLOB NOT NULL,
    file_size BIGINT NOT NULL,
    uploaded_by BIGINT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
