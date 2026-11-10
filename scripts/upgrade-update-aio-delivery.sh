#!/bin/sh
# Administrator-only: CDN-only AIO routing and pre-publication verification.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?verified publisher executable required}
expected=${2:?new executable SHA256 required}
previous=${3:?expected installed executable SHA256 required}
feed_hash=${4:?expected current AIO feed SHA256 required}
target=/usr/local/lib/dustweave-ota-channel-aio/Dustweave.OtaChannel
config=/etc/dustweave-ota-channel-aio/config.json
state=/var/lib/dustweave-ota-channel-aio
public=/home/nginx/dustweave-updates-aio
nginx=/ssl/nginx.conf
container=ohttps-nginx_ssl
service=dustweave-ota-aio-mirror.service
timer=dustweave-ota-aio-mirror.timer
for path in "$payload" "$target" "$config" "$state" "$state/publish.lock" "$nginx"; do
    test ! -L "$path" || { echo "Linked installation path rejected: $path"; exit 1; }
done
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
[ "$(sha256sum "$target" | cut -d ' ' -f1)" = "$previous" ]
[ "$(sha256sum "$public/releases.json" | cut -d ' ' -f1)" = "$feed_hash" ]
backup=$(mktemp -d "$state/cdn-only-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
cp -p "$target" "$backup/publisher"
cp -p "$nginx" "$backup/nginx.conf"
sha256sum "$config" "$public/releases.json" \
 /home/nginx/dustweave-updates/updates.json \
 /home/nginx/dustweave-updates-v2/updates.json \
 /home/nginx/dustweave-updates-v2/updates-v3.json \
 /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel \
 /usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel \
 /var/lib/dustweave-ota-aio-home/.ssh/authorized_keys > "$backup/unchanged.sha256"
for feed in releases-v2.json releases-v3.json .mirror/manifests/releases-1.6.0.json; do
    if test -f "$public/$feed"; then sha256sum "$public/$feed" >> "$backup/unchanged.sha256"; fi
done
"$payload" verify "$config" > "$backup/before-status.json"
"$payload" bundle-nginx "$nginx" "$backup/nginx-candidate.conf" "$config"
timer_active=0
if systemctl is-active --quiet "$timer"; then timer_active=1; fi
replaced=0
web_changed=0
rollback() {
    trap - EXIT HUP INT TERM
    if test "$replaced" = 1; then
        cp -p "$backup/publisher" "$target.restore"
        mv -f "$target.restore" "$target"
    fi
    if test "$web_changed" = 1; then
        # nginx.conf is a bind-mounted file; preserve its inode.
        cat "$backup/nginx.conf" > "$nginx"
        docker exec "$container" nginx -t && docker exec "$container" nginx -s reload || true
    fi
    if test "$timer_active" = 1; then systemctl start "$timer" || true; fi
    echo "CDN-only installation rolled back; evidence: $backup"
}
trap rollback EXIT
trap 'exit 1' HUP INT TERM
systemctl stop "$timer"
systemctl stop "$service"
exec 9>"$state/publish.lock"
flock -n 9 || { echo 'A publication is active; retry after it completes'; exit 1; }
if test -d "$state/capable"; then
    test ! -L "$state/capable" && test ! -L "$state/capable/publish.lock"
    exec 8>"$state/capable/publish.lock"
    flock -n 8 || { echo 'A 1.6 publication is active; retry after it completes'; exit 1; }
fi
if test -d "$state/current"; then
    test ! -L "$state/current" && test ! -L "$state/current/publish.lock"
    exec 7>"$state/current/publish.lock"
    flock -n 7 || { echo 'A current publication is active; retry after it completes'; exit 1; }
fi
[ "$(sha256sum "$public/releases.json" | cut -d ' ' -f1)" = "$feed_hash" ]
[ "$(sha256sum "$target" | cut -d ' ' -f1)" = "$previous" ]
cmp "$backup/nginx.conf" "$nginx"
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
replaced=1
web_changed=1
cat "$backup/nginx-candidate.conf" > "$nginx"
docker exec "$container" nginx -t
docker exec "$container" nginx -s reload
runuser -u dustweave-ota-aio -- env DOTNET_BUNDLE_EXTRACT_BASE_DIR="$state/.net" \
 SSH_ORIGINAL_COMMAND='dustweave-ota status' "$target" serve > "$backup/after-status.json"
sha256sum -c "$backup/unchanged.sha256"
flock -u 9
exec 9>&-
if test "$timer_active" = 1; then systemctl start "$timer"; fi
systemctl start --no-block "$service"
trap - EXIT HUP INT TERM
echo "CDN-only AIO delivery installed; signed feeds and other lanes unchanged. Backup: $backup"
