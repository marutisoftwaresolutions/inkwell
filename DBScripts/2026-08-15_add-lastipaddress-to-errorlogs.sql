-- DBScripts/2026-08-15_add-lastipaddress-to-errorlogs.sql
-- Change: Adds ErrorLogs.LastIpAddress - the client IP of the most recent request that
--         produced this error signature.
-- Why:    The Error Monitor groups errors by signature but never recorded who sent them,
--         so an operator looking at 200 probe 404s could not act on them. With the IP
--         captured, Admin -> Error Monitor can offer a one-click "Block" for the offending
--         address and the IP Firewall can be operated from the evidence screen.
-- Schema impact: One nullable NVARCHAR(45) column on ErrorLogs. Existing rows stay NULL
--                until the signature is seen again. No data migration required.
-- Idempotent: safe to run repeatedly.

IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('ErrorLogs') AND name = 'LastIpAddress')
BEGIN
    ALTER TABLE ErrorLogs ADD LastIpAddress NVARCHAR(45) NULL;
    PRINT 'Added column ErrorLogs.LastIpAddress.';
END
ELSE
BEGIN
    PRINT 'ErrorLogs.LastIpAddress already exists - no action taken.';
END
GO
