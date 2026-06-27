-- =============================================================================
-- migration-all-changes.sql
-- Blogfront / opticalsoftware.org — Full Production Migration
-- Covers all schema and taxonomy changes from this development sprint.
-- Safe to run multiple times (fully idempotent).
-- Target:  SQL Server 2019+ (or Azure SQL)
-- Run as:  DBO or schema owner on the 'blog' database
-- =============================================================================

PRINT '============================================================';
PRINT 'Blogfront Migration — opticalsoftware.org';
PRINT 'Started: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT '============================================================';

-- ============================================================
-- PART 1 — SCHEMA CHANGES
-- ============================================================

PRINT '';
PRINT '-- Part 1: Schema Changes';
PRINT '';

-- 1.1  Posts.FaqJson
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Posts') AND name = N'FaqJson'
)
BEGIN
    ALTER TABLE Posts ADD FaqJson NVARCHAR(MAX) NULL;
    PRINT '  [+] Posts.FaqJson column added.';
END
ELSE
    PRINT '  [=] Posts.FaqJson already exists — skipped.';

-- 1.2  Users.Credentials
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Users') AND name = N'Credentials'
)
BEGIN
    ALTER TABLE Users ADD Credentials NVARCHAR(200) NULL;
    PRINT '  [+] Users.Credentials column added.';
END
ELSE
    PRINT '  [=] Users.Credentials already exists — skipped.';

-- 1.3  Users.Specialty
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Users') AND name = N'Specialty'
)
BEGIN
    ALTER TABLE Users ADD Specialty NVARCHAR(200) NULL;
    PRINT '  [+] Users.Specialty column added.';
END
ELSE
    PRINT '  [=] Users.Specialty already exists — skipped.';

-- 1.4  Users.LicenseNumber
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Users') AND name = N'LicenseNumber'
)
BEGIN
    ALTER TABLE Users ADD LicenseNumber NVARCHAR(100) NULL;
    PRINT '  [+] Users.LicenseNumber column added.';
END
ELSE
    PRINT '  [=] Users.LicenseNumber already exists — skipped.';

-- 1.5  Categories.Color
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Categories') AND name = N'Color'
)
BEGIN
    ALTER TABLE Categories ADD Color NVARCHAR(50) NULL DEFAULT '#e8f5e9' WITH VALUES;
    PRINT '  [+] Categories.Color column added.';
END
ELSE
    PRINT '  [=] Categories.Color already exists — skipped.';

-- 1.6  Tags.Color
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Tags') AND name = N'Color'
)
BEGIN
    ALTER TABLE Tags ADD Color NVARCHAR(50) NULL DEFAULT '#e3f2fd' WITH VALUES;
    PRINT '  [+] Tags.Color column added.';
END
ELSE
    PRINT '  [=] Tags.Color already exists — skipped.';

-- 1.7  Media.OriginalFileName
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'Media') AND name = N'OriginalFileName'
)
BEGIN
    ALTER TABLE Media ADD OriginalFileName NVARCHAR(1000) NULL;
    PRINT '  [+] Media.OriginalFileName column added.';
END
ELSE
    PRINT '  [=] Media.OriginalFileName already exists — skipped.';

-- 1.8  CustomThemeSettings table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'CustomThemeSettings')
BEGIN
    CREATE TABLE CustomThemeSettings (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        UserId        UNIQUEIDENTIFIER NOT NULL,
        SettingGroup  NVARCHAR(100)    NOT NULL DEFAULT 'general',
        SettingKey    NVARCHAR(200)    NOT NULL,
        SettingType   NVARCHAR(50)     NOT NULL DEFAULT 'text',
        SettingValue  NVARCHAR(MAX)    NULL,
        DefaultValue  NVARCHAR(MAX)    NULL,
        Label         NVARCHAR(200)    NULL,
        Description   NVARCHAR(500)    NULL
    );
    PRINT '  [+] CustomThemeSettings table created.';
END
ELSE
    PRINT '  [=] CustomThemeSettings already exists — skipped.';

