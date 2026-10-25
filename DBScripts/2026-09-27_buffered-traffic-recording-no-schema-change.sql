-- DBScripts/2026-09-27_buffered-traffic-recording-no-schema-change.sql
-- Change: PageViewRepository and CrawlerVisitRepository gain RecordManyAsync (batched INSERT in one
--         transaction), used by the new in-memory TrafficRecorder (ADR-0012). The per-request
--         PageViewRepository.RecordAsync is removed.
-- Schema impact: NONE — the batched INSERTs write the existing PageViews / CrawlerVisits columns.
-- Action required: None. Safe to run any number of times.
PRINT 'No schema migration required for buffered traffic recording.';
