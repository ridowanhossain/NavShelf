@echo off
title Installing NavShelf Developer Certificate...
cd /d "%~dp0"
echo ========================================================
echo   Installing NavShelf Developer Certificate for Windows
echo ========================================================
echo.
echo If Windows shows a security confirmation prompt, please click "Yes".
echo.
certutil -user -addstore Root "%~dp0NavShelf.cer"
certutil -user -addstore TrustedPublisher "%~dp0NavShelf.cer"
certutil -user -addstore TrustedPeople "%~dp0NavShelf.cer"
echo.
echo Signing NavShelf.exe binary...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$cert = Get-Item Cert:\CurrentUser\My\C2DA8D926304688200DF658FC5E17B5F29C18277; Set-AuthenticodeSignature '%~dp0NavShelf.exe' -Certificate $cert -HashAlgorithm SHA256; Set-AuthenticodeSignature '%~dp0bin\Release\NavShelf.exe' -Certificate $cert -HashAlgorithm SHA256"
echo.
echo ========================================================
echo   [OK] Certificate installed and NavShelf.exe signed!
echo   You can now start NavShelf using Start.bat.
echo ========================================================
echo.
pause