-- 1.9  Members table (newsletter subscribers, double opt-in)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Members')
BEGIN
    CREATE TABLE Members (
        Id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Uuid             UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Email            NVARCHAR(320)    NOT NULL,
        Name             NVARCHAR(200)    NULL,
        Note             NVARCHAR(2000)   NULL,
        Status           NVARCHAR(50)     NOT NULL DEFAULT 'pending',   -- pending | confirmed | unsubscribed
        Subscribed       BIT              NOT NULL DEFAULT 0,
        ConfirmToken     NVARCHAR(100)    NOT NULL,
        UnsubscribeToken NVARCHAR(100)    NOT NULL,
        ConfirmedAt      DATETIME2        NULL,
        CreatedAt        DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
        DeletedAt        DATETIME2        NULL,

        CONSTRAINT UQ_Members_Email            UNIQUE (Email),
        CONSTRAINT UQ_Members_ConfirmToken     UNIQUE (ConfirmToken),
        CONSTRAINT UQ_Members_UnsubscribeToken UNIQUE (UnsubscribeToken)
    );
    PRINT '  [+] Members table created.';
END
ELSE
    PRINT '  [=] Members already exists — skipped.';

PRINT '';
PRINT '-- Part 1 complete.';

-- ============================================================
-- PART 2 — OPTICAL SOFTWARE TAXONOMY (10 Categories + 60 Tags)
-- Mirrors OptometryTaxonomySeeder.cs — idempotent, checks by Slug.
-- AuthorId = 00000000-0000-0000-0000-000000000000 (self-hosted, site-wide owner)
-- ============================================================

PRINT '';
PRINT '-- Part 2: Optical Software Taxonomy';
PRINT '';

DECLARE @OwnerId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';

-- ── 2a. Categories ────────────────────────────────────────────────────────────

PRINT '  Seeding categories...';

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'practice-management-software' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Practice Management Software', N'practice-management-software', @OwnerId);
    PRINT '    [+] Practice Management Software';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'ehr-electronic-records' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'EHR & Electronic Records', N'ehr-electronic-records', @OwnerId);
    PRINT '    [+] EHR & Electronic Records';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'digital-imaging-diagnostics' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Digital Imaging & Diagnostics', N'digital-imaging-diagnostics', @OwnerId);
    PRINT '    [+] Digital Imaging & Diagnostics';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'ai-machine-learning-optometry' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'AI & Machine Learning in Optometry', N'ai-machine-learning-optometry', @OwnerId);
    PRINT '    [+] AI & Machine Learning in Optometry';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'teleoptometry-remote-care' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Teleoptometry & Remote Care', N'teleoptometry-remote-care', @OwnerId);
    PRINT '    [+] Teleoptometry & Remote Care';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'contact-lens-fitting-technology' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Contact Lens Fitting Technology', N'contact-lens-fitting-technology', @OwnerId);
    PRINT '    [+] Contact Lens Fitting Technology';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'frame-lens-design-software' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Frame & Lens Design Software', N'frame-lens-design-software', @OwnerId);
    PRINT '    [+] Frame & Lens Design Software';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'billing-revenue-cycle' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Billing & Revenue Cycle', N'billing-revenue-cycle', @OwnerId);
    PRINT '    [+] Billing & Revenue Cycle';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'optical-retail-technology' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Optical Retail Technology', N'optical-retail-technology', @OwnerId);
    PRINT '    [+] Optical Retail Technology';
END

IF NOT EXISTS (SELECT 1 FROM Categories WHERE Slug = N'vision-therapy-technology' AND AuthorId = @OwnerId)
BEGIN
    INSERT INTO Categories (Id, Name, Slug, AuthorId)
    VALUES (NEWID(), N'Vision Therapy Technology', N'vision-therapy-technology', @OwnerId);
    PRINT '    [+] Vision Therapy Technology';
END

PRINT '  Categories done.';

-- ── 2b. Tags ──────────────────────────────────────────────────────────────────

PRINT '  Seeding tags...';

-- EHR & Practice Management Products
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'eyefinity' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Eyefinity', N'eyefinity', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'revolution-ehr' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'RevolutionEHR', N'revolution-ehr', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'compulink' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Compulink', N'compulink', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'crystal-pm' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Crystal PM', N'crystal-pm', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'maximeyes' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'MaximEyes', N'maximeyes', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'officemate' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'OfficeMate', N'officemate', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'imedicware' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'iMedicWare', N'imedicware', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'my-vision-express' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'My Vision Express', N'my-vision-express', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'optimantra' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'OptiMantra', N'optimantra', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'uprise-ehr' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Uprise EHR', N'uprise-ehr', @OwnerId); END

