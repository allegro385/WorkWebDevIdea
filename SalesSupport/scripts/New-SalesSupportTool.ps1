<#
.SYNOPSIS
    ToolIdから新しいWebツールのソース一式をテンプレートツールから生成します。

.DESCRIPTION
    テンプレートツールを複製し、命名規則で導出した名称へ置き換えます。テンプレート自身は変更しません。
    生成後はプレースホルダーと旧テンプレート名の残存、ソリューションとProjectReferenceの参照先を検査し、
    restore・buildで確かめます。テストプロジェクトは生成しません（テストはテンプレートとサンプルの
    ソリューションで保持します）。DB、IIS、証明書、環境変数、本番配置先は変更しません。

.PARAMETER ToolId
    Portalのツールマスタへ登録するツールIDです。英数字・下線・ハイフンの1～20文字です。

.PARAMETER ToolName
    ローカル起動時のアプリ名です。省略するとToolIdを使用します。

.PARAMETER SalesSupportRoot
    SalesSupportフォルダーのパスです。省略するとこのスクリプトの親フォルダーを使用します。

.PARAMETER HttpsPort
    生成するツールのローカルHTTPSポートです。省略するとテンプレートと同じ7075のままです。

.PARAMETER HttpPort
    生成するツールのローカルHTTPポートです。省略するとテンプレートと同じ5275のままです。

.PARAMETER SkipBuild
    生成後のrestore・buildを実行しません。検査だけを行います。

.EXAMPLE
    .\New-SalesSupportTool.ps1 -ToolId T001 -ToolName "見積計算ツール" -HttpsPort 7101 -HttpPort 5101
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ToolId,
    [string]$ToolName,
    [string]$SalesSupportRoot,
    [int]$HttpsPort = 0,
    [int]$HttpPort = 0,
    [switch]$SkipBuild
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# テンプレート側の名称です。ここに挙げた文字列だけを置換対象とし、任意の文字列を全面置換しません。
$templateProjectName = 'SalesSupport.Template.Web'
# テストプロジェクトは生成しません。生成物へ参照が漏れていないかを検査するためだけに名称を保持します。
$templateTestsName = 'SalesSupport.Template.Tests'
$templateSolutionName = 'template.slnx'
$templateSourceFolder = 'Template'
$toolIdPlaceholder = '__TOOL_ID__'
$toolNamePlaceholder = '__TOOL_NAME__'
$templateHttpsPort = '7075'
$templateHttpPort = '5275'

# 生成対象として扱うテキストファイルです。ほかの拡張子はそのままコピーします。
$textExtensions = @('.cs', '.cshtml', '.csproj', '.slnx', '.json', '.css', '.js', '.md', '.txt', '.config')
# 生成物・エディター作業用フォルダーは複製しません。
$excludedDirectories = @('bin', 'obj', '.vs')
# テンプレートの説明文書は複製せず、生成したツール用のREADMEを作成します。
$excludedProjectFiles = @('README.md')
# 予約済みの名称です。既存プロジェクトと重複する名前空間・フォルダーを作りません。
$reservedNames = @('Common', 'Portal', 'Template', 'System', 'Sales', 'SalesSupport')
# Windowsが予約しているデバイス名です。フォルダー名に使用できません。
$deviceNames = @('CON', 'PRN', 'AUX', 'NUL', 'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9',
    'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9')

<#
.SYNOPSIS
    処理の区切りを表示します。
#>
function Write-Step {
    param([string]$Message)
    Write-Host "[生成] $Message"
}

<#
.SYNOPSIS
    中断理由を表示して終了コード1で終わります。
#>
function Stop-WithError {
    param([string]$Message)
    Write-Host ""
    Write-Host "中断しました：$Message" -ForegroundColor Red
    exit 1
}

<#
.SYNOPSIS
    ToolIdを検証し、C#識別子とフォルダー名に使う名称へ変換します。
.DESCRIPTION
    ハイフンと下線で区切った各語の先頭だけを大文字にして連結します。例：TOOL_001はTool001、DMY-WEBはDmyWebです。
