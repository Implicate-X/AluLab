param(
    [string]$localPath = "AluLab.Browser\bin\Release\net10.0-browser\publish\wwwroot",
    [string]$ftpRoot = "ftp://implicatex:yNQ5Or%de@myhobby.page/alu.homelabs.one",
    [string]$user = "implicatex",
    [string]$pass = "yNQ5Or%de"
)

$localPath = (Resolve-Path $localPath).Path

# Logfile
$logFile = Join-Path $PSScriptRoot "deploy.log"
"--- Deployment $(Get-Date) ---" | Out-File $logFile

function Log {
    param([string]$msg)
    $msg | Out-File $logFile -Append
}

# Credentials global setzen
$global:creds = New-Object System.Net.NetworkCredential($user, $pass)

# Lokale Dateien (relative Pfade)
$localFiles = Get-ChildItem -Recurse -File $localPath | ForEach-Object {
    $_.FullName.Substring($localPath.Length + 1).Replace("\", "/")
}

$total = $localFiles.Count
$global:index = 0

# --- FTP: rekursives Listing (robust, sicher) ---
function Get-FtpListingRecursive {
    param([string]$remoteDir, [string]$relativeBase = "")

    $items = @()

    try {
        $req = [System.Net.FtpWebRequest]::Create($remoteDir)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectoryDetails
        $req.Credentials = $global:creds
        $resp = $req.GetResponse()
        $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
        $lines = $sr.ReadToEnd().Split("`n") | Where-Object { $_.Trim() -ne "" }
        $sr.Close()
        $resp.Close()
    } catch {
        return @()
    }

    foreach ($line in $lines) {
        if ($line -match "^(?<perm>[\-ld])") {
            $isDir = $matches["perm"] -eq "d"
            $parts = $line.Split(" ", 9)
            $name = $parts[-1].Trim()

            if ($name -in @(".", "..")) { continue }

            $remotePath = "$remoteDir/$name"
            $relativePath = if ($relativeBase -eq "") { $name } else { "$relativeBase/$name" }

            if ($isDir) {
                $items += Get-FtpListingRecursive -remoteDir $remotePath -relativeBase $relativePath
            } else {
                $items += $relativePath
            }
        }
    }

    return $items
}

# Server-Dateien (relative Pfade)
$serverFiles = Get-FtpListingRecursive -remoteDir $ftpRoot

# --- Datei-Infos vom Server holen ---
function Get-FtpFileInfo {
    param([string]$remoteFile)

    try {
        # Größe
        $req = [System.Net.FtpWebRequest]::Create($remoteFile)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::GetFileSize
        $req.Credentials = $global:creds
        $size = ($req.GetResponse()).ContentLength

        # Datum
        $req = [System.Net.FtpWebRequest]::Create($remoteFile)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::GetDateTimestamp
        $req.Credentials = $global:creds
        $date = ($req.GetResponse()).LastModified

        return @{ Exists = $true; Size = $size; Date = $date }
    }
    catch {
        return @{ Exists = $false }
    }
}

# --- Datei hochladen ---
function Upload-FtpFile {
    param(
        [string]$localFile,
        [string]$remoteFile
    )

    $fileInfo = Get-Item $localFile
    $fileSize = $fileInfo.Length

    $req = [System.Net.FtpWebRequest]::Create($remoteFile)
    $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $req.Credentials = $global:creds
    $req.UseBinary = $true
    $req.ContentLength = $fileSize

    $buffer = New-Object byte[] (512KB)
    $read = 0

    $fs = [System.IO.File]::OpenRead($localFile)
    $rs = $req.GetRequestStream()

    while (($read = $fs.Read($buffer, 0, $buffer.Length)) -gt 0) {
        $rs.Write($buffer, 0, $read)
    }

    $rs.Close()
    $fs.Close()

    $resp = $req.GetResponse()
    $resp.Close()

    Log "Uploaded: $remoteFile"
}

# --- Ordner anlegen ---
function Ensure-FtpDirectory {
    param([string]$remoteDir)

    try {
        $req = [System.Net.FtpWebRequest]::Create($remoteDir)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
        $req.Credentials = $global:creds
        $req.GetResponse() | Out-Null
        Log "Created directory: $remoteDir"
    } catch {}
}

# --- Upload + Sync ---
foreach ($rel in $localFiles) {
    $global:index++

    $percent = [math]::Round(($global:index / $total) * 100, 1)
    Write-Progress -Id 1 -Activity "Gesamtfortschritt" -Status "Datei $global:index von $total" -PercentComplete $percent

    $localFile = Join-Path $localPath $rel
    $remoteFile = "$ftpRoot/$rel"

    # Ordnerstruktur erzeugen
    $remoteDir = Split-Path $remoteFile -Parent
    Ensure-FtpDirectory $remoteDir

    # Server-Datei prüfen
    $info = Get-FtpFileInfo $remoteFile

    $uploadNeeded = $false

    if (-not $info.Exists) {
        $uploadNeeded = $true
    }
    elseif ($info.Size -ne (Get-Item $localFile).Length) {
        $uploadNeeded = $true
    }
    elseif ($info.Date -lt (Get-Item $localFile).LastWriteTime) {
        $uploadNeeded = $true
    }

    if ($uploadNeeded) {
        Upload-FtpFile -localFile $localFile -remoteFile $remoteFile
    }
}

# --- Dateien löschen, die nur auf dem Server existieren ---
$toDelete = $serverFiles | Where-Object { $_ -notin $localFiles }

foreach ($rel in $toDelete) {
    $remoteFile = "$ftpRoot/$rel"

    try {
        $req = [System.Net.FtpWebRequest]::Create($remoteFile)
        $req.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
        $req.Credentials = $global:creds
        $req.GetResponse() | Out-Null
        Log "Deleted: $remoteFile"
    } catch {
        Log "Failed to delete: $remoteFile"
    }
}

Write-Progress -Id 1 -Activity "Fertig" -Completed
Log "Deployment abgeschlossen."
Write-Host "Deployment abgeschlossen."