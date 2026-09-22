@echo off
rem Creates a new SalesSupport web tool from the template. See README.md in this folder.
rem Usage: new-tool.bat [ToolId] [-ToolName "name"] [-HttpsPort 7101] [-HttpPort 5101] [-SkipBuild]
setlocal
set "SCRIPT_DIR=%~dp0"
if not "%~1"=="" goto run

set "TOOL_ID="
set /p TOOL_ID=ToolId (example: T001):
if "%TOOL_ID%"=="" goto empty
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%New-SalesSupportTool.ps1" -ToolId "%TOOL_ID%"
goto done

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%New-SalesSupportTool.ps1" %*
goto done

:empty
echo ToolId is required.
endlocal
exit /b 1

:done
endlocal & exit /b %ERRORLEVEL%