#>
function ConvertTo-ToolIdentifier {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { Stop-WithError "ToolIdを入力してください。" }
    $trimmed = $Value.Trim()
    if ($trimmed.Length -gt 20) { Stop-WithError "ToolIdは20文字以内です。入力は$($trimmed.Length)文字です。" }
    if ($trimmed -notmatch '^[A-Za-z0-9_-]+$') {
        Stop-WithError "ToolIdに使用できるのは英数字・下線・ハイフンだけです。ほかの文字はC#の識別子へ変換できません。"
    }
    if ($trimmed.ToUpperInvariant() -eq 'SYSTEM') { Stop-WithError "ToolId 'SYSTEM' は予約値のため使用できません。" }

    $identifier = ''
    # 区切り文字の配列は型を明示します。PowerShellの版によって1つの文字列として解釈されると分割できません。
    foreach ($segment in $trimmed.Split([char[]]@('-', '_'), [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $identifier += $segment.Substring(0, 1).ToUpperInvariant() + $segment.Substring(1).ToLowerInvariant()
    }
    if ($identifier -notmatch '^[A-Za-z][A-Za-z0-9]*$') {
        Stop-WithError "ToolId '$trimmed' からC#識別子を作れません。先頭が英字になるToolIdを指定してください。"
    }
    foreach ($reserved in $reservedNames) {
        if ($identifier.ToUpperInvariant() -eq $reserved.ToUpperInvariant()) {
            Stop-WithError "ToolId '$trimmed' から導出した名称 '$identifier' は既存プロジェクトと重複するため使用できません。"
        }
    }
    foreach ($device in $deviceNames) {
        if ($identifier.ToUpperInvariant() -eq $device) {
            Stop-WithError "導出した名称 '$identifier' はWindowsの予約名のためフォルダー名に使用できません。"
        }
    }
    return $identifier
}

<#
.SYNOPSIS
    既存の同名フォルダー・ソリューションを大文字小文字を無視して検出します。
#>
function Assert-NoNameConflict {
    param([string]$Directory, [string]$Name, [string]$Kind)

    if (-not (Test-Path -LiteralPath $Directory)) { return }
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Name.ToUpperInvariant() -eq $Name.ToUpperInvariant()) {
            Stop-WithError "$Kind '$($entry.FullName)' が既にあります。別のToolIdを指定するか、既存の生成物を整理してください。"
        }
    }
}

<#
.SYNOPSIS
    テキストの置換一覧を順に適用します。
#>
function Convert-Text {
    param([string]$Text, [object[]]$Replacements)

    $converted = $Text
    foreach ($replacement in $Replacements) {
        $converted = $converted.Replace($replacement.From, $replacement.To)
    }
    return $converted
}

<#
.SYNOPSIS
    1ファイルを複製します。テキストは置換し、元のBOMの有無を維持します。
#>
function Copy-TemplateFile {
    param([string]$SourcePath, [string]$DestinationPath, [object[]]$Replacements)

    $directory = Split-Path -Parent $DestinationPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

    $extension = [System.IO.Path]::GetExtension($SourcePath).ToLowerInvariant()
    if ($textExtensions -notcontains $extension) {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
        return
    }

    $bytes = [System.IO.File]::ReadAllBytes($SourcePath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $text = $text.Substring(1) }
    $encoding = New-Object System.Text.UTF8Encoding($hasBom)
    [System.IO.File]::WriteAllText($DestinationPath, (Convert-Text -Text $text -Replacements $Replacements), $encoding)
}

<#
.SYNOPSIS
    複製したソリューションからテストプロジェクトの区画を取り除きます。