-- Clinical Imaging & Diagnostic Technology
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'oct-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'OCT Software', N'oct-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'retinal-imaging' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Retinal Imaging', N'retinal-imaging', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'corneal-topography' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Corneal Topography', N'corneal-topography', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'wavefront-analysis' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Wavefront Analysis', N'wavefront-analysis', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'fundus-photography' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Fundus Photography', N'fundus-photography', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'aberrometry' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Aberrometry', N'aberrometry', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'digital-slit-lamp' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Digital Slit Lamp', N'digital-slit-lamp', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'visual-field-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Visual Field Software', N'visual-field-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'digital-refraction' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Digital Refraction', N'digital-refraction', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'autorefractor' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Autorefractor', N'autorefractor', @OwnerId); END

-- AI & Machine Learning
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'ai-diagnostics' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'AI Diagnostics', N'ai-diagnostics', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'deep-learning' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Deep Learning', N'deep-learning', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'computer-vision' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Computer Vision', N'computer-vision', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'ai-retinal-screening' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'AI Retinal Screening', N'ai-retinal-screening', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'glaucoma-detection-ai' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Glaucoma Detection AI', N'glaucoma-detection-ai', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'diabetic-retinopathy-ai' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Diabetic Retinopathy AI', N'diabetic-retinopathy-ai', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'predictive-analytics' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Predictive Analytics', N'predictive-analytics', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'clinical-decision-support' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Clinical Decision Support', N'clinical-decision-support', @OwnerId); END

-- Telehealth & Remote Care
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'telemedicine' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Telemedicine', N'telemedicine', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'remote-eye-exam' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Remote Eye Exam', N'remote-eye-exam', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'asynchronous-telehealth' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Asynchronous Telehealth', N'asynchronous-telehealth', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'online-vision-test' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Online Vision Test', N'online-vision-test', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'patient-portal' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Patient Portal', N'patient-portal', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'virtual-consultation' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Virtual Consultation', N'virtual-consultation', @OwnerId); END

-- Contact Lens Fitting Technology
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'scleral-lens-fitting' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Scleral Lens Fitting Software', N'scleral-lens-fitting', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'digital-keratometry' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Digital Keratometry', N'digital-keratometry', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'orthokeratology-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Orthokeratology Software', N'orthokeratology-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'contact-lens-simulation' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Contact Lens Simulation', N'contact-lens-simulation', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'topography-guided-fitting' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Topography-Guided Fitting', N'topography-guided-fitting', @OwnerId); END

-- Frame & Dispensing Technology
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'virtual-try-on' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Virtual Try-On', N'virtual-try-on', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'3d-frame-visualization' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'3D Frame Visualization', N'3d-frame-visualization', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'augmented-reality-eyewear' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Augmented Reality Eyewear', N'augmented-reality-eyewear', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'frame-inventory-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Frame Inventory Software', N'frame-inventory-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'lens-ordering-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Lens Ordering Software', N'lens-ordering-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'lab-integration' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Lab Integration', N'lab-integration', @OwnerId); END

-- Billing & Compliance
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'medical-billing-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Medical Billing Software', N'medical-billing-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'icd-10-coding' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'ICD-10 Coding', N'icd-10-coding', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'insurance-claims' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Insurance Claims', N'insurance-claims', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'hipaa-compliance' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'HIPAA Compliance', N'hipaa-compliance', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'revenue-cycle-management' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Revenue Cycle Management', N'revenue-cycle-management', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'era-eob-processing' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'ERA & EOB Processing', N'era-eob-processing', @OwnerId); END

-- Retail & Point of Sale
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'optical-pos' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Optical POS', N'optical-pos', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'inventory-management' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Inventory Management', N'inventory-management', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'customer-loyalty-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Customer Loyalty Software', N'customer-loyalty-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'ecommerce-optometry' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'eCommerce for Optometry', N'ecommerce-optometry', @OwnerId); END

-- Vision Therapy & Rehabilitation
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'vision-therapy-software' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Vision Therapy Software', N'vision-therapy-software', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'binocular-vision-testing' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Binocular Vision Testing', N'binocular-vision-testing', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'perceptual-learning' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Perceptual Learning', N'perceptual-learning', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'amblyopia-treatment-tech' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Amblyopia Treatment Tech', N'amblyopia-treatment-tech', @OwnerId); END

-- Industry & Standards
IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'hl7-fhir' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'HL7 FHIR', N'hl7-fhir', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'interoperability' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Interoperability', N'interoperability', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'cloud-based-ehr' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Cloud-Based EHR', N'cloud-based-ehr', @OwnerId); END

IF NOT EXISTS (SELECT 1 FROM Tags WHERE Slug = N'practice-analytics' AND AuthorId = @OwnerId)
BEGIN INSERT INTO Tags (Id, Name, Slug, AuthorId) VALUES (NEWID(), N'Practice Analytics', N'practice-analytics', @OwnerId); END

