#!/bin/sh
# First installation only. Never rewrites the legacy channel, feed or web configuration.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?verified payload directory required}
account=dustweave-ota-v2
installroot=/usr/local/lib/dustweave-ota-channel-v2
configroot=/etc/dustweave-ota-channel-v2
state=/var/lib/dustweave-ota-channel-v2
public=/home/nginx/dustweave-updates-v2
homedir=/var/lib/dustweave-ota-v2-home
for file in Dustweave.OtaChannel config.json transport.pub updates.json; do test -f "$payload/$file"; done
[ ! -e "$public" ] && [ ! -e "$state" ] && [ ! -e "$configroot" ]
! id "$account" >/dev/null 2>&1 || { echo 'Account already exists; inspect previous installation'; exit 1; }
legacy_hash=$(sha256sum /home/nginx/dustweave-updates/updates.json)
useradd --system --create-home --home-dir "$homedir" --shell /bin/sh "$account"
install -d -o root -g root -m 755 "$installroot" "$configroot"
install -o root -g root -m 755 "$payload/Dustweave.OtaChannel" "$installroot/Dustweave.OtaChannel"
install -o root -g root -m 644 "$payload/config.json" "$configroot/config.json"
install -d -o "$account" -g "$account" -m 755 "$state" "$state/operations" "$state/claims" "$public" "$public/v1.3.0"
[ "$(stat -c %d "$state")" = "$(stat -c %d "$public")" ]
install -o "$account" -g "$account" -m 644 "$payload/updates.json" "$public/updates.json"
chown root:root "$homedir"
chmod 755 "$homedir"
install -d -o root -g root -m 755 "$homedir/.ssh"
key=$(cat "$payload/transport.pub")
case "$key" in 'ssh-ed25519 '*) ;; *) echo 'Invalid public key'; exit 1;; esac
printf 'restrict,command="/usr/bin/env DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/dustweave-ota-channel-v2/.net /usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel serve" %s\n' "$key" > "$homedir/.ssh/authorized_keys"
chown root:root "$homedir/.ssh/authorized_keys"
chmod 644 "$homedir/.ssh/authorized_keys"
su -s /bin/sh "$account" -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$state/.net $installroot/Dustweave.OtaChannel verify $configroot/config.json"
su -s /bin/sh "$account" -c '! test -w /home/nginx/dustweave-updates && ! test -w /var/lib/dustweave-ota-channel'
su -s /bin/sh dustweave-ota -c '! test -w /home/nginx/dustweave-updates-v2 && ! test -w /var/lib/dustweave-ota-channel-v2'
[ "$(sha256sum /home/nginx/dustweave-updates/updates.json)" = "$legacy_hash" ]
printf 'Separate v2 channel initialized; legacy feed unchanged. Configure the independent web alias next.\n'
