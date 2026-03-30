-- ============================================================================
-- DaisyReportServer - Seed Data Migration V002
-- MySQL 8.0 | InnoDB | utf8mb4_unicode_ci
-- ============================================================================
-- Seeds the initial data required for the system to function:
-- users, groups, permissions, folders, configuration, and theme.
-- ============================================================================

SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0;
SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0;
SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='STRICT_TRANS_TABLES,NO_ENGINE_SUBSTITUTION';

-- ============================================================================
-- 1. SCHEMA VERSION ENTRY
-- ============================================================================

INSERT INTO RS_SCHEMA_VERSION (version, major, minor, patch, description, script_name, installed_by)
VALUES (2, 0, 0, 2, 'Seed data - users, groups, permissions, folders, config', 'V002__seed_data.sql', 'migration');

-- ============================================================================
-- 2. ROOT ADMIN USER
-- ============================================================================
-- Password: 'DaisyAdmin2026!' hashed with Argon2id
-- (The actual app will verify with Argon2id library)

INSERT INTO RS_USER (username, password_hash, email, firstname, lastname, enabled)
VALUES ('admin',
        '$argon2id$v=19$m=65536,t=3,p=4$YWRtaW5zYWx0MTIzNA$k8VvdpLEOGKhMFnMT2qF1GNzI3v6ECzKaPL4kcKciPE',
        'admin@daisy.co.za', 'System', 'Administrator', 1);

-- ============================================================================
-- 3. SYSTEM USER (for background jobs / scheduler)
-- ============================================================================
-- This account cannot be logged into interactively; password is a placeholder.

INSERT INTO RS_USER (username, password_hash, email, firstname, lastname, enabled)
VALUES ('system',
        '$argon2id$v=19$m=65536,t=3,p=4$c3lzdGVtc2FsdDEyMzQ$0000000000000000000000000000000000000000000',
        'system@daisy.co.za', 'System', 'Service', 1);

-- ============================================================================
-- 4. ROOT ORGANIZATIONAL UNIT
-- ============================================================================

INSERT INTO RS_ORG_UNIT (name, description, parent_id)
VALUES ('Root', 'Root organizational unit', NULL);

-- Self-referencing closure entry
INSERT INTO RS_ORG_UNIT_CLOSURE (ancestor_id, descendant_id, depth)
VALUES (1, 1, 0);

-- ============================================================================
-- 5. DEFAULT GROUPS
-- ============================================================================

INSERT INTO RS_GROUP (name, description) VALUES
('Administrators',   'Full system access - all permissions granted'),
('Report Designers', 'Create, edit, and manage reports and datasources'),
('Report Viewers',   'View and execute published reports'),
('Dashboard Users',  'View dashboards and dashboard folders');

-- ============================================================================
-- 6. ADD ADMIN TO ADMINISTRATORS GROUP
-- ============================================================================

INSERT INTO RS_GROUP_MEMBER (group_id, user_id) VALUES (1, 1);

-- ============================================================================
-- 7. PERMISSION DEFINITIONS
-- ============================================================================

