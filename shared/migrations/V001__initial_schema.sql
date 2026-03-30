-- ============================================================================
-- DaisyReportServer - Initial Schema Migration V001
-- MySQL 8.0 | InnoDB | utf8mb4_unicode_ci
-- ============================================================================
-- Closure tables for hierarchies, BIGINT AUTO_INCREMENT PKs,
-- AES-256-GCM encrypted fields, Argon2id password hashes.
-- ============================================================================

SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0;
SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0;
SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='STRICT_TRANS_TABLES,NO_ENGINE_SUBSTITUTION';

-- ============================================================================
-- SCHEMA VERSION
-- ============================================================================

CREATE TABLE IF NOT EXISTS RS_SCHEMA_VERSION (
    version       INT          NOT NULL,
    major         INT          NOT NULL,
    minor         INT          NOT NULL,
    patch         INT          NOT NULL,
    description   VARCHAR(500) NULL,
    script_name   VARCHAR(500) NOT NULL,
    checksum      VARCHAR(64)  NULL,
    installed_by  VARCHAR(100) NOT NULL,
    installed_on  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    execution_time_ms INT      NOT NULL DEFAULT 0,
    success       BOOLEAN      NOT NULL DEFAULT 1,
    PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO RS_SCHEMA_VERSION (version, major, minor, patch, description, script_name, installed_by)
VALUES (1, 0, 0, 1, 'Initial schema creation', 'V001__initial_schema.sql', 'migration');

-- ============================================================================
-- IDENTITY & ACCESS
-- ============================================================================

CREATE TABLE RS_USER (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    username          VARCHAR(100) NOT NULL,
    password_hash     VARCHAR(255) NOT NULL COMMENT 'Argon2id hash',
    email             VARCHAR(255) NULL,
    firstname         VARCHAR(100) NULL,
    lastname          VARCHAR(100) NULL,
    enabled           BOOLEAN      NOT NULL DEFAULT 1,
    locked_until      DATETIME     NULL,
    login_failures    INT          NOT NULL DEFAULT 0,
    password_changed  DATETIME     NULL,
    otp_hash          VARCHAR(255) NULL,
    otp_expires       DATETIME     NULL,
    created_at        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at        DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_user_username (username),
    INDEX idx_user_email (email),
    INDEX idx_user_enabled (enabled)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ORG_UNIT (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    parent_id   BIGINT       NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ou_parent (parent_id),
    CONSTRAINT fk_ou_parent FOREIGN KEY (parent_id) REFERENCES RS_ORG_UNIT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ORG_UNIT_CLOSURE (
    ancestor_id   BIGINT NOT NULL,
    descendant_id BIGINT NOT NULL,
    depth         INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (ancestor_id, descendant_id),
    INDEX idx_ouc_descendant (descendant_id),
    INDEX idx_ouc_depth (depth),
    CONSTRAINT fk_ouc_ancestor FOREIGN KEY (ancestor_id) REFERENCES RS_ORG_UNIT (id) ON DELETE CASCADE,
    CONSTRAINT fk_ouc_descendant FOREIGN KEY (descendant_id) REFERENCES RS_ORG_UNIT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_GROUP (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_group_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_GROUP_MEMBER (
    group_id BIGINT NOT NULL,
    user_id  BIGINT NOT NULL,
    PRIMARY KEY (group_id, user_id),
    INDEX idx_gm_user (user_id),
    CONSTRAINT fk_gm_group FOREIGN KEY (group_id) REFERENCES RS_GROUP (id) ON DELETE CASCADE,
    CONSTRAINT fk_gm_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_GROUP_CLOSURE (
    ancestor_id   BIGINT NOT NULL,
    descendant_id BIGINT NOT NULL,
    depth         INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (ancestor_id, descendant_id),
    INDEX idx_gc_descendant (descendant_id),
    INDEX idx_gc_depth (depth),
    CONSTRAINT fk_gc_ancestor FOREIGN KEY (ancestor_id) REFERENCES RS_GROUP (id) ON DELETE CASCADE,
    CONSTRAINT fk_gc_descendant FOREIGN KEY (descendant_id) REFERENCES RS_GROUP (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_OU_MEMBER (
    ou_id   BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    PRIMARY KEY (ou_id, user_id),
    INDEX idx_oum_user (user_id),
    CONSTRAINT fk_oum_ou FOREIGN KEY (ou_id) REFERENCES RS_ORG_UNIT (id) ON DELETE CASCADE,
    CONSTRAINT fk_oum_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_SESSION (
    id            VARCHAR(128) NOT NULL,
    user_id       BIGINT       NOT NULL,
    data          JSON         NULL,
    ip_address    VARCHAR(45)  NULL,
    user_agent    VARCHAR(500) NULL,
    created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at    DATETIME     NOT NULL,
    last_activity DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_session_user (user_id),
    INDEX idx_session_expires (expires_at),
    INDEX idx_session_last_activity (last_activity),
    CONSTRAINT fk_session_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_USER_VARIABLE_DEF (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    type        ENUM('TEXT','LIST') NOT NULL DEFAULT 'TEXT',
    description TEXT         NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_uv_def_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_USER_VARIABLE_VALUE (
    user_id         BIGINT NOT NULL,
    variable_def_id BIGINT NOT NULL,
    value           TEXT   NULL,
    PRIMARY KEY (user_id, variable_def_id),
    CONSTRAINT fk_uvv_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE,
    CONSTRAINT fk_uvv_def FOREIGN KEY (variable_def_id) REFERENCES RS_USER_VARIABLE_DEF (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- ACL / ACE
-- ============================================================================

CREATE TABLE RS_ACL (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    entity_type VARCHAR(50) NOT NULL,
    entity_id   BIGINT      NOT NULL,
    created_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_acl_entity (entity_type, entity_id),
    INDEX idx_acl_entity_type (entity_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_PERMISSION_DEF (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    code        VARCHAR(100) NOT NULL,
    category    VARCHAR(50)  NULL,
    description TEXT         NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_perm_code (code),
    INDEX idx_perm_category (category)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ACE (
    id             BIGINT      NOT NULL AUTO_INCREMENT,
    acl_id         BIGINT      NOT NULL,
    principal_type ENUM('USER','GROUP','OU') NOT NULL,
    principal_id   BIGINT      NOT NULL,
    access_type    ENUM('GRANT','REVOKE') NOT NULL DEFAULT 'GRANT',
    permission     VARCHAR(50) NOT NULL,
    inherit        BOOLEAN     NOT NULL DEFAULT 1,
    position       INT         NOT NULL DEFAULT 0,
    created_at     DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ace_acl (acl_id),
    INDEX idx_ace_principal (principal_type, principal_id),
    INDEX idx_ace_permission (permission),
    CONSTRAINT fk_ace_acl FOREIGN KEY (acl_id) REFERENCES RS_ACL (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- DATASOURCES
-- ============================================================================

CREATE TABLE RS_DATASOURCE (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    dtype       ENUM('DATABASE','CSV','SCRIPT','BIRT','MONDRIAN','XMLA','BUNDLE') NOT NULL,
    folder_id   BIGINT       NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ds_dtype (dtype),
    INDEX idx_ds_folder (folder_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DATABASE_DATASOURCE (
    datasource_id   BIGINT       NOT NULL,
    driver_class    VARCHAR(255) NOT NULL,
    jdbc_url        TEXT         NOT NULL,
    username        VARCHAR(255) NULL,
    password_encrypted TEXT      NULL COMMENT 'AES-256-GCM encrypted',
    min_pool        INT          NOT NULL DEFAULT 3,
    max_pool        INT          NOT NULL DEFAULT 15,
    query_timeout   INT          NOT NULL DEFAULT 300,
    PRIMARY KEY (datasource_id),
    CONSTRAINT fk_dbds_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CSV_DATASOURCE (
    datasource_id  BIGINT    NOT NULL,
    connector_type ENUM('TEXT','ARGUMENT','URL') NOT NULL DEFAULT 'TEXT',
    content        LONGTEXT  NULL,
    url            TEXT      NULL,
    delimiter      CHAR(1)   NOT NULL DEFAULT ',',
    quote_char     CHAR(1)   NOT NULL DEFAULT '"',
    encoding       VARCHAR(20) NOT NULL DEFAULT 'UTF-8',
    cache_minutes  INT       NOT NULL DEFAULT 0,
    PRIMARY KEY (datasource_id),
    CONSTRAINT fk_csvds_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_SCRIPT_DATASOURCE (
    datasource_id BIGINT       NOT NULL,
    script_path   VARCHAR(500) NOT NULL,
    PRIMARY KEY (datasource_id),
    CONSTRAINT fk_sds_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DATASOURCE_PARAM_DEF (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    datasource_id BIGINT       NOT NULL,
    name          VARCHAR(200) NOT NULL,
    type          VARCHAR(50)  NOT NULL,
    default_value TEXT         NULL,
    PRIMARY KEY (id),
    INDEX idx_dspd_ds (datasource_id),
    CONSTRAINT fk_dspd_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DATASOURCE_CONNECTOR (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    name          VARCHAR(200) NOT NULL,
    type          ENUM('SQL','NOSQL','API','FILE','STREAM','SEARCH','TIMESERIES','CLOUD') NOT NULL,
    config_schema JSON         NULL,
    driver_info   JSON         NULL,
    created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dsc_type (type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- REPORTS & DYNAMIC LISTS
-- ============================================================================

CREATE TABLE RS_REPORT_FOLDER (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    parent_id   BIGINT       NULL,
    description TEXT         NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_rf_parent (parent_id),
    CONSTRAINT fk_rf_parent FOREIGN KEY (parent_id) REFERENCES RS_REPORT_FOLDER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_REPORT_FOLDER_CLOSURE (
    ancestor_id   BIGINT NOT NULL,
    descendant_id BIGINT NOT NULL,
    depth         INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (ancestor_id, descendant_id),
    INDEX idx_rfc_descendant (descendant_id),
    INDEX idx_rfc_depth (depth),
    CONSTRAINT fk_rfc_ancestor FOREIGN KEY (ancestor_id) REFERENCES RS_REPORT_FOLDER (id) ON DELETE CASCADE,
    CONSTRAINT fk_rfc_descendant FOREIGN KEY (descendant_id) REFERENCES RS_REPORT_FOLDER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_REPORT (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    folder_id     BIGINT       NULL,
    name          VARCHAR(200) NOT NULL,
    description   TEXT         NULL,
    key_field     VARCHAR(100) NULL,
    engine_type   ENUM('DYNAMIC_LIST','SCRIPT','BIRT','JASPER','CRYSTAL','MONDRIAN','JXLS','GRID_EDITOR') NOT NULL,
    datasource_id BIGINT       NULL,
    query_text    LONGTEXT     NULL,
    config        JSON         NULL,
    created_by    BIGINT       NULL,
    created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_report_key (key_field),
    INDEX idx_report_folder (folder_id),
    INDEX idx_report_engine (engine_type),
    INDEX idx_report_ds (datasource_id),
    INDEX idx_report_created_by (created_by),
    CONSTRAINT fk_report_folder FOREIGN KEY (folder_id) REFERENCES RS_REPORT_FOLDER (id) ON DELETE SET NULL,
    CONSTRAINT fk_report_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE SET NULL,
    CONSTRAINT fk_report_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_PARAMETER_DEF (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    report_id     BIGINT       NOT NULL,
    name          VARCHAR(200) NOT NULL,
    key_field     VARCHAR(50)  NOT NULL,
    type          ENUM('TEXT','DATE','DATASOURCE','FILE','SCRIPT','USER_VARIABLE') NOT NULL DEFAULT 'TEXT',
    default_value TEXT         NULL,
    mandatory     BOOLEAN      NOT NULL DEFAULT 0,
    position      INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_pd_report (report_id),
    CONSTRAINT fk_pd_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_REPORT_VARIANT (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    report_id   BIGINT       NOT NULL,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    config      JSON         NULL,
    created_by  BIGINT       NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_rv_report (report_id),
    INDEX idx_rv_creator (created_by),
    CONSTRAINT fk_rv_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE,
    CONSTRAINT fk_rv_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DL_COLUMN (
    id        BIGINT       NOT NULL AUTO_INCREMENT,
    report_id BIGINT       NOT NULL,
    name      VARCHAR(200) NOT NULL,
    alias_name VARCHAR(200) NULL,
    type      VARCHAR(50)  NOT NULL,
    position  INT          NOT NULL DEFAULT 0,
    hidden    BOOLEAN      NOT NULL DEFAULT 0,
    format    VARCHAR(100) NULL,
    width     INT          NULL,
    link_url  TEXT         NULL,
    PRIMARY KEY (id),
    INDEX idx_dlc_report (report_id),
    CONSTRAINT fk_dlc_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DL_FILTER (
    id          BIGINT NOT NULL AUTO_INCREMENT,
    report_id   BIGINT NOT NULL,
    column_id   BIGINT NULL,
    filter_type ENUM('INCLUSION','EXCLUSION','RANGE','WILDCARD','NULL') NOT NULL,
    `values`    JSON   NULL,
    PRIMARY KEY (id),
    INDEX idx_dlf_report (report_id),
    INDEX idx_dlf_column (column_id),
    CONSTRAINT fk_dlf_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE,
    CONSTRAINT fk_dlf_column FOREIGN KEY (column_id) REFERENCES RS_DL_COLUMN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DL_SORT (
    id        BIGINT NOT NULL AUTO_INCREMENT,
    report_id BIGINT NOT NULL,
    column_id BIGINT NULL,
    direction ENUM('ASC','DESC') NOT NULL DEFAULT 'ASC',
    position  INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_dls_report (report_id),
    CONSTRAINT fk_dls_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE,
    CONSTRAINT fk_dls_column FOREIGN KEY (column_id) REFERENCES RS_DL_COLUMN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DL_PREFILTER (
    id        BIGINT NOT NULL AUTO_INCREMENT,
    report_id BIGINT NOT NULL,
    config    JSON   NULL COMMENT 'Tree-based AND/OR/CONDITION structure',
    PRIMARY KEY (id),
    INDEX idx_dlpf_report (report_id),
    CONSTRAINT fk_dlpf_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DL_COMPUTED_COLUMN (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    report_id  BIGINT       NOT NULL,
    name       VARCHAR(200) NOT NULL,
    expression TEXT         NOT NULL,
    position   INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_dlcc_report (report_id),
    CONSTRAINT fk_dlcc_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- DATASINKS
-- ============================================================================

CREATE TABLE RS_DATASINK (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    dtype       ENUM('EMAIL','SFTP','FTPS','FTP','SCP','LOCAL','SAMBA','PRINTER','TABLE','SCRIPT','S3','DROPBOX','ONEDRIVE','GOOGLE_DRIVE','BOX','HTTPD') NOT NULL,
    folder_id   BIGINT       NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dsk_dtype (dtype),
    INDEX idx_dsk_folder (folder_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_EMAIL_DATASINK (
    datasink_id        BIGINT       NOT NULL,
    host               VARCHAR(255) NOT NULL,
    port               INT          NOT NULL DEFAULT 25,
    encryption         ENUM('NONE','STARTTLS','SSL') NOT NULL DEFAULT 'NONE',
    username           VARCHAR(255) NULL,
    password_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    sender_address     VARCHAR(255) NULL,
    sender_name        VARCHAR(200) NULL,
    force_sender       BOOLEAN      NOT NULL DEFAULT 0,
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_emailds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_SFTP_DATASINK (
    datasink_id          BIGINT       NOT NULL,
    host                 VARCHAR(255) NOT NULL,
    port                 INT          NOT NULL DEFAULT 22,
    auth_type            ENUM('PASSWORD','PUBLIC_KEY') NOT NULL DEFAULT 'PASSWORD',
    username             VARCHAR(255) NULL,
    password_encrypted   TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    public_key           TEXT         NULL,
    passphrase_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    known_hosts          TEXT         NULL,
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_sftpds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_FTP_DATASINK (
    datasink_id        BIGINT       NOT NULL,
    host               VARCHAR(255) NOT NULL,
    port               INT          NOT NULL DEFAULT 21,
    ftps_mode          ENUM('IMPLICIT','EXPLICIT') NULL,
    username           VARCHAR(255) NULL,
    password_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_ftpds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_S3_DATASINK (
    datasink_id          BIGINT       NOT NULL,
    bucket               VARCHAR(255) NOT NULL,
    region               VARCHAR(50)  NULL,
    access_key_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    secret_key_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    endpoint_url         TEXT         NULL,
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_s3ds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CLOUD_DATASINK (
    datasink_id              BIGINT NOT NULL,
    provider                 ENUM('DROPBOX','ONEDRIVE','GOOGLE_DRIVE','BOX') NOT NULL,
    client_id                VARCHAR(255) NULL,
    client_secret_encrypted  TEXT   NULL COMMENT 'AES-256-GCM encrypted',
    access_token_encrypted   TEXT   NULL COMMENT 'AES-256-GCM encrypted',
    refresh_token_encrypted  TEXT   NULL COMMENT 'AES-256-GCM encrypted',
    token_expiry             DATETIME NULL,
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_cloudds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_TABLE_DATASINK (
    datasink_id          BIGINT       NOT NULL,
    target_datasource_id BIGINT       NULL,
    target_table         VARCHAR(255) NOT NULL,
    truncate_before      BOOLEAN      NOT NULL DEFAULT 0,
    batch_size           INT          NOT NULL DEFAULT 1000,
    PRIMARY KEY (datasink_id),
    INDEX idx_tds_target_ds (target_datasource_id),
    CONSTRAINT fk_tds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE,
    CONSTRAINT fk_tds_target FOREIGN KEY (target_datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_LOCAL_DATASINK (
    datasink_id BIGINT       NOT NULL,
    base_path   VARCHAR(500) NOT NULL,
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_localds_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_SAMBA_DATASINK (
    datasink_id        BIGINT       NOT NULL,
    host               VARCHAR(255) NOT NULL,
    share_name         VARCHAR(255) NOT NULL,
    domain_name        VARCHAR(255) NULL,
    username           VARCHAR(255) NULL,
    password_encrypted TEXT         NULL COMMENT 'AES-256-GCM encrypted',
    PRIMARY KEY (datasink_id),
    CONSTRAINT fk_sambads_ds FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DATASINK_CONFIG (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    report_id         BIGINT       NOT NULL,
    datasink_id       BIGINT       NOT NULL,
    filename_template VARCHAR(500) NULL,
    folder_path       VARCHAR(500) NULL,
    output_format     VARCHAR(20)  NULL,
    compress          BOOLEAN      NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_dskc_report (report_id),
    INDEX idx_dskc_datasink (datasink_id),
    CONSTRAINT fk_dskc_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE,
    CONSTRAINT fk_dskc_datasink FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- SCHEDULING
-- ============================================================================

CREATE TABLE RS_SCHEDULE_JOB (
    id                  BIGINT       NOT NULL AUTO_INCREMENT,
    name                VARCHAR(200) NOT NULL,
    description         TEXT         NULL,
    report_id           BIGINT       NULL,
    owner_id            BIGINT       NULL,
    status              ENUM('INACTIVE','WAITING','EXECUTING','COMPLETED','FAILED','CRITICAL_FAILURE') NOT NULL DEFAULT 'INACTIVE',
    schedule_type       ENUM('CRON','ONCE','DAILY','WEEKLY','MONTHLY','INTERVAL') NOT NULL,
    schedule_expression VARCHAR(255) NULL,
    timezone            VARCHAR(50)  NOT NULL DEFAULT 'UTC',
    next_fire_time      DATETIME     NULL,
    last_fire_time      DATETIME     NULL,
    retry_count         INT          NOT NULL DEFAULT 0,
    max_retries         INT          NOT NULL DEFAULT 0,
    occurrence_count    INT          NOT NULL DEFAULT 0,
    max_occurrences     INT          NOT NULL DEFAULT 0,
    lock_owner          VARCHAR(100) NULL,
    lock_acquired_at    DATETIME     NULL,
    heartbeat_at        DATETIME     NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_sj_report (report_id),
    INDEX idx_sj_owner (owner_id),
    INDEX idx_sj_status (status),
    INDEX idx_sj_next_fire (next_fire_time),
    INDEX idx_sj_lock (lock_owner, lock_acquired_at),
    CONSTRAINT fk_sj_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE SET NULL,
    CONSTRAINT fk_sj_owner FOREIGN KEY (owner_id) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_JOB_TRIGGER (
    id           BIGINT      NOT NULL AUTO_INCREMENT,
    job_id       BIGINT      NOT NULL,
    trigger_type VARCHAR(50) NOT NULL,
    config       JSON        NULL,
    PRIMARY KEY (id),
    INDEX idx_jt_job (job_id),
    CONSTRAINT fk_jt_job FOREIGN KEY (job_id) REFERENCES RS_SCHEDULE_JOB (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_JOB_ACTION (
    id          BIGINT NOT NULL AUTO_INCREMENT,
    job_id      BIGINT NOT NULL,
    action_type ENUM('EMAIL','TEAMSPACE','DATASINK','TABLE') NOT NULL,
    datasink_id BIGINT NULL,
    config      JSON   NULL,
    sort_order  INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_ja_job (job_id),
    INDEX idx_ja_datasink (datasink_id),
    CONSTRAINT fk_ja_job FOREIGN KEY (job_id) REFERENCES RS_SCHEDULE_JOB (id) ON DELETE CASCADE,
    CONSTRAINT fk_ja_datasink FOREIGN KEY (datasink_id) REFERENCES RS_DATASINK (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_JOB_CONDITION (
    id                  BIGINT  NOT NULL AUTO_INCREMENT,
    job_id              BIGINT  NOT NULL,
    condition_report_id BIGINT  NULL,
    expression          TEXT    NULL,
    skip_on_fail        BOOLEAN NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX idx_jc_job (job_id),
    INDEX idx_jc_report (condition_report_id),
    CONSTRAINT fk_jc_job FOREIGN KEY (job_id) REFERENCES RS_SCHEDULE_JOB (id) ON DELETE CASCADE,
    CONSTRAINT fk_jc_report FOREIGN KEY (condition_report_id) REFERENCES RS_REPORT (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_JOB_EXECUTION (
    id            BIGINT NOT NULL AUTO_INCREMENT,
    job_id        BIGINT NOT NULL,
    status        ENUM('RUNNING','COMPLETED','FAILED') NOT NULL DEFAULT 'RUNNING',
    started_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    completed_at  DATETIME NULL,
    duration_ms   INT    NULL,
    output_size   BIGINT NULL,
    error_message TEXT   NULL,
    retry_attempt INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_je_job (job_id),
    INDEX idx_je_status (status),
    INDEX idx_je_started (started_at),
    CONSTRAINT fk_je_job FOREIGN KEY (job_id) REFERENCES RS_SCHEDULE_JOB (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- DASHBOARDS
-- ============================================================================

CREATE TABLE RS_DASHBOARD_FOLDER (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    name       VARCHAR(200) NOT NULL,
    parent_id  BIGINT       NULL,
    created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dbf_parent (parent_id),
    CONSTRAINT fk_dbf_parent FOREIGN KEY (parent_id) REFERENCES RS_DASHBOARD_FOLDER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DASHBOARD_FOLDER_CLOSURE (
    ancestor_id   BIGINT NOT NULL,
    descendant_id BIGINT NOT NULL,
    depth         INT    NOT NULL DEFAULT 0,
    PRIMARY KEY (ancestor_id, descendant_id),
    INDEX idx_dfc_descendant (descendant_id),
    INDEX idx_dfc_depth (depth),
    CONSTRAINT fk_dfc_ancestor FOREIGN KEY (ancestor_id) REFERENCES RS_DASHBOARD_FOLDER (id) ON DELETE CASCADE,
    CONSTRAINT fk_dfc_descendant FOREIGN KEY (descendant_id) REFERENCES RS_DASHBOARD_FOLDER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DASHBOARD (
    id                  BIGINT       NOT NULL AUTO_INCREMENT,
    folder_id           BIGINT       NULL,
    name                VARCHAR(200) NOT NULL,
    description         TEXT         NULL,
    layout              ENUM('SINGLE','TWO_COLUMN','THREE_COLUMN','TABS','FREEFORM') NOT NULL DEFAULT 'TWO_COLUMN',
    columns             INT          NOT NULL DEFAULT 2,
    reload_interval     INT          NOT NULL DEFAULT 0,
    is_primary          BOOLEAN      NOT NULL DEFAULT 0,
    is_config_protected BOOLEAN      NOT NULL DEFAULT 0,
    created_by          BIGINT       NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    version             INT          NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX idx_dash_folder (folder_id),
    INDEX idx_dash_creator (created_by),
    CONSTRAINT fk_dash_folder FOREIGN KEY (folder_id) REFERENCES RS_DASHBOARD_FOLDER (id) ON DELETE SET NULL,
    CONSTRAINT fk_dash_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DADGET (
    id           BIGINT      NOT NULL AUTO_INCREMENT,
    dashboard_id BIGINT      NOT NULL,
    dtype        VARCHAR(50) NOT NULL,
    col_position INT         NOT NULL DEFAULT 0,
    row_position INT         NOT NULL DEFAULT 0,
    width_span   INT         NOT NULL DEFAULT 1,
    height       INT         NULL,
    config       JSON        NULL,
    created_at   DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dadget_dash (dashboard_id),
    CONSTRAINT fk_dadget_dash FOREIGN KEY (dashboard_id) REFERENCES RS_DASHBOARD (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DADGET_PARAM (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    dadget_id   BIGINT       NOT NULL,
    param_key   VARCHAR(100) NOT NULL,
    param_value TEXT         NULL,
    PRIMARY KEY (id),
    INDEX idx_dp_dadget (dadget_id),
    CONSTRAINT fk_dp_dadget FOREIGN KEY (dadget_id) REFERENCES RS_DADGET (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- TEAMSPACES
-- ============================================================================

CREATE TABLE RS_TEAMSPACE (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    description TEXT         NULL,
    created_by  BIGINT       NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ts_creator (created_by),
    CONSTRAINT fk_ts_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_TEAMSPACE_MEMBER (
    id           BIGINT NOT NULL AUTO_INCREMENT,
    teamspace_id BIGINT NOT NULL,
    user_id      BIGINT NOT NULL,
    role         ENUM('ADMIN','MANAGER','GUEST') NOT NULL DEFAULT 'GUEST',
    joined_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_tsm_member (teamspace_id, user_id),
    INDEX idx_tsm_user (user_id),
    CONSTRAINT fk_tsm_ts FOREIGN KEY (teamspace_id) REFERENCES RS_TEAMSPACE (id) ON DELETE CASCADE,
    CONSTRAINT fk_tsm_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_TEAMSPACE_APP (
    id           BIGINT      NOT NULL AUTO_INCREMENT,
    teamspace_id BIGINT      NOT NULL,
    entity_type  VARCHAR(50) NOT NULL,
    entity_id    BIGINT      NOT NULL,
    imported_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_tsa_ts (teamspace_id),
    INDEX idx_tsa_entity (entity_type, entity_id),
    CONSTRAINT fk_tsa_ts FOREIGN KEY (teamspace_id) REFERENCES RS_TEAMSPACE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_TEAMSPACE_FILE (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    teamspace_id BIGINT       NOT NULL,
    name         VARCHAR(255) NOT NULL,
    path         VARCHAR(500) NOT NULL,
    mime_type    VARCHAR(100) NULL,
    size_bytes   BIGINT       NULL,
    uploaded_by  BIGINT       NULL,
    uploaded_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_tsf_ts (teamspace_id),
    INDEX idx_tsf_uploader (uploaded_by),
    CONSTRAINT fk_tsf_ts FOREIGN KEY (teamspace_id) REFERENCES RS_TEAMSPACE (id) ON DELETE CASCADE,
    CONSTRAINT fk_tsf_uploader FOREIGN KEY (uploaded_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- CONFIGURATION & SYSTEM
-- ============================================================================

CREATE TABLE RS_CONFIG (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    config_key   VARCHAR(200) NOT NULL,
    config_value TEXT         NULL,
    category     VARCHAR(50)  NULL,
    description  TEXT         NULL,
    updated_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_config_key (config_key),
    INDEX idx_config_category (category)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CONFIG_FILE (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    path       VARCHAR(500) NOT NULL,
    content    LONGTEXT     NULL,
    updated_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_config_file_path (path)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_THEME (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    name       VARCHAR(100) NOT NULL,
    config     JSON         NULL,
    is_default BOOLEAN      NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE KEY uq_theme_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_LOCALIZATION (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    locale        VARCHAR(10)  NOT NULL,
    module        VARCHAR(50)  NOT NULL,
    message_key   VARCHAR(200) NOT NULL,
    message_value TEXT         NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_l10n (locale, module, message_key),
    INDEX idx_l10n_locale (locale)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_GLOBAL_CONSTANT (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(100) NOT NULL,
    value       TEXT         NULL,
    description TEXT         NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_gc_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_EXPORT_CONFIG (
    id        BIGINT      NOT NULL AUTO_INCREMENT,
    report_id BIGINT      NOT NULL,
    format    VARCHAR(20) NOT NULL,
    config    JSON        NULL,
    PRIMARY KEY (id),
    INDEX idx_ec_report (report_id),
    CONSTRAINT fk_ec_report FOREIGN KEY (report_id) REFERENCES RS_REPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- HOOKS & SCRIPTS
-- ============================================================================

CREATE TABLE RS_HOOK (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    hook_type   ENUM('VETO','NOTIFY','PAM','DATABASE_HELPER') NOT NULL,
    entity_type VARCHAR(50)  NULL,
    script_path VARCHAR(500) NOT NULL,
    priority    INT          NOT NULL DEFAULT 100,
    enabled     BOOLEAN      NOT NULL DEFAULT 1,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_hook_type (hook_type),
    INDEX idx_hook_entity (entity_type),
    INDEX idx_hook_enabled (enabled)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_SCRIPT_LIBRARY (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    name        VARCHAR(200) NOT NULL,
    path        VARCHAR(500) NOT NULL,
    description TEXT         NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_PROPERTY (
    id             BIGINT       NOT NULL AUTO_INCREMENT,
    property_key   VARCHAR(200) NOT NULL,
    property_value TEXT         NULL,
    updated_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_property_key (property_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- INTEGRATION
-- ============================================================================

CREATE TABLE RS_LDAP_CONFIG (
    id                    BIGINT       NOT NULL AUTO_INCREMENT,
    name                  VARCHAR(200) NOT NULL,
    host                  VARCHAR(255) NOT NULL,
    port                  INT          NOT NULL DEFAULT 389,
    use_ssl               BOOLEAN      NOT NULL DEFAULT 0,
    bind_dn               TEXT         NULL,
    bind_password_encrypted TEXT       NULL COMMENT 'AES-256-GCM encrypted',
    base_dn               TEXT         NULL,
    user_filter           TEXT         NULL,
    group_filter          TEXT         NULL,
    attr_mapping          JSON         NULL,
    sync_interval_minutes INT          NOT NULL DEFAULT 60,
    enabled               BOOLEAN      NOT NULL DEFAULT 1,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_OIDC_CONFIG (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    name              VARCHAR(200) NOT NULL,
    issuer_url        TEXT         NOT NULL,
    client_id         VARCHAR(255) NOT NULL,
    client_secret_encrypted TEXT   NULL COMMENT 'AES-256-GCM encrypted',
    scopes            VARCHAR(500) NOT NULL DEFAULT 'openid profile email',
    username_claim    VARCHAR(100) NOT NULL DEFAULT 'preferred_username',
    auto_create_user  BOOLEAN      NOT NULL DEFAULT 0,
    enabled           BOOLEAN      NOT NULL DEFAULT 1,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_PAM_CONFIG (
    id       BIGINT      NOT NULL AUTO_INCREMENT,
    name     VARCHAR(200) NOT NULL,
    pam_type VARCHAR(50) NOT NULL,
    position INT         NOT NULL DEFAULT 0,
    config   JSON        NULL,
    enabled  BOOLEAN     NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX idx_pam_position (position)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_API_TOKEN (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    user_id      BIGINT       NOT NULL,
    token_hash   VARCHAR(128) NOT NULL,
    token_prefix VARCHAR(10)  NULL,
    description  TEXT         NULL,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_used_at DATETIME     NULL,
    revoked      BOOLEAN      NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_at_user (user_id),
    INDEX idx_at_hash (token_hash),
    INDEX idx_at_prefix (token_prefix),
    CONSTRAINT fk_at_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- TRANSPORT & VERSIONING
-- ============================================================================

CREATE TABLE RS_TRANSPORT (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    name            VARCHAR(200) NOT NULL,
    description     TEXT         NULL,
    status          ENUM('CREATED','CLOSED','IMPORTED','APPLIED') NOT NULL DEFAULT 'CREATED',
    source_instance VARCHAR(200) NULL,
    created_by      BIGINT       NULL,
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    closed_at       DATETIME     NULL,
    applied_at      DATETIME     NULL,
    PRIMARY KEY (id),
    INDEX idx_tr_status (status),
    INDEX idx_tr_creator (created_by),
    CONSTRAINT fk_tr_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_TRANSPORT_OBJECT (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    transport_id    BIGINT       NOT NULL,
    entity_type     VARCHAR(50)  NOT NULL,
    entity_id       BIGINT       NOT NULL,
    entity_key      VARCHAR(200) NULL,
    serialized_data LONGTEXT     NULL,
    checksum        VARCHAR(64)  NULL,
    PRIMARY KEY (id),
    INDEX idx_to_transport (transport_id),
    INDEX idx_to_entity (entity_type, entity_id),
    CONSTRAINT fk_to_transport FOREIGN KEY (transport_id) REFERENCES RS_TRANSPORT (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ENTITY_VERSION (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    entity_type VARCHAR(50) NOT NULL,
    entity_id   BIGINT      NOT NULL,
    version_num INT         NOT NULL,
    snapshot    LONGTEXT    NULL,
    created_by  BIGINT      NULL,
    created_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ev_entity (entity_type, entity_id),
    INDEX idx_ev_version (entity_type, entity_id, version_num),
    INDEX idx_ev_creator (created_by),
    CONSTRAINT fk_ev_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- AUDIT & LOGGING
-- ============================================================================

CREATE TABLE RS_AUDIT_LOG (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    action      VARCHAR(50) NOT NULL,
    username    VARCHAR(100) NULL,
    remote_addr VARCHAR(45) NULL,
    session_id  VARCHAR(128) NULL,
    entity_type VARCHAR(50) NULL,
    entity_id   BIGINT      NULL,
    details     JSON        NULL,
    created_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_al_action (action),
    INDEX idx_al_username (username),
    INDEX idx_al_entity (entity_type, entity_id),
    INDEX idx_al_created (created_at),
    INDEX idx_al_session (session_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_AUDIT_LOG_PROPERTY (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    audit_log_id BIGINT       NOT NULL,
    property_key VARCHAR(100) NOT NULL,
    property_value TEXT       NULL,
    PRIMARY KEY (id),
    INDEX idx_alp_log (audit_log_id),
    CONSTRAINT fk_alp_log FOREIGN KEY (audit_log_id) REFERENCES RS_AUDIT_LOG (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- SEARCH
-- ============================================================================

CREATE TABLE RS_SEARCH_INDEX (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    entity_type VARCHAR(50) NOT NULL,
    entity_id   BIGINT      NOT NULL,
    field_name  VARCHAR(50) NOT NULL,
    field_value TEXT        NULL,
    tokens      TEXT        NULL,
    weight      DECIMAL(3,1) NOT NULL DEFAULT 1.0,
    updated_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_si_entity (entity_type, entity_id),
    INDEX idx_si_entity_tokens (entity_type),
    FULLTEXT INDEX ftx_si_tokens (tokens)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- FAVORITES & RECENT
-- ============================================================================

CREATE TABLE RS_FAVORITE (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    user_id     BIGINT      NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id   BIGINT      NOT NULL,
    sort_order  INT         NOT NULL DEFAULT 0,
    created_at  DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_fav (user_id, entity_type, entity_id),
    CONSTRAINT fk_fav_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_RECENT_ENTRY (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    user_id     BIGINT      NOT NULL,
    entity_type VARCHAR(50) NOT NULL,
    entity_id   BIGINT      NOT NULL,
    accessed_at DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_re_user (user_id),
    INDEX idx_re_accessed (user_id, accessed_at DESC),
    CONSTRAINT fk_re_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- NOTIFICATIONS
-- ============================================================================

CREATE TABLE RS_NOTIFICATION (
    id        BIGINT       NOT NULL AUTO_INCREMENT,
    user_id   BIGINT       NOT NULL,
    type      VARCHAR(50)  NULL,
    title     VARCHAR(200) NOT NULL,
    message   TEXT         NULL,
    read_flag BOOLEAN      NOT NULL DEFAULT 0,
    link      VARCHAR(500) NULL,
    created_at DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_notif_user (user_id),
    INDEX idx_notif_unread (user_id, read_flag),
    INDEX idx_notif_created (created_at),
    CONSTRAINT fk_notif_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- SEMANTIC LAYER
-- ============================================================================

CREATE TABLE RS_DOMAIN (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    company_id      BIGINT       NULL,
    domain_code     VARCHAR(100) NOT NULL,
    name            VARCHAR(200) NOT NULL,
    description     TEXT         NULL,
    datasource_id   BIGINT       NULL,
    root_table_id   BIGINT       NULL,
    current_version INT          NOT NULL DEFAULT 1,
    status          ENUM('DRAFT','PUBLISHED','ARCHIVED') NOT NULL DEFAULT 'DRAFT',
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dom_company (company_id),
    INDEX idx_dom_code (domain_code),
    INDEX idx_dom_ds (datasource_id),
    INDEX idx_dom_status (status),
    CONSTRAINT fk_dom_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_TABLE (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    domain_id       BIGINT       NOT NULL,
    physical_schema VARCHAR(100) NULL,
    physical_table  VARCHAR(200) NOT NULL,
    table_alias     VARCHAR(200) NULL,
    is_root         BOOLEAN      NOT NULL DEFAULT 0,
    is_dimension    BOOLEAN      NOT NULL DEFAULT 0,
    row_count_cache BIGINT       NULL,
    PRIMARY KEY (id),
    INDEX idx_dt_domain (domain_id),
    CONSTRAINT fk_dt_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Add root_table FK now that RS_DOMAIN_TABLE exists
ALTER TABLE RS_DOMAIN ADD CONSTRAINT fk_dom_root_table
    FOREIGN KEY (root_table_id) REFERENCES RS_DOMAIN_TABLE (id) ON DELETE SET NULL;

CREATE TABLE RS_DOMAIN_TABLE_COLUMN (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    domain_table_id BIGINT       NOT NULL,
    physical_column VARCHAR(200) NOT NULL,
    display_name    VARCHAR(200) NULL,
    data_type       ENUM('STRING','INTEGER','LONG','FLOAT','DOUBLE','DECIMAL','BOOLEAN','DATE','TIME','DATETIME','TIMESTAMP','BINARY','JSON','XML') NOT NULL DEFAULT 'STRING',
    display_format  VARCHAR(100) NULL,
    is_searchable   BOOLEAN      NOT NULL DEFAULT 1,
    is_sortable     BOOLEAN      NOT NULL DEFAULT 1,
    is_filterable   BOOLEAN      NOT NULL DEFAULT 1,
    is_groupable    BOOLEAN      NOT NULL DEFAULT 1,
    is_pii          BOOLEAN      NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_dtc_table (domain_table_id),
    CONSTRAINT fk_dtc_table FOREIGN KEY (domain_table_id) REFERENCES RS_DOMAIN_TABLE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_JOIN (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    domain_id    BIGINT       NOT NULL,
    from_table_id BIGINT      NOT NULL,
    from_column  VARCHAR(200) NOT NULL,
    to_table_id  BIGINT       NOT NULL,
    to_column    VARCHAR(200) NOT NULL,
    join_type    ENUM('INNER','LEFT','RIGHT') NOT NULL DEFAULT 'LEFT',
    cardinality  ENUM('ONE_TO_ONE','ONE_TO_MANY','MANY_TO_ONE','MANY_TO_MANY') NULL,
    join_weight  INT          NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX idx_dj_domain (domain_id),
    INDEX idx_dj_from (from_table_id),
    INDEX idx_dj_to (to_table_id),
    CONSTRAINT fk_dj_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE,
    CONSTRAINT fk_dj_from FOREIGN KEY (from_table_id) REFERENCES RS_DOMAIN_TABLE (id) ON DELETE CASCADE,
    CONSTRAINT fk_dj_to FOREIGN KEY (to_table_id) REFERENCES RS_DOMAIN_TABLE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_JOIN_COLUMN (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    join_id     BIGINT       NOT NULL,
    from_column VARCHAR(200) NOT NULL,
    to_column   VARCHAR(200) NOT NULL,
    position    INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_djc_join (join_id),
    CONSTRAINT fk_djc_join FOREIGN KEY (join_id) REFERENCES RS_DOMAIN_JOIN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_METRIC (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    domain_id         BIGINT       NOT NULL,
    metric_code       VARCHAR(100) NOT NULL,
    name              VARCHAR(200) NOT NULL,
    expression        TEXT         NOT NULL,
    return_type       VARCHAR(50)  NULL,
    display_format    VARCHAR(100) NULL,
    filter_expression TEXT         NULL,
    is_additive       BOOLEAN      NOT NULL DEFAULT 1,
    is_cumulative     BOOLEAN      NOT NULL DEFAULT 0,
    depends_on        JSON         NULL,
    PRIMARY KEY (id),
    INDEX idx_dm_domain (domain_id),
    INDEX idx_dm_code (metric_code),
    CONSTRAINT fk_dm_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_CALCULATED_COLUMN (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    domain_id       BIGINT       NOT NULL,
    column_code     VARCHAR(100) NOT NULL,
    name            VARCHAR(200) NOT NULL,
    expression      TEXT         NOT NULL,
    expression_mode ENUM('SQL_NATIVE','EXPRESSION_LANGUAGE') NOT NULL DEFAULT 'SQL_NATIVE',
    return_type     VARCHAR(50)  NULL,
    depends_on      JSON         NULL,
    PRIMARY KEY (id),
    INDEX idx_dcc_domain (domain_id),
    INDEX idx_dcc_code (column_code),
    CONSTRAINT fk_dcc_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_FIELD_VISIBILITY (
    id         BIGINT      NOT NULL AUTO_INCREMENT,
    domain_id  BIGINT      NOT NULL,
    role_id    BIGINT      NULL,
    field_type ENUM('COLUMN','METRIC','CALCULATED') NOT NULL,
    field_id   BIGINT      NOT NULL,
    visibility ENUM('VISIBLE','HIDDEN','MASKED') NOT NULL DEFAULT 'VISIBLE',
    PRIMARY KEY (id),
    INDEX idx_dfv_domain (domain_id),
    INDEX idx_dfv_role (role_id),
    CONSTRAINT fk_dfv_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_DOMAIN_VERSION (
    id                  BIGINT NOT NULL AUTO_INCREMENT,
    domain_id           BIGINT NOT NULL,
    version_num         INT    NOT NULL,
    state               ENUM('DRAFT','PUBLISHED','ARCHIVED') NOT NULL DEFAULT 'DRAFT',
    definition_snapshot LONGTEXT NULL,
    created_by          BIGINT NULL,
    created_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_dv_domain (domain_id),
    INDEX idx_dv_version (domain_id, version_num),
    INDEX idx_dv_creator (created_by),
    CONSTRAINT fk_dv_domain FOREIGN KEY (domain_id) REFERENCES RS_DOMAIN (id) ON DELETE CASCADE,
    CONSTRAINT fk_dv_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- ALERTING
-- ============================================================================

CREATE TABLE RS_ALERT_RULE (
    id                   BIGINT       NOT NULL AUTO_INCREMENT,
    company_id           BIGINT       NULL,
    folder_id            BIGINT       NULL,
    uid                  VARCHAR(40)  NOT NULL,
    title                VARCHAR(200) NOT NULL,
    enabled              BOOLEAN      NOT NULL DEFAULT 1,
    query_config         JSON         NULL,
    condition_config     JSON         NULL,
    schedule_config      JSON         NULL,
    for_duration_seconds INT          NOT NULL DEFAULT 300,
    no_data_state        ENUM('NO_DATA','NORMAL','ALERTING') NOT NULL DEFAULT 'NO_DATA',
    error_state          ENUM('ERROR','ALERTING') NOT NULL DEFAULT 'ERROR',
    labels               JSON         NULL,
    annotations          JSON         NULL,
    created_by           BIGINT       NULL,
    created_at           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    UNIQUE KEY uq_ar_uid (uid),
    INDEX idx_ar_company (company_id),
    INDEX idx_ar_folder (folder_id),
    INDEX idx_ar_enabled (enabled),
    INDEX idx_ar_creator (created_by),
    CONSTRAINT fk_ar_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_RULE_STATE (
    id                 BIGINT NOT NULL AUTO_INCREMENT,
    rule_id            BIGINT NOT NULL,
    current_state      ENUM('NORMAL','PENDING','ALERTING','RESOLVED','NO_DATA','ERROR') NOT NULL DEFAULT 'NORMAL',
    state_changed_at   DATETIME NULL,
    last_evaluation_at DATETIME NULL,
    `last_value`       DOUBLE NULL,
    evaluation_count   BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    UNIQUE KEY uq_ars_rule (rule_id),
    CONSTRAINT fk_ars_rule FOREIGN KEY (rule_id) REFERENCES RS_ALERT_RULE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_CONTACT_POINT (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    company_id BIGINT       NULL,
    name       VARCHAR(200) NOT NULL,
    type       ENUM('EMAIL','SLACK','TEAMS','PAGERDUTY','SMS','WEBHOOK') NOT NULL,
    config     JSON         NULL,
    created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_acp_company (company_id),
    INDEX idx_acp_type (type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_NOTIFICATION_POLICY (
    id                    BIGINT  NOT NULL AUTO_INCREMENT,
    company_id            BIGINT  NULL,
    parent_id             BIGINT  NULL,
    label_matchers        JSON    NULL,
    contact_point_id      BIGINT  NULL,
    group_by              JSON    NULL,
    group_wait_seconds    INT     NOT NULL DEFAULT 30,
    group_interval_seconds INT    NOT NULL DEFAULT 300,
    continue_matching     BOOLEAN NOT NULL DEFAULT 0,
    position              INT     NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_anp_company (company_id),
    INDEX idx_anp_parent (parent_id),
    INDEX idx_anp_cp (contact_point_id),
    CONSTRAINT fk_anp_parent FOREIGN KEY (parent_id) REFERENCES RS_ALERT_NOTIFICATION_POLICY (id) ON DELETE CASCADE,
    CONSTRAINT fk_anp_cp FOREIGN KEY (contact_point_id) REFERENCES RS_ALERT_CONTACT_POINT (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_SILENCE (
    id             BIGINT   NOT NULL AUTO_INCREMENT,
    company_id     BIGINT   NULL,
    label_matchers JSON     NULL,
    starts_at      DATETIME NOT NULL,
    ends_at        DATETIME NOT NULL,
    comment        TEXT     NULL,
    created_by     BIGINT   NULL,
    created_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_as_company (company_id),
    INDEX idx_as_window (starts_at, ends_at),
    INDEX idx_as_creator (created_by),
    CONSTRAINT fk_as_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_MUTE_TIMING (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    company_id      BIGINT       NULL,
    name            VARCHAR(200) NOT NULL,
    cron_expression VARCHAR(100) NULL,
    timezone        VARCHAR(50)  NULL,
    enabled         BOOLEAN      NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    INDEX idx_amt_company (company_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_ALERT_HISTORY (
    id         BIGINT      NOT NULL AUTO_INCREMENT,
    rule_id    BIGINT      NOT NULL,
    event_type VARCHAR(50) NOT NULL,
    old_state  VARCHAR(20) NULL,
    new_state  VARCHAR(20) NULL,
    value      DOUBLE      NULL,
    labels     JSON        NULL,
    annotations JSON       NULL,
    created_at DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_ah_rule (rule_id),
    INDEX idx_ah_created (created_at),
    INDEX idx_ah_event (event_type),
    CONSTRAINT fk_ah_rule FOREIGN KEY (rule_id) REFERENCES RS_ALERT_RULE (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- QUERY CACHING
-- ============================================================================

CREATE TABLE RS_CACHE_ENTRY (
    id            BIGINT      NOT NULL AUTO_INCREMENT,
    query_hash    VARCHAR(64) NOT NULL,
    param_hash    VARCHAR(48) NULL,
    security_hash VARCHAR(48) NULL,
    ttl_seconds   INT         NOT NULL DEFAULT 300,
    security_mode ENUM('PUBLIC','PER_ROLE','PER_USER','PER_GROUP','AUTO') NOT NULL DEFAULT 'AUTO',
    source_tables JSON        NULL,
    created_at    DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at    DATETIME    NOT NULL,
    access_count  BIGINT      NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_ce_hash (query_hash, param_hash, security_hash),
    INDEX idx_ce_expires (expires_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CACHE_SOURCE_TABLE (
    id                BIGINT       NOT NULL AUTO_INCREMENT,
    cache_entry_id    BIGINT       NOT NULL,
    source_table_name VARCHAR(200) NOT NULL,
    PRIMARY KEY (id),
    INDEX idx_cst_entry (cache_entry_id),
    INDEX idx_cst_table (source_table_name),
    CONSTRAINT fk_cst_entry FOREIGN KEY (cache_entry_id) REFERENCES RS_CACHE_ENTRY (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CACHE_STATS (
    id           BIGINT   NOT NULL AUTO_INCREMENT,
    period_start DATETIME NOT NULL,
    period_end   DATETIME NOT NULL,
    l1_hits      BIGINT   NOT NULL DEFAULT 0,
    l1_misses    BIGINT   NOT NULL DEFAULT 0,
    l2_hits      BIGINT   NOT NULL DEFAULT 0,
    l2_misses    BIGINT   NOT NULL DEFAULT 0,
    l3_hits      BIGINT   NOT NULL DEFAULT 0,
    l3_misses    BIGINT   NOT NULL DEFAULT 0,
    evictions    BIGINT   NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_cs_period (period_start, period_end)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CACHE_MATERIALIZED_TABLE (
    id           BIGINT       NOT NULL AUTO_INCREMENT,
    query_hash   VARCHAR(64)  NOT NULL,
    version_num  INT          NOT NULL DEFAULT 1,
    state        ENUM('CREATING','ACTIVE','RETIRED','FAILED','DROPPED') NOT NULL DEFAULT 'CREATING',
    table_name   VARCHAR(200) NOT NULL,
    ctas_sql     LONGTEXT     NULL,
    created_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    access_count BIGINT       NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    INDEX idx_cmt_hash (query_hash),
    INDEX idx_cmt_state (state),
    INDEX idx_cmt_table (table_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_CACHE_INVALIDATION_LOG (
    id             BIGINT       NOT NULL AUTO_INCREMENT,
    cache_entry_id BIGINT       NULL,
    reason         VARCHAR(100) NULL,
    created_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_cil_entry (cache_entry_id),
    INDEX idx_cil_created (created_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- VISUAL QUERY BUILDER
-- ============================================================================

CREATE TABLE RS_VQB_PIPELINE (
    id              BIGINT       NOT NULL AUTO_INCREMENT,
    user_id         BIGINT       NOT NULL,
    name            VARCHAR(200) NOT NULL,
    description     TEXT         NULL,
    datasource_id   BIGINT       NULL,
    definition_json JSON         NULL,
    version         INT          NOT NULL DEFAULT 1,
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_vqbp_user (user_id),
    INDEX idx_vqbp_ds (datasource_id),
    CONSTRAINT fk_vqbp_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE,
    CONSTRAINT fk_vqbp_ds FOREIGN KEY (datasource_id) REFERENCES RS_DATASOURCE (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_VQB_PIPELINE_VERSION (
    id              BIGINT NOT NULL AUTO_INCREMENT,
    pipeline_id     BIGINT NOT NULL,
    version_num     INT    NOT NULL,
    definition_json JSON   NULL,
    created_by      BIGINT NULL,
    created_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_vqbpv_pipeline (pipeline_id),
    INDEX idx_vqbpv_creator (created_by),
    CONSTRAINT fk_vqbpv_pipeline FOREIGN KEY (pipeline_id) REFERENCES RS_VQB_PIPELINE (id) ON DELETE CASCADE,
    CONSTRAINT fk_vqbpv_creator FOREIGN KEY (created_by) REFERENCES RS_USER (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_VQB_SHARED_PIPELINE (
    id             BIGINT  NOT NULL AUTO_INCREMENT,
    pipeline_id    BIGINT  NOT NULL,
    shared_user_id BIGINT  NULL,
    shared_role_id BIGINT  NULL,
    can_view       BOOLEAN NOT NULL DEFAULT 1,
    can_edit       BOOLEAN NOT NULL DEFAULT 0,
    created_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_vqbsp_pipeline (pipeline_id),
    INDEX idx_vqbsp_user (shared_user_id),
    INDEX idx_vqbsp_role (shared_role_id),
    CONSTRAINT fk_vqbsp_pipeline FOREIGN KEY (pipeline_id) REFERENCES RS_VQB_PIPELINE (id) ON DELETE CASCADE,
    CONSTRAINT fk_vqbsp_user FOREIGN KEY (shared_user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE RS_VQB_SAVED_FILTER (
    id            BIGINT       NOT NULL AUTO_INCREMENT,
    user_id       BIGINT       NOT NULL,
    name          VARCHAR(200) NOT NULL,
    filter_config JSON         NULL,
    created_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_vqbsf_user (user_id),
    CONSTRAINT fk_vqbsf_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- ESCALATION
-- ============================================================================

CREATE TABLE RS_ALERT_ESCALATION_CHAIN (
    id         BIGINT       NOT NULL AUTO_INCREMENT,
    company_id BIGINT       NULL,
    name       VARCHAR(200) NOT NULL,
    steps      JSON         NULL,
    created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_aec_company (company_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- SFTP
-- ============================================================================

CREATE TABLE RS_SFTP_AUTHORIZED_KEY (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    user_id     BIGINT       NOT NULL,
    public_key  TEXT         NOT NULL,
    fingerprint VARCHAR(100) NULL,
    description TEXT         NULL,
    created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_sak_user (user_id),
    INDEX idx_sak_fingerprint (fingerprint),
    CONSTRAINT fk_sak_user FOREIGN KEY (user_id) REFERENCES RS_USER (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- STORED PROCEDURES: Closure Table Maintenance
-- ============================================================================

-- ---------------------------------------------------------------------------
-- ORG UNIT closure helpers
-- ---------------------------------------------------------------------------

DELIMITER $$

CREATE PROCEDURE sp_org_unit_closure_insert(IN p_id BIGINT, IN p_parent_id BIGINT)
BEGIN
    -- Self-reference (depth 0)
    INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
    VALUES (p_id, p_id, 0);

    -- Copy ancestor paths from parent
    IF p_parent_id IS NOT NULL THEN
        INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT c.ancestor_id, p_id, c.depth + 1
        FROM RS_ORG_UNIT_CLOSURE c
        WHERE c.descendant_id = p_parent_id;
    END IF;
END$$

CREATE PROCEDURE sp_org_unit_closure_delete(IN p_id BIGINT)
BEGIN
    -- Remove all paths where p_id or any of its descendants appear
    DELETE FROM RS_ORG_UNIT_CLOSURE
    WHERE descendant_id IN (
        SELECT d.descendant_id FROM (
            SELECT descendant_id FROM RS_ORG_UNIT_CLOSURE WHERE ancestor_id = p_id
        ) d
    );
END$$

CREATE PROCEDURE sp_org_unit_closure_move(IN p_id BIGINT, IN p_new_parent_id BIGINT)
BEGIN
    -- Step 1: Detach subtree from old ancestors
    DELETE c FROM RS_ORG_UNIT_CLOSURE c
    INNER JOIN RS_ORG_UNIT_CLOSURE d ON c.descendant_id = d.descendant_id
    INNER JOIN RS_ORG_UNIT_CLOSURE a ON a.ancestor_id = c.ancestor_id
    WHERE d.ancestor_id = p_id
      AND a.descendant_id = p_id
      AND c.ancestor_id != p_id;

    -- Step 2: Attach subtree to new ancestors
    IF p_new_parent_id IS NOT NULL THEN
        INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT sup.ancestor_id, sub.descendant_id, sup.depth + sub.depth + 1
        FROM RS_ORG_UNIT_CLOSURE sup
        CROSS JOIN RS_ORG_UNIT_CLOSURE sub
        WHERE sup.descendant_id = p_new_parent_id
          AND sub.ancestor_id = p_id;
    END IF;
END$$

-- ---------------------------------------------------------------------------
-- GROUP closure helpers
-- ---------------------------------------------------------------------------

CREATE PROCEDURE sp_group_closure_insert(IN p_id BIGINT, IN p_parent_id BIGINT)
BEGIN
    INSERT INTO RS_GROUP_CLOSURE (ancestor_id, descendant_id, depth)
    VALUES (p_id, p_id, 0);

    IF p_parent_id IS NOT NULL THEN
        INSERT INTO RS_GROUP_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT c.ancestor_id, p_id, c.depth + 1
        FROM RS_GROUP_CLOSURE c
        WHERE c.descendant_id = p_parent_id;
    END IF;
END$$

CREATE PROCEDURE sp_group_closure_delete(IN p_id BIGINT)
BEGIN
    DELETE FROM RS_GROUP_CLOSURE
    WHERE descendant_id IN (
        SELECT d.descendant_id FROM (
            SELECT descendant_id FROM RS_GROUP_CLOSURE WHERE ancestor_id = p_id
        ) d
    );
END$$

CREATE PROCEDURE sp_group_closure_move(IN p_id BIGINT, IN p_new_parent_id BIGINT)
BEGIN
    DELETE c FROM RS_GROUP_CLOSURE c
    INNER JOIN RS_GROUP_CLOSURE d ON c.descendant_id = d.descendant_id
    INNER JOIN RS_GROUP_CLOSURE a ON a.ancestor_id = c.ancestor_id
    WHERE d.ancestor_id = p_id
      AND a.descendant_id = p_id
      AND c.ancestor_id != p_id;

    IF p_new_parent_id IS NOT NULL THEN
        INSERT INTO RS_GROUP_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT sup.ancestor_id, sub.descendant_id, sup.depth + sub.depth + 1
        FROM RS_GROUP_CLOSURE sup
        CROSS JOIN RS_GROUP_CLOSURE sub
        WHERE sup.descendant_id = p_new_parent_id
          AND sub.ancestor_id = p_id;
    END IF;
END$$

-- ---------------------------------------------------------------------------
-- REPORT FOLDER closure helpers
-- ---------------------------------------------------------------------------

CREATE PROCEDURE sp_report_folder_closure_insert(IN p_id BIGINT, IN p_parent_id BIGINT)
BEGIN
    INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
    VALUES (p_id, p_id, 0);

    IF p_parent_id IS NOT NULL THEN
        INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT c.ancestor_id, p_id, c.depth + 1
        FROM RS_REPORT_FOLDER_CLOSURE c
        WHERE c.descendant_id = p_parent_id;
    END IF;
END$$

CREATE PROCEDURE sp_report_folder_closure_delete(IN p_id BIGINT)
BEGIN
    DELETE FROM RS_REPORT_FOLDER_CLOSURE
    WHERE descendant_id IN (
        SELECT d.descendant_id FROM (
            SELECT descendant_id FROM RS_REPORT_FOLDER_CLOSURE WHERE ancestor_id = p_id
        ) d
    );
END$$

CREATE PROCEDURE sp_report_folder_closure_move(IN p_id BIGINT, IN p_new_parent_id BIGINT)
BEGIN
    DELETE c FROM RS_REPORT_FOLDER_CLOSURE c
    INNER JOIN RS_REPORT_FOLDER_CLOSURE d ON c.descendant_id = d.descendant_id
    INNER JOIN RS_REPORT_FOLDER_CLOSURE a ON a.ancestor_id = c.ancestor_id
    WHERE d.ancestor_id = p_id
      AND a.descendant_id = p_id
      AND c.ancestor_id != p_id;

    IF p_new_parent_id IS NOT NULL THEN
        INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT sup.ancestor_id, sub.descendant_id, sup.depth + sub.depth + 1
        FROM RS_REPORT_FOLDER_CLOSURE sup
        CROSS JOIN RS_REPORT_FOLDER_CLOSURE sub
        WHERE sup.descendant_id = p_new_parent_id
          AND sub.ancestor_id = p_id;
    END IF;
END$$

-- ---------------------------------------------------------------------------
-- DASHBOARD FOLDER closure helpers
-- ---------------------------------------------------------------------------

CREATE PROCEDURE sp_dashboard_folder_closure_insert(IN p_id BIGINT, IN p_parent_id BIGINT)
BEGIN
    INSERT INTO RS_DASHBOARD_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
    VALUES (p_id, p_id, 0);

    IF p_parent_id IS NOT NULL THEN
        INSERT INTO RS_DASHBOARD_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT c.ancestor_id, p_id, c.depth + 1
        FROM RS_DASHBOARD_FOLDER_CLOSURE c
        WHERE c.descendant_id = p_parent_id;
    END IF;
END$$

CREATE PROCEDURE sp_dashboard_folder_closure_delete(IN p_id BIGINT)
BEGIN
    DELETE FROM RS_DASHBOARD_FOLDER_CLOSURE
    WHERE descendant_id IN (
        SELECT d.descendant_id FROM (
            SELECT descendant_id FROM RS_DASHBOARD_FOLDER_CLOSURE WHERE ancestor_id = p_id
        ) d
    );
END$$

CREATE PROCEDURE sp_dashboard_folder_closure_move(IN p_id BIGINT, IN p_new_parent_id BIGINT)
BEGIN
    DELETE c FROM RS_DASHBOARD_FOLDER_CLOSURE c
    INNER JOIN RS_DASHBOARD_FOLDER_CLOSURE d ON c.descendant_id = d.descendant_id
    INNER JOIN RS_DASHBOARD_FOLDER_CLOSURE a ON a.ancestor_id = c.ancestor_id
    WHERE d.ancestor_id = p_id
      AND a.descendant_id = p_id
      AND c.ancestor_id != p_id;

    IF p_new_parent_id IS NOT NULL THEN
        INSERT INTO RS_DASHBOARD_FOLDER_CLOSURE (ancestor_id, descendant_id, depth)
        SELECT sup.ancestor_id, sub.descendant_id, sup.depth + sub.depth + 1
        FROM RS_DASHBOARD_FOLDER_CLOSURE sup
        CROSS JOIN RS_DASHBOARD_FOLDER_CLOSURE sub
        WHERE sup.descendant_id = p_new_parent_id
          AND sub.ancestor_id = p_id;
    END IF;
END$$

DELIMITER ;

-- ============================================================================
-- TRIGGERS: Auto-maintain closure tables on INSERT/UPDATE/DELETE
-- ============================================================================

DELIMITER $$

-- ORG UNIT triggers
CREATE TRIGGER trg_org_unit_after_insert AFTER INSERT ON RS_ORG_UNIT
FOR EACH ROW
BEGIN
    CALL sp_org_unit_closure_insert(NEW.id, NEW.parent_id);
END$$

CREATE TRIGGER trg_org_unit_after_update AFTER UPDATE ON RS_ORG_UNIT
FOR EACH ROW
BEGIN
    IF OLD.parent_id <=> NEW.parent_id THEN
        -- parent unchanged, nothing to do
        BEGIN END;
    ELSE
        CALL sp_org_unit_closure_move(NEW.id, NEW.parent_id);
    END IF;
END$$

CREATE TRIGGER trg_org_unit_before_delete BEFORE DELETE ON RS_ORG_UNIT
FOR EACH ROW
BEGIN
    CALL sp_org_unit_closure_delete(OLD.id);
END$$

-- REPORT FOLDER triggers
CREATE TRIGGER trg_report_folder_after_insert AFTER INSERT ON RS_REPORT_FOLDER
FOR EACH ROW
BEGIN
    CALL sp_report_folder_closure_insert(NEW.id, NEW.parent_id);
END$$

CREATE TRIGGER trg_report_folder_after_update AFTER UPDATE ON RS_REPORT_FOLDER
FOR EACH ROW
BEGIN
    IF OLD.parent_id <=> NEW.parent_id THEN
        BEGIN END;
    ELSE
        CALL sp_report_folder_closure_move(NEW.id, NEW.parent_id);
    END IF;
END$$

CREATE TRIGGER trg_report_folder_before_delete BEFORE DELETE ON RS_REPORT_FOLDER
FOR EACH ROW
BEGIN
    CALL sp_report_folder_closure_delete(OLD.id);
END$$

-- DASHBOARD FOLDER triggers
CREATE TRIGGER trg_dashboard_folder_after_insert AFTER INSERT ON RS_DASHBOARD_FOLDER
FOR EACH ROW
BEGIN
    CALL sp_dashboard_folder_closure_insert(NEW.id, NEW.parent_id);
END$$

CREATE TRIGGER trg_dashboard_folder_after_update AFTER UPDATE ON RS_DASHBOARD_FOLDER
FOR EACH ROW
BEGIN
    IF OLD.parent_id <=> NEW.parent_id THEN
        BEGIN END;
    ELSE
        CALL sp_dashboard_folder_closure_move(NEW.id, NEW.parent_id);
    END IF;
END$$

CREATE TRIGGER trg_dashboard_folder_before_delete BEFORE DELETE ON RS_DASHBOARD_FOLDER
FOR EACH ROW
BEGIN
    CALL sp_dashboard_folder_closure_delete(OLD.id);
END$$

DELIMITER ;

-- ============================================================================
-- Restore settings
-- ============================================================================

SET SQL_MODE=@OLD_SQL_MODE;
SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS;
