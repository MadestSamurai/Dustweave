#!/bin/sh
# Administrator-only AIO mirror recovery upgrade. Signed feeds and nginx stay unchanged.
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
service=dustweave-ota-aio-mirror.service
timer=dustweave-ota-aio-mirror.timer
dropdir=/etc/systemd/system/$service.d
dropin=$dropdir/50-resumable-verification.conf
for path in "$payload" "$target" "$config" "$state" "$state/publish.lock" "$state/capable" "$state/capable/publish.lock" "$dropdir" "$dropin"; do
    test ! -L "$path" || { echo "Linked installation path rejected: $path"; exit 1; }
done
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
[ "$(sha256sum "$target" | cut -d ' ' -f1)" = "$previous" ]
[ "$(sha256sum "$public/releases.json" | cut -d ' ' -f1)" = "$feed_hash" ]
test -f /etc/systemd/system/$service && test -f /etc/systemd/system/$timer
backup=$(mktemp -d "$state/mirror-recovery-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
cp -p "$target" "$backup/publisher"
had_dropin=0
if test -f "$dropin"; then cp -p "$dropin" "$backup/service-dropin"; had_dropin=1; fi
timer_active=0
if systemctl is-active --quiet "$timer"; then timer_active=1; fi
sha256sum "$config" "$public/releases.json" /ssl/nginx.conf \
  /home/nginx/dustweave-updates/updates.json \
  /home/nginx/dustweave-updates-v2/updates.json \
  /home/nginx/dustweave-updates-v2/updates-v3.json \
  /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel \
  /usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel \
  /var/lib/dustweave-ota-aio-home/.ssh/authorized_keys > "$backup/unchanged.sha256"
for feed in releases-v2.json releases-1.6.0.json .mirror/manifests/releases-1.6.0.json; do
    if test -f "$public/$feed"; then sha256sum "$public/$feed" >> "$backup/unchanged.sha256"; fi
done
"$payload" verify "$config" > "$backup/before-status.json"
(cd "$backup" && "$payload" self-test > self-test.log)
replaced=0
rollback() {
    trap - EXIT HUP INT TERM
    if test "$replaced" = 1; then
        systemctl stop "$service" || true
        cp -p "$backup/publisher" "$target.restore"
        mv -f "$target.restore" "$target"
        if test "$had_dropin" = 1; then cp -p "$backup/service-dropin" "$dropin"; else rm -f "$dropin"; fi
        systemctl daemon-reload || true
    fi
    if test "$timer_active" = 1; then systemctl start "$timer" || true; fi
    echo "Mirror recovery upgrade rolled back; evidence: $backup"
}
trap rollback EXIT
trap 'exit 1' HUP INT TERM
systemctl stop "$timer"
systemctl stop "$service"
exec 9>"$state/publish.lock"
flock -n 9 || { echo 'A publication is active; retry after it completes'; exit 1; }
if test -d "$state/capable"; then
    exec 8>"$state/capable/publish.lock"
    flock -n 8 || { echo 'A capable publication is active; retry after it completes'; exit 1; }
fi
[ "$(sha256sum "$public/releases.json" | cut -d ' ' -f1)" = "$feed_hash" ]
[ "$(sha256sum "$target" | cut -d ' ' -f1)" = "$previous" ]
sha256sum -c "$backup/unchanged.sha256"
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
replaced=1
install -d -o root -g root -m 755 "$dropdir"
printf '[Service]\nTimeoutStartSec=5min\n' > "$dropin"
chmod 644 "$dropin"
systemctl daemon-reload
runuser -u dustweave-ota-aio -- env DOTNET_BUNDLE_EXTRACT_BASE_DIR="$state/.net" \
  SSH_ORIGINAL_COMMAND='dustweave-ota status' "$target" serve > "$backup/after-status.json"
sha256sum -c "$backup/unchanged.sha256"
flock -u 9
exec 9>&-
if test -d "$state/capable"; then flock -u 8; exec 8>&-; fi
if test "$timer_active" = 1; then systemctl start "$timer"; fi
# Network recovery runs separately. Every invocation is bounded and persists progress.
# A temporary CDN outage must leave the recovery-capable executable installed.
systemctl start --no-block "$service"
trap - EXIT HUP INT TERM
echo "AIO mirror recovery installed; signed feeds and nginx unchanged. Backup: $backup"