INSERT INTO RS_PERMISSION_DEF (code, category, description) VALUES
-- Admin: Users
('admin.users.read',       'admin', 'View user accounts'),
('admin.users.write',      'admin', 'Create and edit user accounts'),
('admin.users.delete',     'admin', 'Delete user accounts'),
-- Admin: Groups
('admin.groups.read',      'admin', 'View groups and memberships'),
('admin.groups.write',     'admin', 'Create and edit groups'),
('admin.groups.delete',    'admin', 'Delete groups'),
-- Admin: Datasources
('admin.datasources.read', 'admin', 'View datasource definitions'),
('admin.datasources.write','admin', 'Create and edit datasources'),
('admin.datasources.delete','admin','Delete datasources'),
-- Admin: Datasinks
('admin.datasinks.read',   'admin', 'View datasink definitions'),
('admin.datasinks.write',  'admin', 'Create and edit datasinks'),
('admin.datasinks.delete', 'admin', 'Delete datasinks'),
-- Admin: Scheduler
('admin.scheduler.read',   'admin', 'View scheduled jobs'),
('admin.scheduler.write',  'admin', 'Create and edit scheduled jobs'),
('admin.scheduler.delete', 'admin', 'Delete scheduled jobs'),
-- Admin: Configuration
('admin.config.read',      'admin', 'View system configuration'),
('admin.config.write',     'admin', 'Modify system configuration'),
-- Admin: Audit
('admin.audit.read',       'admin', 'View audit log entries'),
-- Reports
('report.read',            'report', 'View reports'),
('report.write',           'report', 'Create and edit reports'),
('report.delete',          'report', 'Delete reports'),
('report.execute',         'report', 'Execute reports and export data'),
-- Dashboards
('dashboard.read',         'dashboard', 'View dashboards'),
('dashboard.write',        'dashboard', 'Create and edit dashboards'),
('dashboard.delete',       'dashboard', 'Delete dashboards'),
-- Teamspaces
('teamspace.read',         'teamspace', 'View teamspaces and their contents'),
('teamspace.write',        'teamspace', 'Create and edit teamspaces'),
('teamspace.delete',       'teamspace', 'Delete teamspaces'),
('teamspace.admin',        'teamspace', 'Administer teamspace members and settings'),
-- File Server
('fileserver.read',        'fileserver', 'Download files from the file server'),
('fileserver.write',       'fileserver', 'Upload files to the file server'),
('fileserver.delete',      'fileserver', 'Delete files from the file server'),
-- Terminal
('terminal.execute',       'terminal', 'Execute terminal commands on the server'),
-- Super Admin
('system.admin',           'system', 'Unrestricted super-administrator access');

-- ============================================================================
-- 8. DEFAULT ACL - GRANT ADMINISTRATORS ALL PERMISSIONS
-- ============================================================================
-- ACL attached to the Root OU (entity_type=OU, entity_id=1)

INSERT INTO RS_ACL (entity_type, entity_id) VALUES ('OU', 1);

-- Grant wildcard permission '*' to Administrators group (group_id=1)
INSERT INTO RS_ACE (acl_id, principal_type, principal_id, access_type, permission, inherit, position)
VALUES (1, 'GROUP', 1, 'GRANT', '*', 1, 0);

-- ============================================================================
-- 9. DEFAULT FOLDER STRUCTURE
-- ============================================================================

-- Report Folders: Home (1), Library (2), Trash (3)
INSERT INTO RS_REPORT_FOLDER (name, parent_id, description) VALUES
('Home',    NULL, 'Default report landing folder'),
('Library', NULL, 'Shared report library'),
('Trash',   NULL, 'Deleted reports awaiting permanent removal');

-- Report Folder closure entries (self-referencing depth=0)
INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth) VALUES
(1, 1, 0),
(2, 2, 0),
(3, 3, 0);

-- Dashboard Folders: Home (1), Library (2)
INSERT INTO RS_DASHBOARD_FOLDER (name, parent_id) VALUES
('Home',    NULL),
('Library', NULL);

-- Dashboard Folder closure entries (self-referencing depth=0)
INSERT INTO RS_DASHBOARD_FOLDER_CLOSURE (ancestor_id, descendant_id, depth) VALUES
(1, 1, 0),
(2, 2, 0);

-- ============================================================================
-- 10. DEFAULT CONFIGURATION
-- ============================================================================

INSERT INTO RS_CONFIG (config_key, config_value, category, description) VALUES
-- Security: Passwords
('security.password.min_length',           '12',       'security', 'Minimum password length'),
('security.password.max_age_days',         '90',       'security', 'Maximum password age in days before forced reset'),
-- Security: Lockout
('security.lockout.threshold',             '5',        'security', 'Failed login attempts before account lockout'),
('security.lockout.duration_minutes',      '30',       'security', 'Account lockout duration in minutes'),
-- Security: Session
('security.session.timeout_minutes',       '30',       'security', 'Session inactivity timeout in minutes'),
-- System
('system.timezone',                        'UTC',      'system',   'Default server timezone'),
('system.locale',                          'en',       'system',   'Default locale for UI and formatting'),
('system.date_format',                     'yyyy-MM-dd','system',  'Default date display format'),
-- Reports
('report.preview.max_rows',               '50',        'report',   'Maximum rows shown in report preview'),
('report.export.warning_threshold',       '10000',     'report',   'Row count threshold that triggers an export warning'),
('report.export.hard_limit',              '100000',    'report',   'Absolute maximum rows allowed per export'),
-- Scheduler
('scheduler.check_interval_seconds',      '10',        'scheduler','Interval between scheduler polling cycles'),
('scheduler.max_workers',                 '5',         'scheduler','Maximum concurrent scheduler worker threads'),
('scheduler.missed_fire_threshold_minutes','60',        'scheduler','Minutes after which a missed trigger is skipped'),
-- Cache
('cache.l2.default_ttl_seconds',          '3600',      'cache',    'Default TTL for L2 cache entries'),
('cache.l2.max_memory_mb',               '512',        'cache',    'Maximum memory budget for L2 cache in MB');