PRINT '  Tags done.';
PRINT '';
PRINT '-- Part 2 complete.';

-- ============================================================
-- PART 3 — VERIFICATION SUMMARY
-- ============================================================

PRINT '';
PRINT '-- Part 3: Verification';
PRINT '';

SELECT 'Schema'         AS Section,
       'Posts.FaqJson'  AS Item,
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Posts') AND name = N'FaqJson')
            THEN 'OK' ELSE 'MISSING' END AS Status
UNION ALL
SELECT 'Schema', 'Users.Credentials',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'Credentials')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Users.Specialty',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'Specialty')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Users.LicenseNumber',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Users') AND name = N'LicenseNumber')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Categories.Color',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Categories') AND name = N'Color')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Tags.Color',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'Tags') AND name = N'Color')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'beacon_Media.OriginalFileName',
       CASE WHEN EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'beacon_Media') AND name = N'OriginalFileName')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'CustomThemeSettings table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'CustomThemeSettings')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Members table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Members')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Newsletters table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Newsletters')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Labels table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Labels')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'MemberLabels table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'MemberLabels')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'MemberNewsletters table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'MemberNewsletters')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Emails table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Emails')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Redirects table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Redirects')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Schema', 'Snippets table',
       CASE WHEN EXISTS (SELECT * FROM sys.tables WHERE name = N'Snippets')
            THEN 'OK' ELSE 'MISSING' END
UNION ALL
SELECT 'Taxonomy', 'Categories (optical software)',
       CAST(COUNT(*) AS NVARCHAR) + ' / 10'
FROM Categories
WHERE AuthorId = '00000000-0000-0000-0000-000000000000'
  AND Slug IN (
      'practice-management-software','ehr-electronic-records',
      'digital-imaging-diagnostics','ai-machine-learning-optometry',
      'teleoptometry-remote-care','contact-lens-fitting-technology',
      'frame-lens-design-software','billing-revenue-cycle',
      'optical-retail-technology','vision-therapy-technology')
UNION ALL
SELECT 'Taxonomy', 'Tags (optical software)',
       CAST(COUNT(*) AS NVARCHAR) + ' / 60'
FROM Tags
WHERE AuthorId = '00000000-0000-0000-0000-000000000000'
  AND Slug IN (
      'eyefinity','revolution-ehr','compulink','crystal-pm','maximeyes',
      'officemate','imedicware','my-vision-express','optimantra','uprise-ehr',
      'oct-software','retinal-imaging','corneal-topography','wavefront-analysis',
      'fundus-photography','aberrometry','digital-slit-lamp','visual-field-software',
      'digital-refraction','autorefractor',
      'ai-diagnostics','deep-learning','computer-vision','ai-retinal-screening',
      'glaucoma-detection-ai','diabetic-retinopathy-ai','predictive-analytics',
      'clinical-decision-support',
      'telemedicine','remote-eye-exam','asynchronous-telehealth','online-vision-test',
      'patient-portal','virtual-consultation',
      'scleral-lens-fitting','digital-keratometry','orthokeratology-software',
      'contact-lens-simulation','topography-guided-fitting',
      'virtual-try-on','3d-frame-visualization','augmented-reality-eyewear',
      'frame-inventory-software','lens-ordering-software','lab-integration',
      'medical-billing-software','icd-10-coding','insurance-claims','hipaa-compliance',
      'revenue-cycle-management','era-eob-processing',
      'optical-pos','inventory-management','customer-loyalty-software','ecommerce-optometry',
      'vision-therapy-software','binocular-vision-testing','perceptual-learning',
      'amblyopia-treatment-tech',
      'hl7-fhir','interoperability','cloud-based-ehr','practice-analytics')
ORDER BY Section, Item;

PRINT '';
PRINT '============================================================';
PRINT 'Verification complete.';
PRINT '============================================================';

-- ============================================================
-- PART 4 — MISSING DOMAIN TABLES
-- Tables defined in Blog.Core/Domain but absent from database.
-- Identified by live schema inspection on 2026-05-16.
-- ============================================================

PRINT '';
PRINT '-- Part 4: Missing Domain Tables';
PRINT '';

