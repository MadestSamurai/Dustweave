#!/bin/sh
# Administrator-only, scoped upgrade of the v2 file delivery route.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?verified executable required}
expected=${2:?expected SHA256 required}
mirror=${3:?verified HTTPS mirror base required}
target=/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel
config=/etc/dustweave-ota-channel-v2/config.json
state=/var/lib/dustweave-ota-channel-v2
public=/home/nginx/dustweave-updates-v2
nginx=/ssl/nginx.conf
container=ohttps-nginx_ssl
account=dustweave-ota-v2
service=dustweave-ota-mirror
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
test -f "$target" && test -f "$config" && test -f "$nginx"
# Do not replace the publisher while a publication or preparation is active.
test ! -L "$state/publish.lock"
if [ ! -e "$state/publish.lock" ]; then
    install -o "$account" -g "$account" -m 644 /dev/null "$state/publish.lock"
fi
exec 9>"$state/publish.lock"
flock -n 9 || { echo 'OTA publication is active; retry after it completes'; exit 1; }
stamp=$(date -u +%Y%m%dT%H%M%SZ)
backup="$state/mirror-install-$stamp"
install -d -m 700 "$backup"
cp -p "$target" "$backup/publisher"
cp -p "$config" "$backup/config.json"
cp -p "$nginx" "$backup/nginx.conf"
sha256sum /home/nginx/dustweave-updates/updates.json /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel /etc/dustweave-ota-channel/config.json > "$backup/legacy.sha256"
sha256sum "$public/updates.json" > "$backup/current-feed.sha256"
for unit in "$service.service" "$service.timer"; do
    test ! -f "/etc/systemd/system/$unit" || { echo 'Mirror is already installed; inspect before changing it'; exit 1; }
done
"$payload" verify "$config"
"$payload" mirror-config "$config" "$backup/new-config.json" "$mirror"
"$payload" mirror-nginx "$nginx" "$backup/nginx-candidate.conf" "$backup/new-config.json"
rollback() {
    systemctl disable --now "$service.timer" >/dev/null 2>&1 || true
    systemctl stop "$service.service" >/dev/null 2>&1 || true
    rm -f "$public/.mirror/enabled" "/etc/systemd/system/$service.service" "/etc/systemd/system/$service.timer"
    cp -p "$backup/publisher" "$target.restore"
    mv -f "$target.restore" "$target"
    cp -p "$backup/config.json" "$config"
    cat "$backup/nginx.conf" > "$nginx"
    docker exec "$container" nginx -t && docker exec "$container" nginx -s reload || true
    systemctl daemon-reload
    echo "Mirror installation rolled back; evidence retained at $backup"
}
trap rollback EXIT HUP INT TERM
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
install -o root -g root -m 644 "$backup/new-config.json" "$config"
# Verify every public cloud byte before configuring any redirect.
# This process holds the same lock itself, so release our descriptor first.
flock -u 9
su -s /bin/sh "$account" -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$state/.net $target mirror-prepare"
flock -n 9 || { echo 'Publication started during preparation'; exit 1; }
sha256sum -c "$backup/current-feed.sha256"
cat "$backup/nginx-candidate.conf" > "$nginx"
docker exec "$container" nginx -t
docker exec "$container" nginx -s reload
cat > "/etc/systemd/system/$service.service" <<'SERVICE'
[Unit]
Description=Dustweave verified OTA mirror availability
After=network-online.target

[Service]
Type=oneshot
User=dustweave-ota-v2
Group=dustweave-ota-v2
Environment=DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/dustweave-ota-channel-v2/.net
ExecStart=/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel mirror-health
ExecStopPost=/bin/sh -c 'if [ "$SERVICE_RESULT" != success ]; then rm -f /home/nginx/dustweave-updates-v2/.mirror/enabled; fi'
TimeoutStartSec=90
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=read-only
ProtectSystem=strict
ReadWritePaths=/var/lib/dustweave-ota-channel-v2 /home/nginx/dustweave-updates-v2/.mirror
SERVICE
cat > "/etc/systemd/system/$service.timer" <<'TIMER'
[Unit]
Description=Recheck Dustweave OTA file delivery every minute
[Timer]
OnBootSec=30s
OnUnitInactiveSec=60s
AccuracySec=5s
[Install]
WantedBy=timers.target
TIMER
systemctl daemon-reload
systemctl enable --now "$service.timer"
systemctl start "$service.service"
sha256sum -c "$backup/current-feed.sha256"
sha256sum -c "$backup/legacy.sha256"
trap - EXIT HUP INT TERM
echo "Mirror enabled; original signed feed and legacy lane unchanged. Rollback backup: $backup"
