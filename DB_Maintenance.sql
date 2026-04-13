-- =============================================================================
-- AutoSignals – Database Maintenance Script
-- Run on UAT / Production to resolve the 179 GB size and fragmentation issues
-- identified during the 2026-04 diagnostic.
--
-- SECTIONS:
--   1. Table & index size snapshot (read-only, safe to run anytime)
--   2. Index fragmentation report
--   3. One-time historical data purge (KLineAssetPrices & ErrorLogs)
--   4. Index REBUILD / REORGANIZE
--   5. Reclaim free space (SHRINKFILE)
--   6. Set configurable retention via AdminSettings
--   7. Transaction log bloat fix (SIMPLE recovery + log shrink)
--   8. One-time noisy ErrorLog cleanup (price-fetch + skipping-user rows)
--
-- Run each section separately.  Sections 3–5 will take significant time on a
-- large database; schedule them during a low-traffic maintenance window.
-- =============================================================================

USE AutoSignals;
GO

-- =============================================================================
-- 1. TABLE SIZE SNAPSHOT
-- =============================================================================
SELECT
    t.name                                                       AS TableName,
    CAST(SUM(a.total_pages) * 8.0 / 1024       AS DECIMAL(10,1)) AS TotalMB,
    CAST(SUM(a.used_pages)  * 8.0 / 1024       AS DECIMAL(10,1)) AS UsedMB,
    CAST(SUM(a.total_pages) * 8.0 / 1024 / 1024 AS DECIMAL(10,3)) AS TotalGB,
    SUM(p.rows)                                                  AS ApproxRows
FROM sys.tables t
JOIN sys.indexes         i ON t.object_id = i.object_id
JOIN sys.partitions      p ON i.object_id = p.object_id AND i.index_id = p.index_id
JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.is_ms_shipped = 0
GROUP BY t.name
ORDER BY SUM(a.total_pages) DESC;
GO

-- =============================================================================
-- 2. INDEX FRAGMENTATION REPORT  (only indexes with > 100 pages)
-- =============================================================================
SELECT
    OBJECT_NAME(i.object_id)                                  AS TableName,
    i.name                                                    AS IndexName,
    i.type_desc                                               AS IndexType,
    CAST(s.avg_fragmentation_in_percent AS DECIMAL(5,1))      AS FragPct,
    s.page_count                                              AS Pages
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') s
JOIN sys.indexes i ON s.object_id = i.object_id AND s.index_id = i.index_id
WHERE s.page_count > 100
  AND i.name IS NOT NULL
ORDER BY s.avg_fragmentation_in_percent DESC;
GO

-- =============================================================================
-- 3. ONE-TIME HISTORICAL DATA PURGE
--
-- The app now prunes KLineAssetPrices in batches every 5 minutes (90-day
-- retention by default).  Run this section ONCE on UAT to clear the backlog
-- of pre-retention rows without waiting for the service to chip through them.
--
-- Estimated time: several minutes to hours depending on data volume.
-- =============================================================================

-- 3a. KLineAssetPrices — delete everything older than 90 days in 10k batches
PRINT 'Starting KLineAssetPrices historical purge...';
DECLARE @KlineCutoff  DATETIME2 = DATEADD(DAY, -90, SYSUTCDATETIME());
DECLARE @KlineDeleted INT       = 1;
DECLARE @KlineTotal   INT       = 0;

WHILE @KlineDeleted > 0
BEGIN
    DELETE TOP (10000) FROM KLineAssetPrices WHERE [Time] < @KlineCutoff;
    SET @KlineDeleted = @@ROWCOUNT;
    SET @KlineTotal   = @KlineTotal + @KlineDeleted;
    IF @KlineTotal % 100000 = 0
        PRINT CONCAT('  KLine rows deleted so far: ', @KlineTotal);
END
PRINT CONCAT('KLineAssetPrices purge complete. Total deleted: ', @KlineTotal);
GO

