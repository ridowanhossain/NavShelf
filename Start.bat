@echo off
cd /d "%~dp0"
if exist "%~dp0bin\Release\NavShelf.exe" (
    copy /y "%~dp0bin\Release\NavShelf.exe" "%~dp0NavShelf.exe" >nul 2>&1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "Unblock-File '%~dp0NavShelf.exe' -ErrorAction SilentlyContinue; $cert = Get-Item Cert:\CurrentUser\My\C2DA8D926304688200DF658FC5E17B5F29C18277 -ErrorAction SilentlyContinue; if ($cert) { Set-AuthenticodeSignature '%~dp0NavShelf.exe' -Certificate $cert -HashAlgorithm SHA256 >$null 2>&1 }" >nul 2>&1
start "" "%~dp0NavShelf.exe"
