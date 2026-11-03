#!/bin/sh
# Disposable container fixture. systemctl/docker are mocked; real nginx is
# separately covered by test-aio-delivery.ps1. Never run on the production host.
set -eu
test -f /.dockerenv
test ! -e /etc/dustweave-ota-channel-aio/config.json
state=/var/lib/dustweave-ota-channel-aio
target=/usr/local/lib/dustweave-ota-channel-aio/Dustweave.OtaChannel
mkdir -p "$state" /etc/dustweave-ota-channel-aio /usr/local/lib/dustweave-ota-channel-aio \
 /home/nginx/dustweave-updates-aio /home/nginx/dustweave-updates-v2 /home/nginx/dustweave-updates \
 /usr/local/lib/dustweave-ota-channel /usr/local/lib/dustweave-ota-channel-v2 \
 /var/lib/dustweave-ota-aio-home/.ssh /etc/systemd/system /ssl /results
useradd --system dustweave-ota-aio
chown dustweave-ota-aio:dustweave-ota-aio "$state"
cp /input/config.json /etc/dustweave-ota-channel-aio/config.json
cp /input/releases.json /home/nginx/dustweave-updates-aio/releases.json
cp /input/before.conf /ssl/nginx.conf
for path in /home/nginx/dustweave-updates/updates.json /home/nginx/dustweave-updates-v2/updates.json \
 /home/nginx/dustweave-updates-v2/updates-v3.json \
 /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel \
 /usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel \
 /var/lib/dustweave-ota-aio-home/.ssh/authorized_keys; do printf 'unchanged fixture\n' > "$path"; done
cat > /usr/local/bin/systemctl <<'STUB'
#!/bin/sh
printf '%s\n' "$*" >> /results/systemctl.log
exit 0
STUB
cat > /usr/local/bin/docker <<'STUB'
#!/bin/sh
printf '%s\n' "$*" >> /results/docker.log
if test -f /tmp/fail-nginx; then rm /tmp/fail-nginx; exit 1; fi
exit 0
STUB
chmod 755 /usr/local/bin/systemctl /usr/local/bin/docker
cp /old/Dustweave.OtaChannel "$target"
chmod 755 "$target"
new=$(sha256sum /payload/Dustweave.OtaChannel | cut -d ' ' -f1)
old=$(sha256sum "$target" | cut -d ' ' -f1)
feed=$(sha256sum /input/releases.json | cut -d ' ' -f1)
web=$(sha256sum /ssl/nginx.conf | cut -d ' ' -f1)
sh -n /scripts/upgrade-update-aio-delivery.sh
sh /scripts/upgrade-update-aio-delivery.sh /payload/Dustweave.OtaChannel "$new" "$old" "$feed" > /results/install-success.log 2>&1
test "$(sha256sum "$target" | cut -d ' ' -f1)" = "$new"
test "$(sha256sum /ssl/nginx.conf | cut -d ' ' -f1)" != "$web"
printf 'success: publisher and CDN-only route installed\n'
cp /old/Dustweave.OtaChannel "$target"
chmod 755 "$target"
cat /input/before.conf > /ssl/nginx.conf
touch /tmp/fail-nginx
if sh /scripts/upgrade-update-aio-delivery.sh /payload/Dustweave.OtaChannel "$new" "$old" "$feed" > /results/install-rollback.log 2>&1; then exit 1; fi
test "$(sha256sum "$target" | cut -d ' ' -f1)" = "$old"
test "$(sha256sum /ssl/nginx.conf | cut -d ' ' -f1)" = "$web"
test "$(sha256sum /home/nginx/dustweave-updates-aio/releases.json | cut -d ' ' -f1)" = "$feed"
printf 'rollback: rejected nginx restores executable and original mounted web configuration\n'
if sh /scripts/upgrade-update-aio-delivery.sh /payload/Dustweave.OtaChannel bad "$old" "$feed" > /results/install-bad-hash.log 2>&1; then exit 1; fi
test "$(sha256sum "$target" | cut -d ' ' -f1)" = "$old"
printf 'reject: bad hash changes no installation files\n'
exec 8>"$state/publish.lock"
flock -n 8
if sh /scripts/upgrade-update-aio-delivery.sh /payload/Dustweave.OtaChannel "$new" "$old" "$feed" > /results/install-busy.log 2>&1; then exit 1; fi
test "$(sha256sum "$target" | cut -d ' ' -f1)" = "$old"
test "$(sha256sum /ssl/nginx.conf | cut -d ' ' -f1)" = "$web"
flock -u 8
exec 8>&-
printf 'busy: publication lock respected; current web configuration untouched\n'
printf '{"status":"passed","cases":4,"realServerTouched":false}\n' > /results/results.json
