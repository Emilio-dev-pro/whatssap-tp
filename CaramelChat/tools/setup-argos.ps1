# Installe Argos Translate dans un environnement Python isolé pour What'sUP.
# Aucun paquet n'est ajouté à l'installation Python de l'utilisateur.
#
#   powershell -ExecutionPolicy Bypass -File tools\setup-argos.ps1
#
# L'application détecte automatiquement cet environnement :
#   %LOCALAPPDATA%\WhatsUP\argos-venv\Scripts\python.exe
# Les modèles de langues se téléchargent ensuite depuis
# Mon profil > Langues > Gérer les langues hors ligne.

$ErrorActionPreference = "Stop"

$python = "python"
try {
    & $python --version | Out-Null
} catch {
    Write-Host "Python 3 n'est pas disponible. Installez-le depuis https://www.python.org/downloads/" -ForegroundColor Red
    exit 1
}

$root = Join-Path $env:LOCALAPPDATA "WhatsUP"
$venv = Join-Path $root "argos-venv"
$venvPython = Join-Path $venv "Scripts\python.exe"

if (-not (Test-Path $venvPython)) {
    Write-Host "Création de l'environnement isolé : $venv"
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    & $python -m venv $venv
}

Write-Host "Installation de la dernière version d'Argos Translate…"
& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install --upgrade argostranslate

Write-Host ""
Write-Host "Argos Translate est installé dans :" -ForegroundColor Green
Write-Host "  $venvPython"
Write-Host ""
Write-Host "Redémarrez What'sUP, puis ouvrez : Mon profil > Langues > Gérer les langues hors ligne."
Write-Host "Téléchargez au minimum les modèles Français → Anglais et Anglais → Français."
