# Cache misses are safe: the normal compiler validates identities/hashes and rebuilds them.
function Copy-DustweavePreparedCache {
 param([Parameter(Mandatory)][string]$Source,[Parameter(Mandatory)][string]$Destination,[ValidateRange(1,4)][int]$Generations=2)
 if(!(Test-Path -LiteralPath $Source)){return}
 $sourceRoot=[IO.Path]::GetFullPath($Source);$destinationRoot=[IO.Path]::GetFullPath($Destination)
 if($sourceRoot -eq $destinationRoot -or $destinationRoot.StartsWith($sourceRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cache destination must be separate.'}
 # Do not follow directory links or copy arbitrary files left in compilation workspaces.
 $files=[Collections.Generic.List[IO.FileInfo]]::new();$pending=[Collections.Generic.Stack[IO.DirectoryInfo]]::new()
 $pending.Push([IO.DirectoryInfo]::new($sourceRoot))
 while($pending.Count){
  $dir=$pending.Pop();if($dir.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked cache directory.'}
  foreach($item in $dir.EnumerateFileSystemInfos()){
   if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked cache entry.'}
   if($item -is [IO.DirectoryInfo]){if($item.Name -notlike 'build-*'){$pending.Push($item)}}elseif($item.Name -in @('module.json','observer.json')){$files.Add($item)}
  }
 }
 $entries=foreach($file in $files){
  $relative=[IO.Path]::GetRelativePath($sourceRoot,$file.FullName).Replace('\','/')
  if($relative -match '^modules/([a-z0-9-]+)/[a-f0-9]{64}/[a-f0-9]{64}/module.json$'){$group='module/'+$Matches[1];$names=@('module.dll','module.json')}
  elseif($relative -match '^[a-f0-9]{64}/[a-f0-9]{64}/observer.json$'){$group='observer';$names=@('observer.dll','observer.json','modules.json')}
  else{continue}
  if(!(Test-Path -LiteralPath (Join-Path $file.DirectoryName $names[0]))){continue}
  [pscustomobject]@{group=$group;file=$file;names=$names}
 }
 foreach($group in $entries|Group-Object group){foreach($entry in $group.Group|Sort-Object {$_.file.LastWriteTimeUtc} -Descending|Select-Object -First $Generations){
  foreach($name in $entry.names){
   $from=Join-Path $entry.file.DirectoryName $name;if(!(Test-Path -LiteralPath $from)){continue}
   $target=Join-Path $destinationRoot ([IO.Path]::GetRelativePath($sourceRoot,$from))
   for($parent=[IO.Path]::GetDirectoryName($target);$parent;$parent=[IO.Path]::GetDirectoryName($parent)){
    if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked cache destination.'}
   }
   if((Test-Path -LiteralPath $target) -and ((Get-Item -LiteralPath $target -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked cache destination file.'}
   [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
   Copy-Item -LiteralPath $from -Destination $target -Force
  }
 }}
}