#>
function Remove-TestFolderFromSolution {
    param([string]$SolutionPath)

    $bytes = [System.IO.File]::ReadAllBytes($SolutionPath)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    if ($hasBom) { $text = $text.Substring(1) }
    # テストの<Folder>要素だけを行ごと取り除きます。ほかの区画は変更しません。
    $pattern = '[ \t]*<Folder Name="/tests/">.*?</Folder>\r?\n'
    $updated = [System.Text.RegularExpressions.Regex]::Replace($text, $pattern, '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($updated -eq $text) { Stop-WithError "ソリューションからテストの区画を取り除けませんでした：$SolutionPath" }
    [System.IO.File]::WriteAllText($SolutionPath, $updated, (New-Object System.Text.UTF8Encoding($hasBom)))
}

<#
.SYNOPSIS
    フォルダー配下を複製します。フォルダー名・ファイル名にも同じ置換を適用します。
#>
function Copy-TemplateTree {
    param([string]$SourceRoot, [string]$DestinationRoot, [object[]]$Replacements, [string[]]$ExcludedFiles)

    $copied = 0
    foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Force) {
        $relative = $file.FullName.Substring($SourceRoot.Length).TrimStart([char]'\', [char]'/')
        $segments = $relative -split '[\\/]'
        $skip = $false
        foreach ($segment in $segments) {
            if ($excludedDirectories -contains $segment) { $skip = $true }
        }
        if ($ExcludedFiles -contains $segments[$segments.Length - 1]) { $skip = $true }
        if ($skip) { continue }

        $destinationRelative = Convert-Text -Text ($relative -replace '\\', '/') -Replacements $Replacements
        Copy-TemplateFile -SourcePath $file.FullName -DestinationPath (Join-Path $DestinationRoot $destinationRelative) -Replacements $Replacements
        $copied++
    }
    return $copied
}

<#
.SYNOPSIS
    生成物にプレースホルダーとテンプレート名が残っていないかを検査します。
#>
function Test-NoLeftovers {
    param([string[]]$Roots, [string[]]$Tokens)

    $findings = @()
    foreach ($root in $Roots) {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
            if ($textExtensions -notcontains [System.IO.Path]::GetExtension($file.FullName).ToLowerInvariant()) { continue }
            $text = [System.IO.File]::ReadAllText($file.FullName)
            foreach ($token in $Tokens) {
                if ($text.Contains($token)) { $findings += "$($file.FullName)：$token" }
            }
            foreach ($token in $Tokens) {
                if ($file.Name.Contains($token)) { $findings += "$($file.FullName)：ファイル名に $token" }
            }
        }
    }
    return ,$findings
}

<#
.SYNOPSIS
    ソリューションとProjectReferenceの参照先が実在するかを検査します。
#>
function Test-ProjectPaths {
    param([string]$SolutionPath, [string]$SolutionRoot)

    $findings = @()
    $solutionText = [System.IO.File]::ReadAllText($SolutionPath)
    foreach ($match in [regex]::Matches($solutionText, 'Project\s+Path="([^"]+)"')) {
        $projectPath = [System.IO.Path]::GetFullPath((Join-Path $SolutionRoot ($match.Groups[1].Value -replace '\\', '/')))
        if (-not (Test-Path -LiteralPath $projectPath)) { $findings += "$SolutionPath：$($match.Groups[1].Value) が見つかりません。" }
        else {
            $projectText = [System.IO.File]::ReadAllText($projectPath)
            foreach ($reference in [regex]::Matches($projectText, 'ProjectReference\s+Include="([^"]+)"')) {
                $referencePath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectPath) ($reference.Groups[1].Value -replace '\\', '/')))
                if (-not (Test-Path -LiteralPath $referencePath)) { $findings += "$projectPath：$($reference.Groups[1].Value) が見つかりません。" }
            }
        }
    }
    return ,$findings
}

<#
.SYNOPSIS
    生成したJSONの構文と、単独開発・ポータル連携の各起動プロファイルのToolId・表示名・起動モードを検査します。
