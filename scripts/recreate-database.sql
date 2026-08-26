-- Recreates IsHauntedDb and restores the application login's access to it.
--
-- WHY THIS EXISTS (2026-08-26): the `IsHaunted` SQL login can DROP its own database but has no
-- CREATE DATABASE permission on the server, so `dotnet ef database drop --force` succeeds and the
-- matching `database update` then fails with:
--
--     CREATE DATABASE permission denied in database 'master'.  (Error 262)
--
-- Dropping a database also destroys its USER mappings, so creating the database alone is not
-- enough — the login can connect to the server but not to the new database. Both halves are here.
--
-- RUN THIS ON THE SQL SERVER (192.168.1.71) as a login with server-level rights — `sa`, or a
-- Windows account in the sysadmin role. Windows Integrated auth cannot be used from the Mac
-- ("Cannot generate SSPI context"), which is why this is a manual step.

CREATE DATABASE IsHauntedDb;
GO

USE IsHauntedDb;
GO

-- The login already exists at server level; only its mapping into this database was lost.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IsHaunted')
    CREATE USER [IsHaunted] FOR LOGIN [IsHaunted];
GO

-- db_owner because EF migrations create and alter tables, indexes and constraints.
ALTER ROLE db_owner ADD MEMBER [IsHaunted];
GO

-- OPTIONAL, and the reason this whole file was needed. With dbcreator the application login can
-- drop and recreate its own database, so `dotnet ef database drop` / `update` works end to end
-- and nobody has to come back here. Skip it if the login should stay unable to create databases.
-- ALTER SERVER ROLE dbcreator ADD MEMBER [IsHaunted];
-- GO