-- 3b. ErrorLogs — keep last 90 days
PRINT 'Starting ErrorLogs historical purge...';
DECLARE @ErrCutoff  DATETIME2 = DATEADD(DAY, -90, SYSUTCDATETIME());
DECLARE @ErrDeleted INT       = 1;
DECLARE @ErrTotal   INT       = 0;

WHILE @ErrDeleted > 0
BEGIN
    DELETE TOP (5000) FROM ErrorLogs WHERE [Timestamp] < @ErrCutoff;
    SET @ErrDeleted = @@ROWCOUNT;
    SET @ErrTotal   = @ErrTotal + @ErrDeleted;
END
PRINT CONCAT('ErrorLogs purge complete. Total deleted: ', @ErrTotal);
GO

-- 3c. Analytics — keep last 365 days (optional; safe to skip)
-- DECLARE @AnCutoff DATETIME2 = DATEADD(DAY, -365, SYSUTCDATETIME());
-- DELETE FROM Analytics WHERE [Date] < @AnCutoff;
-- GO

-- =============================================================================
-- 4. INDEX REBUILD / REORGANIZE
--
-- Rebuild indexes with > 30 % fragmentation; reorganize those 10–30 %.
-- REBUILD acquires a schema-modification lock for the duration — schedule
-- during low-traffic hours.  Online rebuilds (ONLINE = ON) are supported on
-- SQL Server Enterprise.
-- =============================================================================

-- Critically fragmented (> 30 %) — REBUILD
ALTER INDEX PK_Signals                        ON Signals             REBUILD WITH (ONLINE = OFF);
ALTER INDEX PK_SignalPerformances             ON SignalPerformances   REBUILD WITH (ONLINE = OFF);
ALTER INDEX IX_KLineAssetPrices_Symbol_Type_Time ON KLineAssetPrices REBUILD WITH (ONLINE = OFF);
ALTER INDEX PK_GeneralAssetPrices             ON GeneralAssetPrices  REBUILD WITH (ONLINE = OFF);

-- Mildly fragmented (10–30 %) — REORGANIZE (online, low impact)
ALTER INDEX PK_BinanceRemovedAssets           ON BinanceRemovedAssets  REORGANIZE;
ALTER INDEX PK_KuCoinRemovedAssets            ON KuCoinRemovedAssets   REORGANIZE;
GO

-- Update statistics after rebuilds
UPDATE STATISTICS Signals;
UPDATE STATISTICS SignalPerformances;
UPDATE STATISTICS KLineAssetPrices;
UPDATE STATISTICS ErrorLogs;
GO

-- =============================================================================
-- 5. RECLAIM FREE SPACE  (run AFTER purge + rebuild)
--
-- SHRINKFILE reclaims OS-level disk space.  It causes temporary fragmentation
-- so always rebuild indexes AFTER shrinking if you re-run section 4.
-- Replace 'AutoSignals' and 'AutoSignals_log' with your actual logical file names
-- (query sys.database_files to confirm).
-- =============================================================================

-- Check current file sizes / free space first
SELECT name, physical_name,
       CAST(size * 8.0 / 1024 AS DECIMAL(10,1))            AS SizeMB,
       CAST(FILEPROPERTY(name,'SpaceUsed') * 8.0/1024 AS DECIMAL(10,1)) AS UsedMB
FROM sys.database_files;
GO

-- Shrink data file to leave 10 % free
-- DBCC SHRINKFILE (N'AutoSignals',     TRUNCATEONLY);

-- Shrink log file (only safe after a full backup)
-- DBCC SHRINKFILE (N'AutoSignals_log', 1);
GO

-- =============================================================================
-- 6. CONFIGURABLE RETENTION SETTINGS
--
-- These are read by the application on every price-service cycle.
-- Change the values to suit your retention requirements.
-- =============================================================================

-- KLine candle history (days).  Default 90.  Minimum recommended: 30.
MERGE AdminSettings AS target
USING (VALUES ('KLineRetentionDays', '90')) AS src (K, V)
ON target.[Key] = src.K
WHEN MATCHED THEN UPDATE SET target.[Value] = src.V
WHEN NOT MATCHED THEN INSERT ([Key], [Value]) VALUES (src.K, src.V);