-- ============================================================================
-- 11. DEFAULT THEME
-- ============================================================================

INSERT INTO RS_THEME (name, config, is_default) VALUES
('Default', JSON_OBJECT(
    'primary',    '#1976D2',
    'secondary',  '#424242',
    'accent',     '#82B1FF',
    'error',      '#FF5252',
    'warning',    '#FB8C00',
    'info',       '#2196F3',
    'success',    '#4CAF50',
    'background', '#FAFAFA',
    'surface',    '#FFFFFF',
    'sidebar',    '#263238',
    'headerFont', 'Inter, Roboto, sans-serif',
    'bodyFont',   'Inter, Roboto, sans-serif',
    'fontSize',   '14',
    'borderRadius','6'
), 1);

-- ============================================================================
-- 12. DEMO DATA (uncomment to seed demo environment)
-- ============================================================================

/*
-- Demo Users (password for all: 'demo' hashed with Argon2id)
INSERT INTO RS_USER (username, password_hash, email, firstname, lastname, enabled) VALUES
('alice',   '$argon2id$v=19$m=65536,t=3,p=4$ZGVtb3NhbHQxMjM0$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 'alice@daisy.co.za',   'Alice',   'Anderson', 1),
('bob',     '$argon2id$v=19$m=65536,t=3,p=4$ZGVtb3NhbHQxMjM0$BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB', 'bob@daisy.co.za',     'Bob',     'Baker',    1),
('charlie', '$argon2id$v=19$m=65536,t=3,p=4$ZGVtb3NhbHQxMjM0$CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC', 'charlie@daisy.co.za', 'Charlie', 'Clark',    1),
('diana',   '$argon2id$v=19$m=65536,t=3,p=4$ZGVtb3NhbHQxMjM0$DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD', 'diana@daisy.co.za',   'Diana',   'Daniels',  1);

-- Demo group memberships
-- alice -> Report Designers, bob -> Report Viewers, charlie -> Dashboard Users, diana -> Report Designers + Dashboard Users
INSERT INTO RS_GROUP_MEMBER (group_id, user_id) VALUES
(2, 3),  -- alice  -> Report Designers
(3, 4),  -- bob    -> Report Viewers
(4, 5),  -- charlie -> Dashboard Users
(2, 6),  -- diana  -> Report Designers
(4, 6);  -- diana  -> Dashboard Users

-- Demo Datasource (MySQL, points to local sample database)
INSERT INTO RS_DATASOURCE (name, description, dtype) VALUES
('Sample Database', 'Demo MySQL datasource for testing reports', 'DATABASE');

INSERT INTO RS_DATABASE_DATASOURCE (datasource_id, driver_class, jdbc_url, username, password_encrypted)
VALUES (1, 'com.mysql.cj.jdbc.Driver', 'jdbc:mysql://localhost:3306/daisy_sample', 'sample_reader', NULL);

-- Demo Report Folder
INSERT INTO RS_REPORT_FOLDER (name, parent_id, description) VALUES
('Demo Reports', 1, 'Sample reports for evaluation');

INSERT INTO RS_REPORT_FOLDER_CLOSURE (ancestor_id, descendant_id, depth) VALUES
(4, 4, 0),
(1, 4, 1);

-- Demo Reports
INSERT INTO RS_REPORT (folder_id, name, description, engine_type, datasource_id, created_by) VALUES
(4, 'Employee List',    'Basic employee listing with department filter',   'DYNAMIC_LIST', 1, 1),
(4, 'Sales Summary',    'Monthly sales summary with date range parameter', 'DYNAMIC_LIST', 1, 1),
(4, 'Inventory Report', 'Current inventory levels by category',            'DYNAMIC_LIST', 1, 1);
*/

-- ============================================================================
-- RESTORE SESSION STATE
-- ============================================================================

SET SQL_MODE=@OLD_SQL_MODE;
SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS;
SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS;
