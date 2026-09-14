# JEWEL One Click Restore V2

This build is designed for **real local SQL Server restore**. It does not treat the backup folder as the restored database location.

## Correct restore behavior

The application automatically detects the selected local SQL Server instance's own:

- DATA directory
- LOG directory
- BACKUP directory

The actual restored database files are created inside the SQL Server instance's DATA/LOG locations using `RESTORE DATABASE ... WITH MOVE`.

A success message is shown only after the program verifies all of the following:

1. The database exists in the selected SQL Server instance.
2. The database state is `ONLINE`.
3. SQL Server reports physical MDF/NDF/LDF files.
4. Those physical files are inside the selected SQL instance's detected DATA/LOG locations.
5. The application can open a Windows-authenticated connection directly to the restored database.
6. A query against `sys.tables` succeeds.

If verification fails, the program reports **failure**, not success.

## One-click local workflow

1. Start `JewelOneClickRestore.exe`.
2. Installed local SQL Server instances are detected automatically from local services/registry.
3. A running instance is selected automatically when possible.
4. Select a `.BAK` or `.ZIP` backup.
5. ZIP backups are extracted locally without internet.
6. The app reads `RESTORE HEADERONLY` and `RESTORE FILELISTONLY`.
7. The database name stored inside the backup is automatically selected as the target database.
8. The SQL Server DATA and LOG locations are displayed automatically.
9. Click **RESTORE DATABASE** once.
10. Confirm replacement/creation.
11. Restore runs locally and asynchronously with progress.
12. The application verifies the restored database before showing success.

## Backup access reliability

SQL Server reads backup files under the SQL Server service account, not under the desktop application's user account.

V2 handles this locally by:

- running with Administrator elevation,
- granting best-effort read access to the selected backup,
- testing the backup with SQL Server,
- and, when SQL Server cannot read the original path, automatically copying the BAK to the selected SQL instance's local BACKUP/DATA staging location and retrying.

The staging copy is deleted after the restore. The original BAK or ZIP is never deleted or modified.

## ZIP support

`.ZIP` is supported using built-in .NET compression. No WinRAR, 7-Zip, PowerShell, internet, or external program is required.

If one BAK is inside the ZIP, it is selected automatically. If multiple BAK files are found, the user selects one.

## .BAK double-click

Click **Enable .BAK Double-Click** inside the application once.

After that, double-clicking a `.bak` file opens JEWEL One Click Restore with that backup already loaded. The application also adds a **Restore with JEWEL One Click Restore** context-menu action for `.bak` and `.zip` files.

ZIP's normal Windows double-click/extraction behavior is not replaced.

## Offline operation

After publishing the self-contained EXE, the restore application requires **no internet connection** and does not require a separate .NET runtime on the restore PC.

The local PC still needs:

- an installed SQL Server Database Engine instance,
- that SQL Server service running,
- a Windows user with sufficient SQL Server restore permission.

## Build

Open `JewelOneClickRestore.sln` in Visual Studio 2022/2026, or from Developer PowerShell run:

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Expected published EXE with the current x64 project configuration:

```text
bin\x64\Release\net8.0-windows\win-x64\publish\JewelOneClickRestore.exe
```

The app requests Administrator elevation at startup to improve local SQL Server backup-file permission handling.

## Safety

- Never runs `DROP DATABASE`.
- Never deletes the original ZIP/BAK.
- Uses Windows Authentication only.
- No SQL passwords are stored.
- Existing database replacement requires confirmation.
- Existing database is put into `SINGLE_USER WITH ROLLBACK IMMEDIATE` only for replacement.
- A best-effort `MULTI_USER` reset is always attempted afterward.
- SQL instance names are dynamically detected; no SQLEXPRESS instance names are hard-coded.
- Restore destination physical files are generated from the selected SQL Server instance's actual DATA/LOG folders.

## Logs

Logs are stored at:

```text
%LOCALAPPDATA%\JewelOneClickRestore\Logs\
```

The log records detected instances, SQL paths, backup metadata, MOVE paths, restore progress, restore verification, and errors.
