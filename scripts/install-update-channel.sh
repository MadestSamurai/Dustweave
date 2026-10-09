#!/bin/sh
# Run once as an administrator with a verified channel binary and public transport key.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?payload directory required}
[ -f "$payload/Dustweave.OtaChannel" ] && [ -f "$payload/config.json" ] && [ -f "$payload/transport.pub" ]
account=dustweave-ota
installroot=/usr/local/lib/dustweave-ota-channel
configroot=/etc/dustweave-ota-channel
state=/var/lib/dustweave-ota-channel
public=/home/nginx/dustweave-updates
[ -f "$public/updates.json" ]
# Existing website configuration and the simulator release channel remain untouched.
if ! id "$account" >/dev/null 2>&1; then useradd --system --create-home --home-dir /var/lib/dustweave-ota-home --shell /bin/sh "$account"; fi
install -d -o root -g root -m 755 "$installroot" "$configroot"
install -o root -g root -m 755 "$payload/Dustweave.OtaChannel" "$installroot/Dustweave.OtaChannel"
install -o root -g root -m 644 "$payload/config.json" "$configroot/config.json"
install -d -o "$account" -g "$account" -m 755 "$state" "$state/operations" "$state/claims"
[ "$(stat -c %d "$state")" = "$(stat -c %d "$public")" ] || { echo 'State and public storage must share a filesystem for atomic directory rename'; exit 1; }
# Only this application's static update directory is writable by the constrained publisher.
chown "$account:$account" "$public"
chmod 755 "$public"
chown root:root /var/lib/dustweave-ota-home
chmod 755 /var/lib/dustweave-ota-home
install -d -o root -g root -m 755 /var/lib/dustweave-ota-home/.ssh
key=$(cat "$payload/transport.pub")
case "$key" in 'ssh-ed25519 '*) ;; *) echo 'Invalid transport public key'; exit 1;; esac
printf 'restrict,command="/usr/bin/env DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/dustweave-ota-channel/.net /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel serve" %s\n' "$key" > /var/lib/dustweave-ota-home/.ssh/authorized_keys
chown root:root /var/lib/dustweave-ota-home/.ssh/authorized_keys
chmod 644 /var/lib/dustweave-ota-home/.ssh/authorized_keys
# A plain SSH session has no permitted command; no shell, forwarding or arbitrary file upload is available.
su -s /bin/sh "$account" -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$state/.net $installroot/Dustweave.OtaChannel verify $configroot/config.json"
printf 'Dustweave OTA channel installed\n'
