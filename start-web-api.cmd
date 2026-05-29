@echo off
setlocal

REM Starts the FastMediatR sample Web API from the repository root.
pushd "%~dp0"

echo Starting FastMediatR sample API...
echo URL: http://localhost:5000/health
echo Press Ctrl+C to stop.
echo.

dotnet run --project "samples\FastMediatR.Sample.Api\FastMediatR.Sample.Api.csproj"
set "EXIT_CODE=%ERRORLEVEL%"

popd
endlocal & exit /b %EXIT_CODE%
