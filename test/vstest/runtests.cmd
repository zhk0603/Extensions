set target=%1
powershell.exe -NoProfile -NoLogo -ExecutionPolicy unrestricted -Command "./dotnet-install.ps1 -Version 3.0.0-preview1-26907-05 -Channel 3.0 -Runtime dotnet -NoCdn -InstallDir %HELIX_CORRELATION_PAYLOAD%"
set PATH=%HELIX_CORRELATION_PAYLOAD%;%PATH%
dotnet vstest %target% --logger:trx
dir
dir %HELIX_CORRELATION_PAYLOAD%\shared\Microsoft.NETCore.App\3.0.0-preview-26907-05


