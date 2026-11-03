param([Parameter(Mandatory)][string]$PublisherDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$publish=[IO.Path]::GetFullPath($PublisherDirectory)
$out=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
function Save($path,$text){[IO.File]::WriteAllText((Join-Path $out $path),$text.Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))}
function DockerRun([string[]]$Arguments){& docker @Arguments;if($LASTEXITCODE){throw "Docker failed ($LASTEXITCODE)"}}
$name='dw-aio-delivery-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$file='Dustweave-1.5.15-Lite-from-1.5.14-win-x64.delta.zip'
$missing='Dustweave-1.5.15-Portable-from-1.5.14-win-x64.delta.zip'
New-Item -ItemType Directory -Force -Path (Join-Path $out 'public/.mirror/routes/v1.5.15'),(Join-Path $out 'public/v1.5.15') | Out-Null
Save "public/.mirror/routes/v1.5.15/$file" ('a'*64)
Save "public/v1.5.15/$file" 'ORIGIN-BODY-MUST-NEVER-LEAK'
Save "public/v1.5.15/$missing" 'UNVERIFIED-ORIGIN-BODY-MUST-NEVER-LEAK'
Save 'public/releases.json' '{"fixture":"unchanged index"}'
Save 'config.json' '{"State":"/unused","Public":"/home/nginx/dustweave-updates-aio","Keys":{},"Layout":3,"MirrorBaseUrl":"https://test.cdn.7caiyun.com/dustweave/ota/all-in-one"}'
Save 'before.conf' @'
events {}
http {
 server {
  listen 80;
  location = /unrelated { return 200 'other-page'; }
  # BEGIN DUSTWEAVE AIO
  location ^~ /updates/dustweave/all-in-one/ {
   alias /usr/share/nginx/dustweave-updates-aio/;
  }
  # END DUSTWEAVE AIO
  # BEGIN DUSTWEAVE OTA CDN
  location /updates/dustweave/v2/ { return 200 'legacy'; }
  # END DUSTWEAVE OTA CDN
 }
}
'@
DockerRun @('run','--rm','--mount',"type=bind,source=$publish,target=/publisher,readonly",'--mount',"type=bind,source=$out,target=/work",'mcr.microsoft.com/dotnet/runtime-deps:8.0-bookworm-slim','/publisher/Dustweave.OtaChannel','bundle-nginx','/work/before.conf','/work/nginx.conf','/work/config.json')
$checks=[Collections.Generic.List[string]]::new()
try {
 DockerRun @('run','--detach','--name',$name,'-p','127.0.0.1::80','--mount',"type=bind,source=$out/nginx.conf,target=/etc/nginx/nginx.conf,readonly",'--mount',"type=bind,source=$out/public,target=/usr/share/nginx/dustweave-updates-aio,readonly",'nginx:alpine')
 DockerRun @('exec',$name,'nginx','-t')
 $mapping=(& docker port $name '80/tcp').Trim()
 $base='http://'+$mapping
 $handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false
 $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromSeconds(5)
 function Check($label,$path,$status,$location=$null,$method='GET',$range=$null) {
  $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($method),$base+$path)
  if($range){[void]$request.Headers.TryAddWithoutValidation('Range',$range)}
  $response=$client.SendAsync($request).GetAwaiter().GetResult()
  try {
   $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
   if([int]$response.StatusCode -ne $status){throw "$label returned $($response.StatusCode)"}
   if($location -and $response.Headers.Location.AbsoluteUri -ne $location){throw "$label redirect differs"}
   if($body.Contains('ORIGIN-BODY')){throw "$label leaked local payload"}
   if($status -eq 503 -and ($body.Length -gt 1024 -or !$response.Headers.Contains('Retry-After'))){throw "$label must return small retriable error"}
   $checks.Add($label)
  }finally{$response.Dispose();$request.Dispose()}
 }
 $path="/updates/dustweave/all-in-one/v1.5.15/$file"
 $dest="https://test.cdn.7caiyun.com/dustweave/ota/all-in-one/v1.5.15/$file"
 Check 'verified-route-without-health-switch' $path 302 $dest
 Check 'legacy-client-cannot-force-origin' ($path+'?origin=1') 302 $dest
 Check 'range-request-stays-redirected' ($path+'?origin=1') 302 $dest 'GET' 'bytes=1-'
 Check 'head-stays-redirected' $path 302 $dest 'HEAD'
 Save 'public/.mirror/enabled' 'test'
 Check 'healthy-state-same-route' $path 302 $dest
 Check 'unverified-local-file-not-served' "/updates/dustweave/all-in-one/v1.5.15/$missing" 503
 Check 'origin-query-cannot-expose-unverified-file' "/updates/dustweave/all-in-one/v1.5.15/${missing}?origin=1" 503
 Check 'full-package-still-not-hosted' '/updates/dustweave/all-in-one/v1.5.15/Dustweave-1.5.15-Lite-win-x64.zip' 404
 Check 'internal-routes-hidden' "/updates/dustweave/all-in-one/.mirror/routes/v1.5.15/$file" 404
 Check 'signed-index-still-served' '/updates/dustweave/all-in-one/releases.json' 200
 Check 'unrelated-web-route-unchanged' '/unrelated' 200
 Check 'old-lane-unchanged' '/updates/dustweave/v2/updates.json' 200
 Save 'results.json' (@{status='passed';checks=$checks.ToArray();realServerTouched=$false}|ConvertTo-Json -Depth 5)
 Write-Output "$($checks.Count) actual nginx delivery checks passed: $out"
} finally {
 if($client){$client.Dispose()}
 & docker rm -f $name | Out-Null
}
