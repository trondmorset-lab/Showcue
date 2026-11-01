@echo off
REM Bygger ShowCue til en frittstående ShowCue.exe i mappa "publish".
REM Krever .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0

where dotnet >nul 2>nul
if errorlevel 1 (
  echo Fant ikke .NET SDK. Last ned .NET 10 SDK fra https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)

dotnet publish ShowCue\ShowCue.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo Bygging feilet. Se meldingene over.
  pause
  exit /b 1
)

echo.
echo Ferdig! Programmet ligger i publish\ShowCue.exe
pause
