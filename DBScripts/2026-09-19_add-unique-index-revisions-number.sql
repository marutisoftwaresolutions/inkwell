-- DBScripts/2026-09-19_add-unique-index-revisions-number.sql
-- Change: Revision numbers become unique per entity — UX_Revisions_Entity_Number on
--         Revisions (EntityType, EntityId, Number).
-- Why:    RevisionRepository.CreateAsync computed Number as MAX(Number)+1 in a step
--         separate from the INSERT, so two concurrent saves of the same post could take
--         the same number. The repository now computes the number inside the INSERT under
--         UPDLOCK/HOLDLOCK; this index is the hard guarantee, and a collision (2601/2627)
--         is retried once.
-- Schema impact: one unique index; no column change. Should duplicates already exist,
--         only the entities that carry one are renumbered first, in a deterministic order
--         (Number, CreatedAt, Id); every other entity keeps its numbers untouched.
-- Also in: MigrationService.cs (startup upgrade) and init.sql section 38 (fresh install).
-- Idempotent: safe to run more than once.
SET NOCOUNT ON;

IF OBJECT_ID('Revisions') IS NULL
BEGIN
    PRINT 'Revisions table not present — nothing to do (init.sql / MigrationService create it with the index).';
END
ELSE IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('Revisions') AND name = 'UX_Revisions_Entity_Number')
BEGIN
    PRINT 'UX_Revisions_Entity_Number already exists — skipped.';
END
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM Revisions GROUP BY EntityType, EntityId, Number HAVING COUNT(*) > 1)
    BEGIN
        ;WITH d AS (
            SELECT r.Number,
                   NewNumber = ROW_NUMBER() OVER (PARTITION BY r.EntityType, r.EntityId ORDER BY r.Number, r.CreatedAt, r.Id)
            FROM Revisions r
            WHERE EXISTS (SELECT 1 FROM Revisions x
                          WHERE x.EntityType = r.EntityType AND x.EntityId = r.EntityId
                          GROUP BY x.Number HAVING COUNT(*) > 1)
        )
        UPDATE d SET Number = NewNumber WHERE Number <> NewNumber;
        PRINT CAST(@@ROWCOUNT AS NVARCHAR(20)) + ' revision row(s) renumbered to clear duplicate numbers.';
    END

    CREATE UNIQUE INDEX UX_Revisions_Entity_Number ON Revisions (EntityType, EntityId, Number);
    PRINT 'UX_Revisions_Entity_Number created.';
END
