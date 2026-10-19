#!/bin/sh
# Replace only the v2 publisher after verifying its signed-feed compatibility.
set -eu
[ "$(id -u)" = 0 ] || { echo 'Administrator installation required'; exit 1; }
payload=${1:?verified executable required}
expected=${2:?expected SHA256 required}
target=/usr/local/lib/dustweave-ota-channel-v2/Dustweave.OtaChannel
config=/etc/dustweave-ota-channel-v2/config.json
legacy=$(sha256sum /home/nginx/dustweave-updates/updates.json /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel /etc/dustweave-ota-channel/config.json)
current=$(sha256sum /home/nginx/dustweave-updates-v2/updates.json)
[ "$(sha256sum "$payload" | cut -d ' ' -f1)" = "$expected" ]
"$payload" verify "$config"
backup="$target.before-147"
[ ! -e "$backup" ]
cp -p "$target" "$backup"
trap 'cp -p "$backup" "$target.restore"; mv -f "$target.restore" "$target"' EXIT HUP INT TERM
install -o root -g root -m 755 "$payload" "$target.new"
mv -f "$target.new" "$target"
su -s /bin/sh dustweave-ota-v2 -c "DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/dustweave-ota-channel-v2/.net $target verify $config"
[ "$(sha256sum /home/nginx/dustweave-updates-v2/updates.json)" = "$current" ]
[ "$(sha256sum /home/nginx/dustweave-updates/updates.json /usr/local/lib/dustweave-ota-channel/Dustweave.OtaChannel /etc/dustweave-ota-channel/config.json)" = "$legacy" ]
trap - EXIT HUP INT TERM
echo 'V2 publisher upgraded; both feeds and legacy publisher unchanged.'
