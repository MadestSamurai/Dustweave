function Assert-DustweaveUpdateLane {
 param([string]$Version,[object[]]$PreviousReleases=@())
 $bundled=[Version]($Version.Split('-')[0]) -ge [Version]'1.5.0'
 foreach($release in $PreviousReleases){
  $previousBundled=[Version](([string]$release.Version).Split('-')[0]) -ge [Version]'1.5.0'
  if($previousBundled -ne $bundled){throw 'The 1.5+ all-in-one feed and the legacy 1.4 feed must remain separate. Start the new feed without historical releases.'}
 }
 return $(if($bundled){'all-in-one'}else{'legacy'})
}