#>
function Test-GeneratedJson {
    param([string[]]$Roots, [string]$LaunchSettingsPath, [string]$ExpectedToolId, [string]$ExpectedName)

    $findings = @()
    foreach ($root in $Roots) {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.json') {
            try { [void](ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($file.FullName)) -ErrorAction Stop) }
            catch { $findings += "$($file.FullName)：JSONの構文が不正です。" }
        }
    }
    try {
        $settings = ConvertFrom-Json -InputObject ([System.IO.File]::ReadAllText($LaunchSettingsPath)) -ErrorAction Stop
        # 生成直後のF5を単独開発にするため、先頭の起動プロファイルは standalone とします。
        if (@($settings.profiles.PSObject.Properties.Name)[0] -cne 'standalone') {
            $findings += "$LaunchSettingsPath：先頭の起動プロファイルが standalone ではありません。"
        }
        foreach ($expected in @(@{ Name = 'standalone'; Mode = 'Standalone' }, @{ Name = 'portal'; Mode = 'Portal' })) {
            # StrictModeでは存在しないプロパティの参照が例外になるため、PSObject経由で有無を確認します。
            $profileEntry = $settings.profiles.PSObject.Properties[$expected.Name]
            if ($null -eq $profileEntry) { $findings += "$LaunchSettingsPath：起動プロファイル $($expected.Name) がありません。"; continue }
            $variables = $profileEntry.Value.environmentVariables.PSObject.Properties
            $valueOf = { param($name) $entry = $variables[$name]; if ($null -eq $entry) { $null } else { $entry.Value } }
            if ((& $valueOf 'SalesSupport__Application__ToolId') -cne $ExpectedToolId -or
                (& $valueOf 'SalesSupport__Application__Name') -cne $ExpectedName) {
                $findings += "$LaunchSettingsPath：$($expected.Name) のToolIdまたはアプリ名が入力値と一致しません。"
            }
            if ((& $valueOf 'SalesSupport__Tool__ExecutionMode') -cne $expected.Mode) {
                $findings += "$LaunchSettingsPath：$($expected.Name) の起動モードが不正です。"
            }
        }
    }
    catch {
        $message = "$LaunchSettingsPath：JSONの構文が不正です。"
        if ($findings -notcontains $message) { $findings += $message }
    }
    return ,$findings
}

# ----- 1. 入力の検証と名称の導出 -----
$identifier = ConvertTo-ToolIdentifier -Value $ToolId
$toolIdValue = $ToolId.Trim()
$displayName = $ToolName
if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = $toolIdValue }
# プレースホルダーはJSONの文字列値の内側にあるため、表示名をJSON文字列としてエスケープしてから差し込みます。
$jsonDisplayName = ConvertTo-Json -InputObject $displayName -Compress
$escapedDisplayName = $jsonDisplayName.Substring(1, $jsonDisplayName.Length - 2)

if ([string]::IsNullOrWhiteSpace($SalesSupportRoot)) { $SalesSupportRoot = Split-Path -Parent $PSScriptRoot }
$SalesSupportRoot = [System.IO.Path]::GetFullPath($SalesSupportRoot)

$templateProjectPath = [System.IO.Path]::GetFullPath((Join-Path $SalesSupportRoot "src/$templateSourceFolder/$templateProjectName"))
$templateSolutionPath = [System.IO.Path]::GetFullPath((Join-Path $SalesSupportRoot $templateSolutionName))
foreach ($required in @($templateProjectPath, $templateSolutionPath)) {
    if (-not (Test-Path -LiteralPath $required)) { Stop-WithError "テンプレートが見つかりません：$required" }
}

$projectName = "SalesSupport.$identifier.Web"
$solutionName = "$identifier.slnx"
$destinationProjectPath = [System.IO.Path]::GetFullPath((Join-Path $SalesSupportRoot "src/$identifier/$projectName"))
$destinationSolutionPath = [System.IO.Path]::GetFullPath((Join-Path $SalesSupportRoot $solutionName))

Write-Step "ToolId '$toolIdValue' から名称 '$identifier' を導出しました。"

# ----- 2. 生成前の衝突検査 -----
Assert-NoNameConflict -Directory (Join-Path $SalesSupportRoot 'src') -Name $identifier -Kind 'ソースフォルダー'
Assert-NoNameConflict -Directory $SalesSupportRoot -Name $solutionName -Kind 'ソリューション'

