param([string]$Version='1.2.0')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Version must be a semantic version such as 1.1.0.'}
& (Join-Path $PSScriptRoot 'Build.ps1')
$dist=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force|Out-Null
$releaseName='PBZ-L3Sprint-OneClick-v'+$Version
$releaseDir=Join-Path $dist $releaseName
if(Test-Path -LiteralPath $releaseDir){throw 'Release directory already exists. Choose a fresh version or move the previous directory aside.'}
New-Item -ItemType Directory -Path $releaseDir|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\PBZ-L3Sprint-Setup.exe') -Destination (Join-Path $releaseDir 'PBZ-L3Sprint-Setup.exe')
foreach($name in @('README.md','LICENSE.txt')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $releaseDir $name)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $releaseDir 'docs') -Recurse
$sourcePaths=@('source','payload','docs','tests','Build.ps1','Package.ps1','README.md','LICENSE.txt','.gitignore','.gitattributes')|ForEach-Object{Join-Path $PSScriptRoot $_}
Compress-Archive -LiteralPath $sourcePaths -DestinationPath (Join-Path $releaseDir 'Installer-Source.zip')
$hashes=Get-ChildItem -LiteralPath $releaseDir -Recurse -File|Sort-Object FullName|ForEach-Object{
    $relative=$_.FullName.Substring($releaseDir.Length+1).Replace('\','/')
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$relative
}
[IO.File]::WriteAllText((Join-Path $releaseDir 'SHA256SUMS.txt'),($hashes -join "`n")+"`n",[Text.UTF8Encoding]::new($false))
$zip=Join-Path $dist ($releaseName+'.zip')
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $releaseDir|ForEach-Object{$_.FullName}) -DestinationPath $zip
[IO.File]::WriteAllText(($zip+'.sha256'),((Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($zip)+"`n"),[Text.UTF8Encoding]::new($false))
Write-Output $zip
