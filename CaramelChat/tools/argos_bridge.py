#!/usr/bin/env python3
"""Pont local entre What'sUP et Argos Translate.

Le processus C# écrit une commande JSON par ligne sur stdin et lit les réponses
JSON sur stdout. Aucun port réseau n'est ouvert : le moteur reste strictement
local et n'est pas exposé sur Internet.

Commandes :
  {"id":"1","op":"status"}
  {"id":"2","op":"translate","text":"...","from":"fr","to":"en"}
  {"id":"3","op":"list"}
  {"id":"4","op":"install","from":"fr","to":"en","jobId":"..."}
  {"id":"5","op":"remove","from":"fr","to":"en"}
"""

import json
import os
import shutil
import sys
import tempfile
import urllib.request

ARGOS_PATH = None
args = sys.argv[1:]
if "--argos-path" in args:
    try:
        ARGOS_PATH = args[args.index("--argos-path") + 1]
    except IndexError:
        ARGOS_PATH = None

if ARGOS_PATH and os.path.isdir(ARGOS_PATH):
    sys.path.insert(0, ARGOS_PATH)

IMPORT_ERROR = None
VERSION = "inconnue"

try:
    import argostranslate
    from argostranslate import package as argos_package
    from argostranslate import translate as argos_translate

    VERSION = getattr(argostranslate, "__version__", "installée")
    ARGOS_AVAILABLE = True
except Exception as exc:  # ImportError, erreur de dépendance…
    ARGOS_AVAILABLE = False
    IMPORT_ERROR = "%s : %s" % (type(exc).__name__, exc)


def refresh_translation_cache():
    """Argos mémorise les langues installées : on vide ce cache après une installation."""
    for name in ("get_installed_languages", "get_translation_from_codes"):
        function = getattr(argos_translate, name, None)
        cache_clear = getattr(function, "cache_clear", None)
        if callable(cache_clear):
            try:
                cache_clear()
            except Exception:
                pass


def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def fail(message):
    return {"ok": False, "error": message}


def installed_pairs():
    if ARGOS_AVAILABLE:
        refresh_translation_cache()
    pairs = []
    try:
        for package in argos_package.get_installed_packages():
            from_code = getattr(package, "from_code", None)
            to_code = getattr(package, "to_code", None)
            if from_code and to_code:
                pairs.append({"from": from_code, "to": to_code})
        return pairs
    except Exception:
        pass

    # Repli : déduire les paires des traductions chargées par Argos.
    try:
        for language in argos_translate.get_installed_languages():
            for other in argos_translate.get_installed_languages():
                if language.code == other.code:
                    continue
                try:
                    if language.get_translation(other) is not None:
                        pairs.append({"from": language.code, "to": other.code})
                except Exception:
                    continue
    except Exception:
        pass
    return pairs


def installed_languages():
    if ARGOS_AVAILABLE:
        refresh_translation_cache()
    languages = []
    try:
        for language in argos_translate.get_installed_languages():
            languages.append(
                {"code": getattr(language, "code", ""), "name": getattr(language, "name", "")}
            )
    except Exception:
        pass
    return languages


def refresh_package_index():
    argos_package.update_package_index()


def available_packages():
    refresh_package_index()
    return argos_package.get_available_packages()


def package_data_directories():
    candidates = []
    try:
        from argostranslate import settings as argos_settings

        data_dir = getattr(argos_settings, "data_dir", None)
        if data_dir:
            candidates.append(data_dir)
    except Exception:
        pass

    candidates.append(os.path.join(os.path.expanduser("~"), ".local", "share", "argos-translate"))
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        candidates.append(os.path.join(local_app_data, "argos-translate"))
    return candidates


def remove_package(from_code, to_code):
    removed = False
    for data_dir in package_data_directories():
        packages_dir = os.path.join(data_dir, "packages")
        if not os.path.isdir(packages_dir):
            continue
        for name in os.listdir(packages_dir):
            folder = os.path.join(packages_dir, name)
            metadata_path = os.path.join(folder, "metadata.json")
            if not os.path.isfile(metadata_path):
                continue
            try:
                with open(metadata_path, "r", encoding="utf-8") as handle:
                    metadata = json.load(handle)
            except Exception:
                continue
            if metadata.get("from_code") != from_code or metadata.get("to_code") != to_code:
                continue
            shutil.rmtree(folder, ignore_errors=True)
            removed = True
    return removed


