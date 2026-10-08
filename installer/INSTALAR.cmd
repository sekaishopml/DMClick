@echo off
rem Doble click para instalar DM-CLICK (pedira permiso de administrador).
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
