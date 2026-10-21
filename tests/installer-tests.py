"""Real POSIX-script install/rollback checks in disposable directories; no Emby is started."""
import hashlib
import pathlib
import shutil
import subprocess
import sys
import uuid

busybox, plugin, parent = map(pathlib.Path, sys.argv[1:4])
repo = pathlib.Path(__file__).resolve().parent.parent
root = parent.resolve() / ("film-index-install-test-" + uuid.uuid4().hex)
root.mkdir()
(root / ".fixture-only").write_text("installer fixture only\n")
package = root / "package with spaces"
package.mkdir()
shutil.copyfile(repo / "packaging/film-index.sh", package / "film-index.sh")
shutil.copyfile(plugin, package / "FilmIndex.Emby.dll")
digest = hashlib.sha256(plugin.read_bytes()).hexdigest()
(package / "FilmIndex.Emby.dll.sha256").write_text(digest + "  FilmIndex.Emby.dll\n")

checks = []
def check(value, message):
    if not value:
        raise AssertionError(message)
    checks.append(message)

def run(*args, ok=True):
    result = subprocess.run([str(busybox.resolve()), "sh", str(package / "film-index.sh"), *map(str, args)], capture_output=True, text=True, timeout=15)
    check((result.returncode == 0) == ok, "Script exit: " + str(args[0]) + (" succeeds" if ok else " refuses invalid operation"))
    if ok and result.returncode:
        raise AssertionError(result.stderr)
    return result

def server(name, old=True):
    directory = root / name
    (directory / "config").mkdir(parents=True)
    (directory / "plugins/configurations").mkdir(parents=True)
    (directory / "data").mkdir()
    (directory / "data/library.db").write_bytes(b"library and playback state must remain unchanged")
    if old:
        (directory / "plugins/FilmIndex.Emby.dll").write_bytes(b"old plugin")
        (directory / "plugins/configurations/Film Index Emby.json").write_text('{"old":"settings"}')
    return directory

config = server("server data with spaces")
run("install", config, "4.10.1.0", "--server-stopped")
check(hashlib.sha256((config / "plugins/FilmIndex.Emby.dll").read_bytes()).hexdigest() == digest, "Install writes the exact checked payload")
check((config / "plugins/configurations/Film Index Emby.json").read_text() == '{"old":"settings"}', "Upgrade preserves existing plugin settings")
backup = next((config / "film-index-backups").iterdir())
check((backup / "plugin.dll").read_bytes() == b"old plugin", "Backup retains the previous plugin binary")
(config / "plugins/configurations/Film Index Emby.json").write_text('{"new":"settings"}')
run("restore", backup, config, "--server-stopped")
check((config / "plugins/FilmIndex.Emby.dll").read_bytes() == b"old plugin" and (config / "plugins/configurations/Film Index Emby.json").read_text() == '{"old":"settings"}', "Rollback restores both previous plugin and previous settings")
snapshots = [entry for entry in (config / "film-index-backups").iterdir() if entry != backup]
check(len(snapshots) == 1 and hashlib.sha256((snapshots[0] / "plugin.dll").read_bytes()).hexdigest() == digest, "Rollback first preserves the version being replaced")
run("install", config, "4.10.1.0", ok=False)
run("install", config, "4.9.0.0", "--server-stopped", ok=False)
(config / "plugins/StrmAssistant.dll").write_bytes(b"legacy fixture")
run("install", config, "4.10.1.0", "--server-stopped", ok=False)
(config / "plugins/StrmAssistant.dll").unlink()
(package / "FilmIndex.Emby.dll").write_bytes(b"corrupt payload")
run("install", config, "4.10.1.0", "--server-stopped", ok=False)
check((config / "plugins/FilmIndex.Emby.dll").read_bytes() == b"old plugin" and len(list((config / "film-index-backups").iterdir())) == 2, "Checksum refusal leaves installation and backups untouched")
(backup / "plugin.dll").write_bytes(b"corrupt backup")
run("restore", backup, config, "--server-stopped", ok=False)
check((config / "data/library.db").read_bytes() == b"library and playback state must remain unchanged", "Installer and rollback never modify the native database")
shutil.copyfile(plugin, package / "FilmIndex.Emby.dll")
fresh = server("first install", old=False)
run("install", fresh, "4.10.1.0", "--server-stopped")
first_backup = next((fresh / "film-index-backups").iterdir())
run("restore", first_backup, fresh, "--server-stopped")
check(not (fresh / "plugins/FilmIndex.Emby.dll").exists() and not (fresh / "plugins/configurations/Film Index Emby.json").exists(), "First-install rollback restores the original absence of plugin and settings")
print(f"{len(checks)} installer/rollback checks passed with BusyBox; this is not fnOS/Linux certification.")
print("Fixture retained: " + str(root))
