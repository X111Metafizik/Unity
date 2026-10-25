@echo off
setlocal
cd /d "%~dp0"
if exist "Builds\PlanetAOT\Ladir.exe" (
  start "LADIR" "Builds\PlanetAOT\Ladir.exe"
) else if exist "Builds\Planet\Ladir.exe" (
  start "LADIR" "Builds\Planet\Ladir.exe"
) else (
  echo Unity projesini acin. LADIR menusunden 5 - Build Planet Studio for Windows secenegini calistirin.
  pause
)
