/*
    Scheduled backups for IsHauntedDb.

    WHY THIS EXISTS. On 2026-09-11 this database had EIGHT backups in its whole life, every one of
    them taken by hand, every one COPY_ONLY, and nothing scheduled at all - SQL Agent was stopped
    and set to Manual. Worse, copy-only backups never start a log chain, so although the database
    reports FULL recovery it behaved as SIMPLE: no point-in-time recovery existed, and the gaps
    between hand-taken backups ran to four days.

    WHAT THIS GIVES. A nightly full and a log backup every fifteen minutes, so the worst case
    becomes fifteen minutes of loss rather than four days.

    WHERE. S:\ishaunted-backups, deliberately NOT the C: drive the .mdf and .ldf live on - a backup
    beside the thing it protects survives a mistake but not a failed disk. NT Service\MSSQLSERVER
    is granted Modify there; the service account, not the person running this.

    CHECKSUM on every backup, and RESTORE VERIFYONLY is how these were proved rather than assumed.
    An unverified backup is a belief, not a backup.

    RETENTION is fourteen days for both, via xp_delete_file - which reads backup headers and so
    declines to delete anything that is not a backup.

    Idempotent: existing jobs of these names are dropped and rebuilt, so this can be re-run.
*/

USE [msdb];
GO

SET NOCOUNT ON;
GO

/* ---------- Full backup, nightly ---------------------------------------------------------- */

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'IsHauntedDb - Full backup')
    EXEC msdb.dbo.sp_delete_job @job_name = N'IsHauntedDb - Full backup', @delete_unused_schedule = 1;
GO

EXEC msdb.dbo.sp_add_job
     @job_name    = N'IsHauntedDb - Full backup',
     @description = N'Nightly full backup of IsHauntedDb to S:\ishaunted-backups\Full, 14 day retention.',
     @enabled     = 1;
GO

EXEC msdb.dbo.sp_add_jobstep
     @job_name       = N'IsHauntedDb - Full backup',
     @step_name      = N'Back up',
     @subsystem      = N'TSQL',
     @database_name  = N'master',
     @on_success_action = 3,          -- go to the next step (the tidy-up)
     @on_fail_action    = 2,          -- quit reporting failure
     @command = N'
DECLARE @file NVARCHAR(400) =
    N''S:\ishaunted-backups\Full\IsHauntedDb_''
    + CONVERT(NVARCHAR(8), GETDATE(), 112) + N''_''
    + REPLACE(CONVERT(NVARCHAR(8), GETDATE(), 108), '':'', '''') + N''.bak'';

BACKUP DATABASE [IsHauntedDb] TO DISK = @file
    WITH INIT, COMPRESSION, CHECKSUM;

-- A backup nobody has read back is a belief, not a backup.
RESTORE VERIFYONLY FROM DISK = @file WITH CHECKSUM;
';
GO

EXEC msdb.dbo.sp_add_jobstep
     @job_name       = N'IsHauntedDb - Full backup',
     @step_name      = N'Remove backups older than 14 days',
     @subsystem      = N'TSQL',
     @database_name  = N'master',
     @on_success_action = 1,          -- quit reporting success
     @on_fail_action    = 2,
     @command = N'
DECLARE @cutoff NVARCHAR(30) = CONVERT(NVARCHAR(19), DATEADD(day, -14, GETDATE()), 126);
EXEC master.dbo.xp_delete_file 0, N''S:\ishaunted-backups\Full'', N''bak'', @cutoff, 0;
';
GO

EXEC msdb.dbo.sp_add_jobschedule
     @job_name        = N'IsHauntedDb - Full backup',
     @name            = N'Nightly 02:00',
     @freq_type       = 4,            -- daily
     @freq_interval   = 1,
     @active_start_time = 20000;      -- 02:00:00
GO

EXEC msdb.dbo.sp_add_jobserver @job_name = N'IsHauntedDb - Full backup';
GO

/* ---------- Log backup, every fifteen minutes ---------------------------------------------- */

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'IsHauntedDb - Log backup')
    EXEC msdb.dbo.sp_delete_job @job_name = N'IsHauntedDb - Log backup', @delete_unused_schedule = 1;
GO

EXEC msdb.dbo.sp_add_job
     @job_name    = N'IsHauntedDb - Log backup',
     @description = N'Transaction log backup every 15 minutes. This is what makes point-in-time recovery possible, and what keeps the log from growing without end.',
     @enabled     = 1;
GO

EXEC msdb.dbo.sp_add_jobstep
     @job_name       = N'IsHauntedDb - Log backup',
     @step_name      = N'Back up the log',
     @subsystem      = N'TSQL',
     @database_name  = N'master',
     @on_success_action = 3,
     @on_fail_action    = 2,
     @command = N'
DECLARE @file NVARCHAR(400) =
    N''S:\ishaunted-backups\Log\IsHauntedDb_''
    + CONVERT(NVARCHAR(8), GETDATE(), 112) + N''_''
    + REPLACE(CONVERT(NVARCHAR(8), GETDATE(), 108), '':'', '''') + N''.trn'';

BACKUP LOG [IsHauntedDb] TO DISK = @file
    WITH INIT, COMPRESSION, CHECKSUM;
';
GO

EXEC msdb.dbo.sp_add_jobstep
     @job_name       = N'IsHauntedDb - Log backup',
     @step_name      = N'Remove log backups older than 14 days',
     @subsystem      = N'TSQL',
     @database_name  = N'master',
     @on_success_action = 1,
     @on_fail_action    = 2,
     @command = N'
DECLARE @cutoff NVARCHAR(30) = CONVERT(NVARCHAR(19), DATEADD(day, -14, GETDATE()), 126);
EXEC master.dbo.xp_delete_file 0, N''S:\ishaunted-backups\Log'', N''trn'', @cutoff, 0;
';
GO

EXEC msdb.dbo.sp_add_jobschedule
     @job_name        = N'IsHauntedDb - Log backup',
     @name            = N'Every 15 minutes',
     @freq_type       = 4,            -- daily
     @freq_interval   = 1,
     @freq_subday_type     = 4,       -- minutes
     @freq_subday_interval = 15,
     @active_start_time = 0,
     @active_end_time   = 235959;
GO

EXEC msdb.dbo.sp_add_jobserver @job_name = N'IsHauntedDb - Log backup';
GO

PRINT 'Backup jobs created. They run only while the SQL Server Agent service is running.';
GO