def download_with_progress(url, job_id):
    target = tempfile.mktemp(suffix=".argosmodel")
    request = urllib.request.Request(url, headers={"User-Agent": "WhatsUP-Argos/1.0"})
    with urllib.request.urlopen(request, timeout=120) as response:
        total = int(response.headers.get("Content-Length") or 0)
        downloaded = 0
        with open(target, "wb") as handle:
            while True:
                chunk = response.read(262144)
                if not chunk:
                    break
                handle.write(chunk)
                downloaded += len(chunk)
                if total > 0:
                    emit(
                        {
                            "event": "progress",
                            "jobId": job_id,
                            "percent": round(downloaded * 100.0 / total, 1),
                        }
                    )
    return target


def handle(command):
    request_id = command.get("id")
    op = command.get("op")
    base = {"id": request_id}

    if op == "status":
        base.update(
            {
                "ok": ARGOS_AVAILABLE,
                "version": VERSION,
                "python": sys.executable,
                "argosPath": ARGOS_PATH or "",
                "pairs": installed_pairs() if ARGOS_AVAILABLE else [],
                "languages": installed_languages() if ARGOS_AVAILABLE else [],
            }
        )
        if not ARGOS_AVAILABLE:
            base["error"] = (
                "Argos Translate n'est pas installé pour ce Python (%s). "
                "Lancez tools\\setup-argos.ps1 ou installez le paquet argostranslate. Détail : %s"
                % (sys.executable, IMPORT_ERROR)
            )
        return base

    if not ARGOS_AVAILABLE:
        base.update(fail("Argos Translate n'est pas disponible. Détail : %s" % IMPORT_ERROR))
        return base

    if op == "translate":
        text = (command.get("text") or "").strip()
        from_code = command.get("from")
        to_code = command.get("to")
        if not text or not from_code or not to_code:
            base.update(fail("Texte ou langues manquants."))
            return base
        if from_code == to_code:
            base.update(fail("Les langues source et cible doivent être différentes."))
            return base
        refresh_translation_cache()
        try:
            translated = argos_translate.translate(text, from_code, to_code)
        except Exception as exc:
            base.update(
                fail(
                    "Traduction %s → %s impossible : %s. Vérifiez que le modèle est installé."
                    % (from_code, to_code, exc)
                )
            )
            return base
        base.update({"ok": True, "text": translated})
        return base

    if op == "list":
        try:
            packages = available_packages()
            available = []
            for package in packages:
                available.append(
                    {
                        "from": getattr(package, "from_code", ""),
                        "to": getattr(package, "to_code", ""),
                        "version": getattr(package, "package_version", None),
                        "size": getattr(package, "size", None),
                    }
                )
            base.update({"ok": True, "available": available, "pairs": installed_pairs()})
        except Exception as exc:
            base.update(fail("Recherche des modèles impossible (connexion Internet ?) : %s" % exc))
            base["pairs"] = installed_pairs()
        return base

    if op == "install":
        from_code = command.get("from")
        to_code = command.get("to")
        job_id = command.get("jobId")
        if not from_code or not to_code:
            base.update(fail("Langues manquantes."))
            return base
        try:
            package = None
            for candidate in available_packages():
                if candidate.from_code == from_code and candidate.to_code == to_code:
                    package = candidate
                    break
            if package is None:
                base.update(fail("Aucun modèle %s → %s n'est proposé par Argos." % (from_code, to_code)))
                return base

            links = getattr(package, "links", None) or []
            if links:
                path = download_with_progress(links[0], job_id)
            else:
                path = package.download()

            argos_package.install_from_path(path)
            try:
                os.remove(path)
            except Exception:
                pass
            base.update({"ok": True})
        except Exception as exc:
            base.update(fail("Installation impossible : %s" % exc))
        return base

    if op == "remove":
        from_code = command.get("from")
        to_code = command.get("to")
        if not from_code or not to_code:
            base.update(fail("Langues manquantes."))
            return base
        try:
            removed = remove_package(from_code, to_code)
            if not removed:
                base.update(fail("Ce modèle n'est pas installé."))
            else:
                base.update({"ok": True})
        except Exception as exc:
            base.update(fail("Suppression impossible : %s" % exc))
        return base

    base.update(fail("Commande inconnue : %s" % op))
    return base


def main():
    for raw_line in sys.stdin:
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        try:
            command = json.loads(raw_line)
        except Exception:
            continue

        try:
            response = handle(command)
        except Exception as exc:
            response = {"id": command.get("id")}
            response.update(fail("Erreur interne d'Argos : %s" % exc))

        emit(response)


if __name__ == "__main__":
    main()