# ----- 3. 複製と置換 -----
$replacements = @(
    @{ From = $templateProjectName; To = $projectName },
    @{ From = "src/$templateSourceFolder/"; To = "src/$identifier/" },
    @{ From = $templateSolutionName; To = $solutionName },
    @{ From = $toolIdPlaceholder; To = $toolIdValue },
    @{ From = $toolNamePlaceholder; To = $escapedDisplayName }
)
if ($HttpsPort -gt 0) { $replacements += @{ From = $templateHttpsPort; To = $HttpsPort.ToString() } }
if ($HttpPort -gt 0) { $replacements += @{ From = $templateHttpPort; To = $HttpPort.ToString() } }

$projectFiles = Copy-TemplateTree -SourceRoot $templateProjectPath -DestinationRoot $destinationProjectPath -Replacements $replacements -ExcludedFiles $excludedProjectFiles
Copy-TemplateFile -SourcePath $templateSolutionPath -DestinationPath $destinationSolutionPath -Replacements $replacements
# 生成したツールはテストプロジェクトを持たないため、複製したソリューションからテストの区画を取り除きます。
Remove-TestFolderFromSolution -SolutionPath $destinationSolutionPath
Write-Step "ソース $projectFiles 件、ソリューション1件を生成しました。"

# ----- 4. 生成したツールのREADME -----
$readme = @"
# $projectName

ToolId ``$toolIdValue`` のWebツールです。テンプレートツールから生成しました。
共通契約は[Common詳細設計](../../../../設計書/10_Common詳細設計.md)、テンプレートの範囲は[サンプル・テンプレートツール設計方針](../../../../設計書/12_サンプル・テンプレートツール設計方針.md)を正とします。

生成直後は入力・計算・出力がテンプレートの例のままです。``Models``・``Services``・``Views``をこのツールの題材へ差し替えてください。
共通の認証・利用制御、``WEB_OPEN``と``WEB_EXECUTE``のログ記録、CSRF、アップロード条件の取得と一時ファイルの削除は変更せずに使用します。

## 構成

| 項目 | 値 |
| --- | --- |
| ToolId | ``$toolIdValue`` |
| ソリューション | ``SalesSupport/$solutionName`` |
| プロジェクト | ``SalesSupport/src/$identifier/$projectName`` |
| Portalからの起動URL例 | ``/tools/$toolIdValue/app`` |

## 実行に必要な設定

起動プロファイルは``standalone``（既定、Visual StudioのF5）と``portal``の2つを``Properties/launchSettings.json``へ生成済みです。

| 起動プロファイル | 用途 | 必要なもの |
| --- | --- | --- |
| ``standalone`` | 画面・計算・出力の開発（単独開発） | なし（Portal・DB・SMTP・共通設定ファイル・鍵／保存フォルダーは不要） |
| ``portal`` | Portalでログインした実ユーザーでの結合試験 | Portalの起動、``SalesSupport/config/salessupport.common.Development.json``、開発用DBとツール行等の登録 |

