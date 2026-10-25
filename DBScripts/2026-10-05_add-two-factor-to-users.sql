-- =============================================================================
-- 2026-10-05_add-two-factor-to-users.sql
-- Change: Two-factor sign-in with an authenticator app (v1.0.8, ADR-0021).
--   Users.TwoFactorSecretProtected  NVARCHAR(1000) NULL  TOTP secret, Data-Protection-encrypted
--   Users.TwoFactorEnabledAt        DATETIME2 NULL       when the user turned it on (NULL = off)
--   Users.TwoFactorRecoveryHashes   NVARCHAR(MAX) NULL   JSON array of SHA-256 hashes of unused recovery codes
--   Users.TwoFactorLastStep         BIGINT NULL          last accepted 30-second step (blocks code replay)
-- Schema impact: four nullable columns; existing users keep signing in with a password only.
-- Idempotent: each column is added only when missing.
-- =============================================================================

IF COL_LENGTH('Users', 'TwoFactorSecretProtected') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorSecretProtected NVARCHAR(1000) NULL;
    PRINT '  [+] Users.TwoFactorSecretProtected added.';
END
ELSE PRINT '  [=] Users.TwoFactorSecretProtected already exists — skipped.';

IF COL_LENGTH('Users', 'TwoFactorEnabledAt') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorEnabledAt DATETIME2 NULL;
    PRINT '  [+] Users.TwoFactorEnabledAt added.';
END
ELSE PRINT '  [=] Users.TwoFactorEnabledAt already exists — skipped.';

IF COL_LENGTH('Users', 'TwoFactorRecoveryHashes') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorRecoveryHashes NVARCHAR(MAX) NULL;
    PRINT '  [+] Users.TwoFactorRecoveryHashes added.';
END
ELSE PRINT '  [=] Users.TwoFactorRecoveryHashes already exists — skipped.';

IF COL_LENGTH('Users', 'TwoFactorLastStep') IS NULL
BEGIN
    ALTER TABLE Users ADD TwoFactorLastStep BIGINT NULL;
    PRINT '  [+] Users.TwoFactorLastStep added.';
END
ELSE PRINT '  [=] Users.TwoFactorLastStep already exists — skipped.';
