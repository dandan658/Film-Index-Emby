#!/bin/sh
set -eu

fail() { printf '%s\n' "$*" >&2; exit 1; }
usage() { fail 'Usage: film-index.sh install CONFIG_ROOT 4.10.1.0 --server-stopped | restore BACKUP_ROOT CONFIG_ROOT --server-stopped'; }
canonical() { [ -d "$1" ] || fail 'Directory does not exist'; (cd "$1" && pwd -P); }
validate_root() {
    [ "$config" != / ] || fail 'A filesystem root is not an Emby data directory'
    [ -d "$config/config" ] && [ -d "$config/plugins" ] || fail 'Expected an existing Emby data directory with config and plugins'
    [ ! -L "$config/plugins" ] && [ ! -L "$config/plugins/configurations" ] || fail 'Plugin directories must not be symbolic links'
    [ ! -L "$config/plugins/FilmIndex.Emby.dll" ] && [ ! -L "$config/plugins/configurations/Film Index Emby.json" ] || fail 'Plugin files must not be symbolic links'
}
backup_current() {
    backup_parent="$config/film-index-backups"
    [ ! -L "$backup_parent" ] || fail 'Backup directory must not be a symbolic link'
    mkdir -p "$backup_parent"
    backup="$backup_parent/$(date -u +%Y%m%dT%H%M%SZ)-$$"
    (umask 077; mkdir "$backup")
    printf '%s\n' 'FilmIndex.Emby.backup.v1' > "$backup/FORMAT"
    : > "$backup/STATE"
    if [ -f "$config/plugins/FilmIndex.Emby.dll" ]; then
        cp -p "$config/plugins/FilmIndex.Emby.dll" "$backup/plugin.dll"
        printf '%s\n' 'plugin=1' >> "$backup/STATE"
    else printf '%s\n' 'plugin=0' >> "$backup/STATE"; fi
    if [ -f "$config/plugins/configurations/Film Index Emby.json" ]; then
        cp -p "$config/plugins/configurations/Film Index Emby.json" "$backup/settings.json"
        printf '%s\n' 'settings=1' >> "$backup/STATE"
    else printf '%s\n' 'settings=0' >> "$backup/STATE"; fi
    (cd "$backup"; for file in FORMAT STATE plugin.dll settings.json; do
        if [ -f "$file" ]; then sha256sum "$file"; fi
    done > SHA256SUMS)
}
stage=''
cleanup() { if [ -n "$stage" ] && [ -f "$stage" ]; then rm -f "$stage"; fi; }
trap cleanup EXIT
trap 'cleanup; exit 1' HUP INT TERM
[ "$#" -gt 0 ] || usage
action=$1; shift
case "$action" in
install)
    [ "$#" -eq 3 ] && [ "$2" = '4.10.1.0' ] && [ "$3" = '--server-stopped' ] || usage
    config=$(canonical "$1"); validate_root
    [ ! -f "$config/plugins/StrmAssistant.dll" ] && [ ! -f "$config/plugins/StrmAssistant.Pro.dll" ] || fail 'Move the existing StrmAssistant DLL outside plugins before installing this independent plugin'
    package=$(CDPATH='' cd -- "$(dirname -- "$0")" && pwd -P)
    [ -f "$package/FilmIndex.Emby.dll" ] && [ -f "$package/FilmIndex.Emby.dll.sha256" ] || fail 'Installation payload or checksum missing'
    (cd "$package"; sha256sum -c FilmIndex.Emby.dll.sha256) || fail 'Plugin checksum mismatch'
    backup_current
    stage="$config/plugins/.film-index-install-$$.dll"
    [ ! -e "$stage" ] && [ ! -L "$stage" ] || fail 'Installation staging path already exists'
    cp "$package/FilmIndex.Emby.dll" "$stage"; chmod 644 "$stage"
    mv -f "$stage" "$config/plugins/FilmIndex.Emby.dll"; stage=''
    printf 'Installed Film Index Emby for 4.10.1.0. Backup: %s\n' "$backup"
    ;;
restore)
    [ "$#" -eq 3 ] && [ "$3" = '--server-stopped' ] || usage
    restore_from=$(canonical "$1"); config=$(canonical "$2"); validate_root
    [ "$(cat "$restore_from/FORMAT")" = 'FilmIndex.Emby.backup.v1' ] || fail 'Unknown backup format'
    # The manifest is data; do not source or execute any backup content.
    (cd "$restore_from"; sha256sum -c SHA256SUMS) || fail 'Backup checksum mismatch'
    grep -Eq '^plugin=[01]$' "$restore_from/STATE" && grep -Eq '^settings=[01]$' "$restore_from/STATE" || fail 'Invalid backup state'
    if grep -qx 'plugin=1' "$restore_from/STATE"; then [ -f "$restore_from/plugin.dll" ] || fail 'Backup DLL missing'; fi
    if grep -qx 'settings=1' "$restore_from/STATE"; then [ -f "$restore_from/settings.json" ] || fail 'Backup settings missing'; fi
    # Preserve the currently installed version before any rollback mutation.
    backup_current
    if grep -qx 'plugin=1' "$restore_from/STATE"; then
        [ -f "$restore_from/plugin.dll" ] || fail 'Backup DLL missing'
        stage="$config/plugins/.film-index-restore-$$.dll"
        [ ! -e "$stage" ] && [ ! -L "$stage" ] || fail 'Restore staging path already exists'
        cp -p "$restore_from/plugin.dll" "$stage"; mv -f "$stage" "$config/plugins/FilmIndex.Emby.dll"; stage=''
    else rm -f "$config/plugins/FilmIndex.Emby.dll"; fi
    if grep -qx 'settings=1' "$restore_from/STATE"; then
        [ -f "$restore_from/settings.json" ] || fail 'Backup settings missing'
        mkdir -p "$config/plugins/configurations"
        stage="$config/plugins/configurations/.film-index-restore-$$.json"
        [ ! -e "$stage" ] && [ ! -L "$stage" ] || fail 'Restore settings staging path already exists'
        cp -p "$restore_from/settings.json" "$stage"; mv -f "$stage" "$config/plugins/configurations/Film Index Emby.json"; stage=''
    else rm -f "$config/plugins/configurations/Film Index Emby.json"; fi
    printf 'Plugin and settings restored. Pre-rollback backup: %s\n' "$backup"
    ;;
*) usage ;;
esac
