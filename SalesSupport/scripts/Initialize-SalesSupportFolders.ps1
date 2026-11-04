<#
.SYNOPSIS
    配置先の標準フォルダーを作成し、アプリケーションプールへ権限を付与します。

.DESCRIPTION
    apps、config、keys、storage\temp、storage\filesを作成します。既にあるフォルダーはそのまま使います。
    -AppPoolNameを指定すると、各フォルダーへ必要な権限を追加します。既存の権限の削除、IISのサイト・
    アプリケーションプールの作成、設定ファイルの配置は行いません。-WhatIfで実行内容だけを確認できます。

.PARAMETER RootPath
    配置のルートフォルダーです。共通設定ファイルのconfigフォルダーの親を指定します。例：D:\SalesSupport

.PARAMETER AppPoolName
    権限を付与するアプリケーションプール名です。Portalと全Webツールのプールを指定します。

.EXAMPLE
    .\Initialize-SalesSupportFolders.ps1 -RootPath D:\SalesSupport -AppPoolName SalesSupportPortal, SalesSupportT001
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$RootPath,
    [string[]]$AppPoolName = @()
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# 標準配置のフォルダーと、アプリケーションプールへ与える権限です。RXは読取りと実行、Mは変更です。
$folders = @(
    @{ Path = 'apps'; Right = '(OI)(CI)RX' },
    @{ Path = 'config'; Right = '(OI)(CI)RX' },
    @{ Path = 'keys'; Right = '(OI)(CI)M' },
    @{ Path = 'storage\temp'; Right = '(OI)(CI)M' },
    @{ Path = 'storage\files'; Right = '(OI)(CI)M' }
)

$root = [System.IO.Path]::GetFullPath($RootPath)
foreach ($folder in $folders) {
    $path = Join-Path $root $folder.Path
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        if ($PSCmdlet.ShouldProcess($path, 'フォルダーを作成')) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
            Write-Host "[作成] $path"
        }
    }
    foreach ($pool in $AppPoolName) {
        $grant = "IIS AppPool\${pool}:$($folder.Right)"
        if ($PSCmdlet.ShouldProcess($path, "権限を追加（$grant）")) {
            & icacls.exe $path /grant $grant | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "権限を追加できませんでした：$path、$grant" }
            Write-Host "[権限] $path に $grant"
        }
    }
}

Write-Host "完了しました。configと鍵を運用管理者とアプリケーションプールだけが読めることを確認してください。"
