$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x compiler not found.'}
$buildRoot=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildRoot -Force|Out-Null
$compilerArgs=@('/nologo','/target:winexe','/optimize+','/codepage:65001',('/out:'+(Join-Path $buildRoot 'PBZ-L3Sprint-Setup.exe')))
foreach($name in @('System.Windows.Forms.dll','System.Drawing.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll')){$compilerArgs+=('/reference:'+(Join-Path $framework $name))}
foreach($name in @('main.lua','settings.lua')){$compilerArgs+=('/resource:'+(Join-Path $PSScriptRoot ('payload\'+$name))+',payload.'+$name)}
foreach($name in @('Installer.cs','VendorInfo.cs','SelfTests.cs')){$compilerArgs+=(Join-Path $PSScriptRoot ('source\'+$name))}
& $compiler @compilerArgs
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
Write-Output (Join-Path $buildRoot 'PBZ-L3Sprint-Setup.exe')
