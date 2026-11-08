@echo off
rem Opens the Agent Task Board in your browser (tools/taskboard). Ctrl+C stops it.
rem Extra arguments go to `taskboard serve`, e.g. task-board.bat --port 8800
setlocal
set "TB=%~dp0tools\taskboard\taskboard.py"
set "PY=python"
where py >nul 2>nul && set "PY=py -3"
%PY% "%TB%" serve --open %*
if errorlevel 1 pause
