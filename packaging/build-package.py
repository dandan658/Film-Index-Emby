"""Create a single-DLL preview package from a clean, committed source checkout."""
import argparse
import hashlib
import io
import json
import pathlib
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True)
parser.add_argument("--tag", default="v0.1.0-preview.1")
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parent.parent
def git(*args):
    return subprocess.check_output(["git", *args], cwd=repo)
if git("status", "--porcelain").strip():
    raise SystemExit("Commit source and verification reports before packaging")
revision = git("rev-parse", "HEAD").decode().strip()
output = pathlib.Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
stem = "FilmIndex.Emby-" + args.tag + "-emby4.10.1.0"
dll = repo / "StrmAssistant/bin/Release/net8.0/FilmIndex.Emby.dll"
if not dll.exists():
    raise SystemExit("Build the production DLL first")
source = git("archive", "--format=zip", "--prefix=Film-Index-Emby/", "HEAD")
source_name = stem + "-source.zip"
(output / source_name).write_bytes(source)
payload = {
    "FilmIndex.Emby.dll": dll.read_bytes(),
    "film-index.sh": (repo / "packaging/film-index.sh").read_bytes().replace(b"\r\n", b"\n"),
    "INSTALL.zh-CN.md": (repo / "packaging/INSTALL.zh-CN.md").read_bytes(),
    "README.md": (repo / "README.md").read_bytes(),
    "DEVELOPMENT.md": (repo / "DEVELOPMENT.md").read_bytes(),
    "VERIFICATION.json": (repo / "VERIFICATION.json").read_bytes(),
    "THIRD-PARTY-NOTICES.md": (repo / "packaging/THIRD-PARTY-NOTICES.md").read_bytes(),
    "LICENSE": (repo / "LICENSE").read_bytes(),
    "source/" + source_name: source,
}
for directory in ("OpenCC", "DiscUtils", "Harmony"):
    for file in (repo / "StrmAssistant/Data" / directory).iterdir():
        if file.name.startswith(("LICENSE", "UPSTREAM")):
            payload["licenses/" + directory + "/" + file.name] = file.read_bytes()
digest = hashlib.sha256(payload["FilmIndex.Emby.dll"]).hexdigest()
payload["FilmIndex.Emby.dll.sha256"] = (digest + "  FilmIndex.Emby.dll\n").encode()
manifest = {"tag": args.tag, "source_revision": revision, "target_emby": "4.10.1.0",
            "status": "preview; user acceptance pending", "fnos_tested": False,
            "full_feature_parity": False, "dll_sha256": digest,
            "files": {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()}}
payload["manifest.json"] = (json.dumps(manifest, indent=2) + "\n").encode()
package = output / (stem + ".zip")
with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name, data in sorted(payload.items()):
        archive.writestr(name, data)
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None
    assert [name for name in archive.namelist() if name.lower().endswith(".dll")] == ["FilmIndex.Emby.dll"]
    for name, expected in manifest["files"].items():
        assert hashlib.sha256(archive.read(name)).hexdigest() == expected
checksums = output / "SHA256SUMS"
checksums.write_text("".join(hashlib.sha256(path.read_bytes()).hexdigest() + "  " + path.name + "\n"
                             for path in (package, output / source_name)), encoding="ascii")
print(json.dumps({"package": str(package), "source": str(output / source_name), "revision": revision,
                  "dll_sha256": digest, "payload_entries": len(payload)}, indent=2))
