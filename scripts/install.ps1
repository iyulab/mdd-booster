# mdd-booster 로컬 설치 스크립트
#
# 동작:
#   1. MddBooster.Cli 를 dotnet publish (Debug, net10.0) → bin/publish/
#   2. bin/publish/ 내용을 전역 설치 경로(기본 D:\lib\mdd-booster\)로 복사
#   3. mdd --version 으로 설치 확인
#
# 사용:
#   PS> ./scripts/install.ps1                                    # 기본 경로
#   PS> ./scripts/install.ps1 -InstallPath C:\tools\mdd-booster  # 커스텀 경로
#   PS> ./scripts/install.ps1 -Configuration Release             # Release 빌드

[CmdletBinding()]
param(
    [string]$InstallPath = "D:\lib\mdd-booster",
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

# 저장소 루트 경로 — 이 스크립트는 scripts/ 에 위치
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$PublishDir = Join-Path $RepoRoot 'bin/publish'
$CliProject = Join-Path $RepoRoot 'src/MddBooster.Cli/MddBooster.Cli.csproj'

if (-not (Test-Path $CliProject)) {
    throw "MddBooster.Cli 프로젝트를 찾을 수 없음: $CliProject"
}

Write-Host "==> Publish ($Configuration) → $PublishDir" -ForegroundColor Cyan
dotnet publish $CliProject -c $Configuration -o $PublishDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 실패 (exit $LASTEXITCODE)"
}

if (-not (Test-Path $InstallPath)) {
    Write-Host "==> 설치 경로 생성: $InstallPath" -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}

Write-Host "==> Copy → $InstallPath" -ForegroundColor Cyan
# 전체 복사 (runtimes 하위 디렉터리 포함)
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $InstallPath -Recurse -Force

# 설치 검증 — PATH에 $InstallPath 가 없더라도 직접 호출 가능
$mddExe = Join-Path $InstallPath 'mdd.exe'
if (-not (Test-Path $mddExe)) {
    throw "설치 후에도 mdd.exe 없음: $mddExe"
}

Write-Host "==> 설치 완료" -ForegroundColor Green
Write-Host ""
& $mddExe version
Write-Host ""
Write-Host "PATH에 '$InstallPath' 를 추가하거나, 기존 wrapper ('D:\lib\mdd.cmd' 등)가 최신 경로를 가리키는지 확인하세요." -ForegroundColor Yellow
