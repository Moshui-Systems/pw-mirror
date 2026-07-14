@echo off
rem Compila a DLL e o injetor com g++ (MinGW-w64 x86_64).
rem Requer g++ no PATH. Instalado via: winget install BrechtSanders.WinLibs.POSIX.UCRT
rem (o binario fica em %LOCALAPPDATA%\Microsoft\WinGet\Packages\BrechtSanders.WinLibs...\mingw64\bin)

where g++ >nul 2>&1
if errorlevel 1 (
  echo g++ nao encontrado no PATH. Adicione a pasta mingw64\bin do WinLibs ao PATH.
  exit /b 1
)

echo Compilando MirrorHook.dll ...
g++ -shared -O2 -o MirrorHook.dll dllmain.cpp -static -static-libgcc -static-libstdc++ -lkernel32
if errorlevel 1 exit /b 1

echo Compilando injector.exe ...
g++ -O2 -municode -o injector.exe injector.cpp -static -static-libgcc -static-libstdc++
if errorlevel 1 exit /b 1

echo OK. Gerados: MirrorHook.dll e injector.exe
