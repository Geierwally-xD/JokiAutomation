<#
.SYNOPSIS
	Kopiert Logdateien vom Raspberry Pi und von JoKi Automation (lokal) in einen Dropbox-Ordner.

.DESCRIPTION
	Dieses Skript kopiert die Log-Dateien aus einem konfigurierbaren Netzwerkpfad (z. B. ein gemounteter Samba-Share vom Raspberry Pi)
	sowie aus dem lokalen JoKi Automation Log-Verzeichnis nach Dropbox. Es erstellt Zielordner, protokolliert den Ablauf und
	unterstützt einen Trockenlauf (DryRun).

.PARAMETER DropboxPath
	Pfad zum lokalen Dropbox-Ordner. Default: $env:USERPROFILE\Dropbox

.PARAMETER RaspberryShare
	UNC-Pfad zum Raspberry-Logordner (z. B. \\raspberry\logs). Standardwert: \\raspberry\logs

.PARAMETER JoKiLogsPath
	Pfad zu den JoKi Automation Logs auf dem lokalen Rechner. Standardwert: Ordner in dem das Skript liegt (z. B. C:\jokiAutomation)

.PARAMETER DryRun
	Wenn gesetzt, werden nur die Aktionen angezeigt, aber keine Kopien durchgeführt.

#>
param(
	[string]
	$DropboxPath = "$env:USERPROFILE\Dropbox",

	[string]
	$RaspberryShare = "\\raspberry\logs",

	[string]
	$JoKiLogsPath,

	[switch]
	$DryRun
)

function Write-Log {
	param([string] $Message)
	$ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
	$entry = "[$ts] $Message"
	Write-Output $entry
	if ($global:LogFile) { Add-Content -Path $global:LogFile -Value $entry -ErrorAction SilentlyContinue }
}