-- Error log retention (days).  Reduced from 90 to 30 — high-volume environment.
MERGE AdminSettings AS target
USING (VALUES ('ErrorLogRetentionDays', '30')) AS src (K, V)
ON target.[Key] = src.K
WHEN MATCHED THEN UPDATE SET target.[Value] = src.V
WHEN NOT MATCHED THEN INSERT ([Key], [Value]) VALUES (src.K, src.V);
GO

-- =============================================================================
-- 7. TRANSACTION LOG BLOAT FIX
--
-- Root cause: FULL recovery model with no log backups causes unbounded .ldf
-- growth.  On a DEV/UAT environment with no point-in-time restore requirement,
-- switch to SIMPLE recovery so the log auto-truncates at each checkpoint.
--
-- FOR PRODUCTION: keep FULL recovery but schedule regular log backups instead:
--   BACKUP LOG AutoSignals TO DISK = 'AutoSignals_log.bak';
--
-- Applied on 2026-04-11 — .ldf shrunk from 160 GB to 136 MB.
-- =============================================================================

-- Check current recovery model
SELECT name, recovery_model_desc FROM sys.databases WHERE name = 'AutoSignals';
GO

-- Switch to SIMPLE (DEV/UAT only)
ALTER DATABASE AutoSignals SET RECOVERY SIMPLE;
GO
CHECKPOINT;
GO

-- Verify log file size before shrink
SELECT name,
       CAST(size * 8.0 / 1024        AS DECIMAL(10,1)) AS SizeMB,
       CAST(FILEPROPERTY(name,'SpaceUsed') * 8.0/1024 AS DECIMAL(10,1)) AS UsedMB
FROM sys.database_files WHERE type_desc = 'LOG';
GO

-- Shrink log file to 100 MB target
DBCC SHRINKFILE (AutoSignals_log, 100);
GO

-- Verify after shrink
SELECT name,
       CAST(size * 8.0 / 1024        AS DECIMAL(10,1)) AS SizeMB
FROM sys.database_files WHERE type_desc = 'LOG';
GO

-- =============================================================================
-- 8. ONE-TIME NOISY ERRORLOG CLEANUP
--
-- Removes rows generated by two known high-frequency but non-critical sources:
--   a) Price-fetch failures for dead/delisted symbols (VFY/USDT, HMSTR/USDT …)
--      These are now logged at ILogger.LogWarning only (ErrorLogService removed).
--   b) "Skipping user" messages for users without an exchange configured.
--      These are expected business-logic paths, not errors.
--
-- Applied on 2026-04-11 — cleaned ~65,000 rows (67K → 1.4K).
-- Run once if re-deploying to a new environment that has accumulated these rows.
-- =============================================================================

PRINT 'Cleaning price-fetch noise rows...';
DECLARE @PfDeleted INT = 1;
DECLARE @PfTotal   INT = 0;
WHILE @PfDeleted > 0
BEGIN
    DELETE TOP(5000) FROM ErrorLogs WHERE Message LIKE 'Failed to fetch price%';
    SET @PfDeleted = @@ROWCOUNT;
    SET @PfTotal   = @PfTotal + @PfDeleted;
END
PRINT CONCAT('Price-fetch rows deleted: ', @PfTotal);
GO

PRINT 'Cleaning skipping-user noise rows...';
DECLARE @SkDeleted INT = 1;
DECLARE @SkTotal   INT = 0;
WHILE @SkDeleted > 0
BEGIN
    DELETE TOP(5000) FROM ErrorLogs WHERE Message LIKE 'Skipping user%';
    SET @SkDeleted = @@ROWCOUNT;
    SET @SkTotal   = @SkTotal + @SkDeleted;
END
PRINT CONCAT('Skipping-user rows deleted: ', @SkTotal);
GO

SELECT COUNT(*) AS RemainingErrorLogRows FROM ErrorLogs;
GO

-- =============================================================================
-- Done.  Re-run section 1 to confirm the size reduction.
-- =============================================================================