単独開発の範囲は[共通設定の単独開発手順](../../../config/README.md#ツールを単独で開発する)を参照してください。接続文字列・鍵・保存領域を含む連携モードの共通設定は[共通設定ファイル](../../../config/README.md)で管理します。ツール固有の値は各アプリの環境変数で与えます。

| 環境変数 | 内容 |
| --- | --- |
| ``SalesSupport__Application__ToolId`` | ``$toolIdValue`` |
| ``SalesSupport__Application__Name`` | 障害ログへ記録するアプリ名 |
| ``SalesSupport__Tool__ExecutionMode`` | ``standalone``は``Standalone``、``portal``は``Portal``を明示。配置環境では設定しません |
| ``SalesSupport__CommonConfigPath`` | 連携モードの共通設定ファイルへの、コンテンツルートからの相対パス。IISの標準配置では不要 |

配置環境ではIISの``web.config``でToolIdとアプリ名を設定します。

## 生成後に別途必要な作業

スクリプトはソース一式だけを作ります。次の作業は[導入・運用](../../../../設計書/05_導入・運用.md)に従って別途行います。

1. Portalのツールマスタへ ToolId ``$toolIdValue`` のWEBツール行と起動URLを所定のSQLで登録する。
2. アップロードを使う場合は``TOOL``＋``TOOL_INPUT``のアップロード条件を登録する。
3. IISへアプリケーションとアプリケーションプールを登録し、環境変数を設定する。
4. 非公開のまま動作を確認してから、限定公開または一般公開へ変更する。

## 検証

``````text
dotnet build SalesSupport/$solutionName
dotnet run --project SalesSupport/src/$identifier/$projectName --launch-profile standalone
``````

このツールにテストプロジェクトはありません。要求パイプライン（連携モードで未認証の画面要求がPortalのログインへ誘導されること、単独開発でCSRFトークンのないPOSTが400になること）はCommonが持つ共通の動作であり、テンプレートのソリューションのテストで確認しています。このツール固有の計算・画面を変更したときは、テンプレートとサンプルのテストを参考に検証方法を用意してください。共有Cookieでの入場、サイト・ツール状態による利用制御、アップロード条件の取得、ログ記録は``portal``プロファイルで実DBとPortalを併用した環境で確認してください。
"@
[System.IO.File]::WriteAllText((Join-Path $destinationProjectPath 'README.md'), $readme, (New-Object System.Text.UTF8Encoding($false)))

# ----- 5. 生成後の検査 -----
$tokens = @($templateProjectName, $templateTestsName, "src/$templateSourceFolder/", $templateSolutionName, $toolIdPlaceholder, $toolNamePlaceholder)
$leftovers = Test-NoLeftovers -Roots @($destinationProjectPath) -Tokens $tokens
$solutionText = [System.IO.File]::ReadAllText($destinationSolutionPath)
foreach ($token in $tokens) {
    if ($solutionText.Contains($token)) { $leftovers += "$destinationSolutionPath：$token" }
}
$pathFindings = Test-ProjectPaths -SolutionPath $destinationSolutionPath -SolutionRoot $SalesSupportRoot
$launchSettingsPath = Join-Path $destinationProjectPath 'Properties/launchSettings.json'
$jsonFindings = Test-GeneratedJson -Roots @($destinationProjectPath) -LaunchSettingsPath $launchSettingsPath -ExpectedToolId $toolIdValue -ExpectedName $displayName
$findings = @($leftovers) + @($pathFindings) + @($jsonFindings)
if ($findings.Count -gt 0) {
    Write-Host ""
    Write-Host "生成物の検査で問題が見つかりました。完成品として扱わないでください。" -ForegroundColor Red
    foreach ($finding in $findings) { Write-Host " - $finding" }
    Write-Host "生成先：$destinationProjectPath、$destinationSolutionPath"
    exit 1
}
Write-Step "プレースホルダーの残存、参照先、JSONの検査に成功しました。"

# ----- 6. restore・build -----
if ($SkipBuild) {
    Write-Step "restore・buildは -SkipBuild の指定により実行していません。"
}
elseif (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host ".NET SDKが見つからないため、restore・buildを実行していません。" -ForegroundColor Yellow
}
else {
    foreach ($step in @(
            @{ Label = 'restore'; Arguments = @('restore', $destinationSolutionPath) },
            @{ Label = 'build'; Arguments = @('build', $destinationSolutionPath, '--no-restore') })) {
        Write-Step "dotnet $($step.Label) を実行します。"
        & dotnet $step.Arguments
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "dotnet $($step.Label) が失敗しました。生成物を完成品として扱わないでください。" -ForegroundColor Red
            Write-Host "生成先：$destinationProjectPath、$destinationSolutionPath"
            exit 1
        }
    }
}

# ----- 7. 結果と次の作業 -----
Write-Host ""
Write-Host "ToolId '$toolIdValue' のツールを生成しました。"
Write-Host "  ソリューション：$destinationSolutionPath"
Write-Host "  プロジェクト  ：$destinationProjectPath"
Write-Host ""
Write-Host "このスクリプトはソースだけを作成します。DB登録、アップロード条件、IISの設定、環境変数、本番配置は行っていません。"
Write-Host "生成したプロジェクトのREADMEに沿って、ツールマスタの登録とIISの準備を別途進めてください。"
exit 0
