"""Build a minimal installation ZIP and standalone tutorial; no source/test assets."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--tag', default='v0.1.0-preview.1')
parser.add_argument('--source-revision', required=True, help='Commit used to build the DLL')
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parent.parent
revision = subprocess.check_output(['git', 'rev-parse', args.source_revision + '^{commit}'], cwd=repo).decode().strip()
output = pathlib.Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
dll = (repo / 'StrmAssistant/bin/Release/net8.0/FilmIndex.Emby.dll').read_bytes()
digest = hashlib.sha256(dll).hexdigest()
tutorial = (repo / 'packaging/INSTALL.zh-CN.md').read_text(encoding='utf-8-sig')
tutorial += '\n## 构建和对应源码\n\n'
tutorial += '插件 SHA-256：`' + digest + '`。\n\n'
tutorial += '本 DLL 对应提交：`' + revision + '`。\n\n'
tutorial += '[完整对应源码下载](https://github.com/dandan658/Film-Index-Emby/archive/' + revision + '.zip)\n\n'
tutorial += '依赖的版本和源码地址见下方声明。源码在同一 GitHub 仓库公开提供，不需要另行购买或申请。\n\n'
tutorial += '## 版权与依赖声明\n\n'
tutorial += (repo / 'packaging/THIRD-PARTY-NOTICES.md').read_text(encoding='utf-8-sig')
for label, path in [('GPL-3.0', repo / 'LICENSE'), ('Harmony MIT', repo / 'StrmAssistant/Data/Harmony/LICENSE'), ('DiscUtils MIT', repo / 'StrmAssistant/Data/DiscUtils/LICENSE.txt'), ('OpenCC Apache-2.0', repo / 'StrmAssistant/Data/OpenCC/LICENSE')]:
    tutorial += '\n### ' + label + '\n\n```text\n' + path.read_text(encoding='utf-8-sig').strip() + '\n```\n'
encoded = tutorial.encode('utf-8')
(output / 'INSTALL.zh-CN.md').write_bytes(encoded)
package = output / ('FilmIndex.Emby-' + args.tag + '-emby4.10.1.0.zip')
payload = {'FilmIndex.Emby.dll': dll, 'INSTALL.zh-CN.md': encoded}
with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name, content in payload.items():
        archive.writestr(name, content)
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None
    assert set(archive.namelist()) == set(payload)
    for name, content in payload.items():
        assert archive.read(name) == content
print(json.dumps({'package': str(package), 'tutorial': str(output / 'INSTALL.zh-CN.md'), 'source_revision': revision, 'dll_sha256': digest, 'entries': list(payload)}, indent=2))
