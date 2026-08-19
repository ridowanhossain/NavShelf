@echo off
title Stopping NavShelf...
echo Stopping NavShelf...

taskkill /F /IM NavShelf.exe >nul 2>&1
taskkill /F /IM ExplorerNav.exe >nul 2>&1
taskkill /F /IM BookmarkBar.exe >nul 2>&1
taskkill /F /IM ExplorerBar.exe >nul 2>&1
taskkill /F /IM ExplorerBookmarkBar.exe >nul 2>&1

echo [OK] NavShelf has been stopped successfully.
ping 127.0.0.1 -n 2 >nul
exit
