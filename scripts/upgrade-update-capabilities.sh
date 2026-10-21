#!/bin/sh
# Administrator-only publisher/route upgrade. Does not activate a release feed.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?verified executable required}
expected=${2:?expected SHA256 required}
target=/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel
config=/etc/dustweave-ota-channel-v2/config.json
state=/var/lib/dustweave-ota-channel-v2
public=/home/nginx/dustweave-updates-v2
nginx=/ssl/nginx.conf
container=ohttps-nginx_ssl
account=dustweave-ota-v2
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
test -f "$target" && test -f "$config" && test -f "$nginx"
test ! -L "$state" && test ! -L "$public" && test ! -L "$state/capable"
install -d -o "$account" -g "$account" -m 755 "$state/capable"
for lock in "$state/publish.lock" "$state/capable/publish.lock"; do
    test ! -L "$lock"
    if [ ! -e "$lock" ]; then install -o "$account" -g "$account" -m 644 /dev/null "$lock"; fi
done
exec 9>"$state/publish.lock"
exec 8>"$state/capable/publish.lock"
flock -n 9 || { echo 'Legacy-index publication is active'; exit 1; }
flock -n 8 || { echo 'Capable-index publication is active'; exit 1; }
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$state/capability-install-$stamp"
test ! -e "$backup"
install -d -m 700 "$backup"
cp -p "$target" "$backup/publisher"
cp -p "$config" "$backup/config.json"
cp -p "$nginx" "$backup/nginx.conf"
sha256sum /home/nginx/dustweave-updates/updates.json /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel /etc/dustweave-ota-channel/config.json > "$backup/legacy.sha256"
sha256sum "$public/updates.json" "$config" > "$backup/current.sha256"
if [ -e "$public/updates-v3.json" ]; then
    test ! -L "$public/updates-v3.json"
    sha256sum "$public/updates-v3.json" >> "$backup/current.sha256"
fi
"$payload" self-test > "$backup/self-test.log"
"$payload" verify "$config"
"$payload" mirror-nginx "$nginx" "$backup/nginx-candidate.conf" "$config"
rollback() {
    trap - EXIT HUP INT TERM
    cp -p "$backup/publisher" "$target.restore"
    mv -f "$target.restore" "$target"
    cat "$backup/nginx.conf" > "$nginx"
    docker exec "$container" nginx -t && docker exec "$container" nginx -s reload || true
    echo "Capability installation rolled back; evidence: $backup"
}
trap rollback EXIT HUP INT TERM
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
su -s /bin/sh "$account" -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$state/.net $target verify $config"
cat "$backup/nginx-candidate.conf" > "$nginx"
docker exec "$container" nginx -t
docker exec "$container" nginx -s reload
sha256sum -c "$backup/legacy.sha256"
sha256sum -c "$backup/current.sha256"
trap - EXIT HUP INT TERM
echo "Capability publisher installed; all existing feeds unchanged. Backup: $backup"
