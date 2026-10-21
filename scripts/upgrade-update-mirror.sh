#!/bin/sh
# Upgrade an already configured v2 publisher without replacing its service units.
set -eu
[ "$(id -u)" = 0 ] || exit 1
payload=${1:?verified executable required}
expected=${2:?new executable SHA256 required}
previous=${3:?expected installed executable SHA256 required}
target=/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel
config=/etc/dustweave-ota-channel-v2/config.json
state=/var/lib/dustweave-ota-channel-v2
nginx=/ssl/nginx.conf
container=ohttps-nginx_ssl
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
[ "$(sha256sum "$target" | cut -d ' ' -f1)" = "$previous" ]
test -f /etc/systemd/system/dustweave-ota-mirror.timer
test ! -L "$state/publish.lock"
exec 9>"$state/publish.lock"
flock -n 9 || { echo 'A publication is active'; exit 1; }
backup="$state/mirror-upgrade-$(date -u +%Y%m%dT%H%M%SZ)"
install -d -m 700 "$backup"
cp -p "$target" "$backup/publisher"
cp -p "$nginx" "$backup/nginx.conf"
sha256sum "$config" /home/nginx/dustweave-updates-v2/updates.json /home/nginx/dustweave-updates/updates.json /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel /etc/dustweave-ota-channel/config.json > "$backup/unchanged.sha256"
"$payload" verify "$config"
"$payload" mirror-nginx "$nginx" "$backup/nginx-candidate.conf" "$config"
rollback() {
    cp -p "$backup/publisher" "$target.restore"
    mv -f "$target.restore" "$target"
    cat "$backup/nginx.conf" > "$nginx"
    docker exec "$container" nginx -t && docker exec "$container" nginx -s reload || true
    echo "Publisher upgrade rolled back; evidence: $backup"
}
trap rollback EXIT HUP INT TERM
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
cat "$backup/nginx-candidate.conf" > "$nginx"
docker exec "$container" nginx -t
docker exec "$container" nginx -s reload
sha256sum -c "$backup/unchanged.sha256"
su -s /bin/sh dustweave-ota-v2 -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$state/.net $target mirror-health"
trap - EXIT HUP INT TERM
echo "V2 repair-capable publisher installed; feed and legacy lane unchanged. Backup: $backup"
