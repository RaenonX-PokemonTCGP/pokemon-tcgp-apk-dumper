param(
    [Parameter(mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string]
    $CsFileDir,

    [ValidateNotNullOrEmpty()]
    [switch]
    $PauseOnEnd = $false
)

$ReplaceTargetRegexList = @(
    #  //Field offset: 0x0
    # > Note the space before //
    '//Field offset: .*'
)

function Format-File {
    param (
        [Parameter(mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [string]
        $FilePath
    )

    Write-Host -ForegroundColor Cyan "Cleaning up $FilePath"

    foreach ($private:ReplaceTargetRegex in $ReplaceTargetRegexList) {
        (Get-Content -Path $FilePath -Raw) -Replace $private:ReplaceTargetRegex, "" | Set-Content $FilePath
    }

    # Remove all ending newlines
    (Get-Content -Path $FilePath -Raw).TrimEnd() | Set-Content $FilePath
}

Get-ChildItem -Path "$CsFileDir\*" -Include "*.cs" -Force -Recurse | ForEach-Object {
    Format-File -FilePath $_.FullName
}

if ($PauseOnEnd) {
    Write-Host "C# file dump cleanup is done."
    Read-Host
}