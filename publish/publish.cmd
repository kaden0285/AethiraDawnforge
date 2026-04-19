@echo off
setlocal EnableDelayedExpansion

rem ==================================================================
rem  RimWorld Workshop upload script - Kurin Demigodess (Aethira)
rem
rem  Prerequisites (one-time setup):
rem   1. Download steamcmd from:
rem        https://developer.valvesoftware.com/wiki/SteamCMD
rem   2. Extract steamcmd.exe to C:\steamcmd\ (or edit STEAMCMD below)
rem   3. Run steamcmd.exe once manually so it self-updates
rem   4. In steamcmd, run:  login <your_steam_username>
rem      Enter password + 2FA. Cached creds let future runs skip 2FA.
rem
rem  What this does:
rem   - Copies ONLY the shipping mod folders to a temp staging dir.
rem     Source code, docs site, git history, CLAUDE.md, and local
rem     tooling are excluded from the Workshop upload.
rem   - Generates a workshop.vdf pointing at the staging dir.
rem   - Invokes steamcmd to push the staging content to Workshop item
rem     3707968015 (existing Aethira Dawnforge listing).
rem ==================================================================

set "STEAMCMD=C:\steamcmd\steamcmd.exe"
set "PUBLISHED_FILE_ID=3707968015"
set "APP_ID=294100"
set "CHANGENOTE=Fix mid-save ascension loop: failsafe now spawns Aethira directly with a Divine Arrival letter when she has never existed in the save, instead of a 7-day ascension recovery for a pawn that never was. Docs updated."

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%.."
set "MOD_ROOT=%CD%"
popd

set "STAGING=%TEMP%\KurinDemigodess_Workshop"
set "VDF=%SCRIPT_DIR%_workshop_generated.vdf"

echo ==================================================
echo  Mod root : %MOD_ROOT%
echo  Staging  : %STAGING%
echo  VDF      : %VDF%
echo  Item ID  : %PUBLISHED_FILE_ID%
echo ==================================================
echo.

if not exist "%STEAMCMD%" (
    echo ERROR: steamcmd not found at "%STEAMCMD%"
    echo Download it from https://developer.valvesoftware.com/wiki/SteamCMD
    echo Then either install to that path, or edit STEAMCMD in this script.
    exit /b 1
)

rem ==== CLEAN STAGING ====
if exist "%STAGING%" (
    echo Cleaning existing staging dir...
    rmdir /s /q "%STAGING%"
)
mkdir "%STAGING%"

rem ==== COPY WHITELISTED MOD CONTENT ====
echo Copying mod content to staging...
call :copydir 1.6
call :copydir About
call :copydir Biotech
call :copydir Odyssey
call :copydir Hair
call :copydir Languages
call :copydir Sounds
call :copydir Textures
call :copyfile LoadFolders.xml
call :copyfile Changelog.txt

rem ==== GENERATE VDF ====
echo Generating workshop.vdf...
(
    echo "workshopitem"
    echo {
    echo 	"appid" "%APP_ID%"
    echo 	"publishedfileid" "%PUBLISHED_FILE_ID%"
    echo 	"contentfolder" "%STAGING%"
    echo 	"changenote" "%CHANGENOTE%"
    echo }
) > "%VDF%"

echo.
echo --- VDF contents ---
type "%VDF%"
echo --------------------
echo.

rem ==== INVOKE STEAMCMD ====
set /p STEAM_USER=Steam username:
echo.
echo Running steamcmd upload. You may be prompted for password / 2FA code.
echo.

"%STEAMCMD%" +login %STEAM_USER% +workshop_build_item "%VDF%" +quit

if errorlevel 1 (
    echo.
    echo Upload FAILED. Check the output above for the specific error.
    exit /b 1
)

echo.
echo Upload complete. Verify at:
echo   https://steamcommunity.com/sharedfiles/filedetails/?id=%PUBLISHED_FILE_ID%
exit /b 0

:copydir
if exist "%MOD_ROOT%\%~1" (
    xcopy /E /I /Y /Q "%MOD_ROOT%\%~1" "%STAGING%\%~1" > nul
    echo   + %~1\
) else (
    echo   - %~1\ ^(not present, skipped^)
)
goto :eof

:copyfile
if exist "%MOD_ROOT%\%~1" (
    copy /Y "%MOD_ROOT%\%~1" "%STAGING%\%~1" > nul
    echo   + %~1
) else (
    echo   - %~1 ^(not present, skipped^)
)
goto :eof
