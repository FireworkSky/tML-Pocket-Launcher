@echo off
setlocal
if not exist "%JAVA_HOME%\include\jni.h" (
  echo Set JAVA_HOME to a full 64-bit JDK 11 or newer.
  exit /b 1
)
if /i not "%VSCMD_ARG_TGT_ARCH%"=="x64" (
  for /f "usebackq delims=" %%I in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do call "%%I\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64
)
set "out=%~dp0..\..\build\tmod-textures"
if not exist "%out%\classes" mkdir "%out%\classes"
"%JAVA_HOME%\bin\javac" --release 11 -encoding UTF-8 -d "%out%\classes" "%~dp0TmodTextures.java" || exit /b 1
"%JAVA_HOME%\bin\jar" --create --file "%out%\tmod-textures.jar" --main-class app.com.tmlpocket.feature.mods.TmodTextures -C "%out%\classes" . || exit /b 1
pushd "%out%" || exit /b 1
cl /nologo /O2 /MT /LD /I"%JAVA_HOME%\include" /I"%JAVA_HOME%\include\win32" "%~dp0bc3.c" /link /out:tmod_bc3.dll
set "status=%errorlevel%"
popd
exit /b %status%
