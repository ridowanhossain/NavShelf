@echo off
powershell -Command "Unblock-File '%~dp0NavShelf.exe'" >nul 2>&1
start "" "%~dp0NavShelf.exe"