try {
	# Initialisieren
	$now = Get-Date -Format "yyyyMMdd_HHmmss"
	# Ermittele Script-Verzeichnis (erlaubt Platzierung z.B. in C:\jokiAutomation)
	if ($PSBoundParameters.ContainsKey('JoKiLogsPath') -and $JoKiLogsPath) {
		# Benutzer hat Pfad übergeben -> verwende ihn
	} else {
		if ($PSScriptRoot) { $scriptDir = $PSScriptRoot } else { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition }
		# Standard: Logs liegen direkt im gleichen Ordner wie das Skript (z. B. C:\jokiAutomation)
		$JoKiLogsPath = $scriptDir
		Write-Log "JoKiLogsPath nicht übergeben. Setze auf Script-Ordner: $JoKiLogsPath"
		# Versuche Raspberry-Pfad aus network.cfg im Script-Ordner zu lesen
		$cfgPath = Join-Path -Path $scriptDir -ChildPath "network.cfg"
		if (Test-Path -Path $cfgPath) {
			try {
				$cfg = Get-Content -Path $cfgPath -ErrorAction Stop
				foreach ($line in $cfg) {
					$l = $line.Trim()
					if ($l -eq '' -or $l.StartsWith('#')) { continue }
					# DROPBOX_LOG_PATH;C:\Path\To\Dropbox
					if ($l -match '^DROPBOX_LOG_PATH\s*;\s*(.+)$') {
						$dp = $Matches[1].Trim().Trim('"')
						if ($dp) { $DropboxPath = $dp; Write-Log "DROPBOX_LOG_PATH aus network.cfg gesetzt auf: $DropboxPath"; continue }
					}
					# RaspberryPi entries: RaspberryPi_Main;192.168.178.50;22
					if ($l -match '^(RaspberryPi[^;]*?)\s*;\s*([^;\s]+)') {
						$ip = $Matches[2].Trim()
						if ($ip -match '^[0-9]+(\.[0-9]+){3}$') {
							# Baue UNC-Pfad als \<IP>\logs
							$RaspberryShare = "\\\\$ip\\logs"
							Write-Log "Raspberry IP aus network.cfg gefunden: $ip -> RaspberryShare gesetzt auf: $RaspberryShare"
							break
						}
					}
				}
			}
			catch {
				Write-Log "Warnung: network.cfg konnte nicht gelesen werden: $($_.Exception.Message)"
			}
		} else {
			Write-Log "network.cfg nicht gefunden im Script-Ordner: $cfgPath"
		}
	}
	$global:LogFile = Join-Path -Path $DropboxPath -ChildPath "CopyLogs_$((Get-Date).ToString('yyyyMMdd')).log"

	Write-Log "Starte Kopiervorgang"
	Write-Log "DropboxPath = $DropboxPath"
	Write-Log "RaspberryShare = $RaspberryShare"
	Write-Log "JoKiLogsPath = $JoKiLogsPath"

	# Zielordner
	$destRaspberry = Join-Path -Path $DropboxPath -ChildPath "RaspberryLogs"
	$destJoKi = Join-Path -Path $DropboxPath -ChildPath "JoKiAutomationLogs"

	if ($DryRun) {
		Write-Log "DryRun aktiviert - es werden keine Dateien kopiert."
	} else {
		# Erstelle Dropbox-Ordner falls notwendig
		if (-not (Test-Path -Path $DropboxPath)) {
			Write-Log "Dropbox-Pfad existiert nicht. Erstelle: $DropboxPath"
			New-Item -Path $DropboxPath -ItemType Directory -Force | Out-Null
		}
	}

	function Copy-OnlyLogs {
		param(
			[string] $Source,
			[string] $Destination
		)

		$patterns = @('*.log','*.txt','*.csv')
		if (-not (Test-Path -Path $Source)) {
			Write-Log "WARNUNG: Quellpfad nicht gefunden: $Source"
			return
		}

		Write-Log "Suche Log-Dateien in: $Source"
		if ($DryRun) { Write-Log "DryRun: Keine Dateien werden kopiert, nur aufgelistet." }

		# Ensure destination exists
		if (-not $DryRun) { New-Item -Path $Destination -ItemType Directory -Force | Out-Null }

		$srcRoot = (Get-Item -LiteralPath $Source).FullName.TrimEnd('\') + '\'
		$files = Get-ChildItem -Path $Source -Recurse -File -Include $patterns -ErrorAction SilentlyContinue
		if (-not $files -or $files.Count -eq 0) {
			Write-Log "Keine Logdateien gefunden in: $Source"
			return
		}

		foreach ($f in $files) {
			$rel = $f.FullName.Substring($srcRoot.Length)
			$targetDir = Join-Path -Path $Destination -ChildPath (Split-Path -Path $rel -Parent)
			if (-not $targetDir) { $targetDir = $Destination }
			if (-not $DryRun) {
				if (-not (Test-Path -Path $targetDir)) { New-Item -Path $targetDir -ItemType Directory -Force | Out-Null }
				Copy-Item -Path $f.FullName -Destination (Join-Path $targetDir $f.Name) -Force -ErrorAction Stop
			}
			Write-Log "$(if ($DryRun) { 'DRY:' } else { '' }) Kopiere: $($f.FullName) -> $targetDir"
		}

		Write-Log "Fertig mit Quelle: $Source"
	}

	# Kopiere Raspberry-Logs (nur Log-Dateien)
	Write-Log "Verarbeite Raspberry-Logs"
	Write-Log "Raspberry-Quelle: $RaspberryShare -> Ziel: $destRaspberry"
	Copy-OnlyLogs -Source $RaspberryShare -Destination $destRaspberry

	# Kopiere JoKi Automation Logs (nur Log-Dateien)
	Write-Log "Verarbeite JoKi Automation Logs"
	Write-Log "JoKi-Quelle: $JoKiLogsPath -> Ziel: $destJoKi"
	Copy-OnlyLogs -Source $JoKiLogsPath -Destination $destJoKi

	Write-Log "Kopiervorgang beendet"
	exit 0
}
catch {
	Write-Log "FEHLER: $($_.Exception.Message)"
	exit 1
}

<# Beispiele:
	.\scripts\Copy-LogsToDropbox.ps1
	.\scripts\Copy-LogsToDropbox.ps1 -DropboxPath "D:\Dropbox (Benutzer)" -RaspberryShare "\\192.168.1.50\logs" -DryRun
#>