-- 4.1  Newsletters
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Newsletters')
BEGIN
    CREATE TABLE Newsletters (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name          NVARCHAR(200)    NOT NULL,
        Slug          NVARCHAR(200)    NOT NULL,
        Description   NVARCHAR(2000)   NULL,
        SenderName    NVARCHAR(200)    NULL,
        SenderEmail   NVARCHAR(320)    NULL,
        SenderReplyTo NVARCHAR(320)    NULL,
        Status        NVARCHAR(50)     NULL DEFAULT 'active',
        CreatedAt     DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_Newsletters_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Newsletters table created.';
END
ELSE
    PRINT '  [=] Newsletters already exists — skipped.';

-- 4.2  Labels  (member segmentation tags, separate from post Tags)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Labels')
BEGIN
    CREATE TABLE Labels (
        Id    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name  NVARCHAR(200)    NOT NULL,
        Slug  NVARCHAR(200)    NOT NULL,

        CONSTRAINT UQ_Labels_Slug UNIQUE (Slug)
    );
    PRINT '  [+] Labels table created.';
END
ELSE
    PRINT '  [=] Labels already exists — skipped.';

-- 4.3  MemberLabels  (many-to-many: Members ↔ Labels)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'MemberLabels')
BEGIN
    CREATE TABLE MemberLabels (
        MemberId UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id) ON DELETE CASCADE,
        LabelId  UNIQUEIDENTIFIER NOT NULL REFERENCES Labels(Id)  ON DELETE CASCADE,

        CONSTRAINT PK_MemberLabels PRIMARY KEY (MemberId, LabelId)
    );
    PRINT '  [+] MemberLabels table created.';
END
ELSE
    PRINT '  [=] MemberLabels already exists — skipped.';

-- 4.4  MemberNewsletters  (many-to-many: Members ↔ Newsletters with Subscribed flag)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'MemberNewsletters')
BEGIN
    CREATE TABLE MemberNewsletters (
        MemberId     UNIQUEIDENTIFIER NOT NULL REFERENCES Members(Id)     ON DELETE CASCADE,
        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE CASCADE,
        Subscribed   BIT              NOT NULL DEFAULT 1,

        CONSTRAINT PK_MemberNewsletters PRIMARY KEY (MemberId, NewsletterId)
    );
    PRINT '  [+] MemberNewsletters table created.';
END
ELSE
    PRINT '  [=] MemberNewsletters already exists — skipped.';

-- 4.5  Emails  (newsletter send log per post)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Emails')
BEGIN
    CREATE TABLE Emails (
        Id           UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        PostId       UNIQUEIDENTIFIER NOT NULL REFERENCES Posts(Id)       ON DELETE CASCADE,
        NewsletterId UNIQUEIDENTIFIER NOT NULL REFERENCES Newsletters(Id) ON DELETE NO ACTION,
        Subject      NVARCHAR(500)    NOT NULL,
        Status       NVARCHAR(50)     NULL DEFAULT 'pending',
        OpensCount   INT              NOT NULL DEFAULT 0,
        ClicksCount  INT              NOT NULL DEFAULT 0,
        SentCount    INT              NOT NULL DEFAULT 0,
        CreatedAt    DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  [+] Emails table created.';
END
ELSE
    PRINT '  [=] Emails already exists — skipped.';

-- 4.6  Redirects  (From capped at 850 chars — nonclustered unique index limit is 1700 bytes)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Redirects')
BEGIN
    CREATE TABLE Redirects (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [From]    NVARCHAR(850)    NOT NULL,
        [To]      NVARCHAR(2000)   NOT NULL,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT UQ_Redirects_From UNIQUE ([From])
    );
    PRINT '  [+] Redirects table created.';
END
ELSE
    PRINT '  [=] Redirects already exists — skipped.';

-- 4.7  Snippets  (reusable content blocks / lexical snippets)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = N'Snippets')
BEGIN
    CREATE TABLE Snippets (
        Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        Name      NVARCHAR(200)    NOT NULL,
        Lexical   NVARCHAR(MAX)    NULL,
        CreatedBy UNIQUEIDENTIFIER NOT NULL REFERENCES Users(Id) ON DELETE NO ACTION,
        CreatedAt DATETIME2        NOT NULL DEFAULT GETUTCDATE()
    );
    PRINT '  [+] Snippets table created.';
END
ELSE
    PRINT '  [=] Snippets already exists — skipped.';

PRINT '';
PRINT '-- Part 4 complete.';

PRINT '';
PRINT '============================================================';
PRINT 'Migration complete: ' + CONVERT(NVARCHAR, GETUTCDATE(), 120) + ' UTC';
PRINT 'Next step: run week1-ehr-articles.sql to seed Week 1 content';
PRINT '============================================================';
