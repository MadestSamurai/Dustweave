$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'publish-update-signed.ps1'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Publisher script syntax is invalid'}
$assignment=$ast.EndBlock.Statements|Where-Object {$_ -is [Management.Automation.Language.AssignmentStatementAst] -and $_.Right.Extent.Text -eq "InvokeChannel 'dustweave-ota status'"}|Select-Object -First 1
$guard=$ast.EndBlock.Statements|Where-Object {$_.Extent.Text.StartsWith('if($Current -and $Capable)')}|Select-Object -First 1
$channel=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'InvokeChannel'},$false)
if(!$assignment -or !$guard -or !$channel){throw 'Cannot locate production channel selection boundary'}
$routing=@($channel.Body.EndBlock.Statements|Select-Object -First 2|ForEach-Object {$_.Extent.Text}) -join "`n"
$name=$assignment.Left.VariablePath.UserPath
# Execute the production typed parameters, status assignment and command routing.
# Only transport is replaced; no server, signing key or game is involved.
$source=$ast.ParamBlock.Extent.Text+"`n"+$guard.Extent.Text+@'

function InvokeChannel([string]$command){[pscustomobject]@{version='1.6.9';layout=3;feedSha256=('a'*64)}}
'@+"`n"+$assignment.Extent.Text+"`n"+'$command="dustweave-ota submit"'+"`n"+$routing+"`n"+@"
[pscustomobject]@{Current=[bool]`$Current;Capable=[bool]`$Capable;Version=(Get-Variable '$name' -ValueOnly).version;Command=`$command}
"@
$probe=[scriptblock]::Create($source)
foreach($mode in @('default','capable','current')){
 $arguments=@{Action='submit';Config='synthetic'}
 if($mode -eq 'capable'){$arguments.Capable=$true};if($mode -eq 'current'){$arguments.Current=$true}
 $result=& $probe @arguments
 $expected=if($mode -eq 'default'){'dustweave-ota submit'}else{"dustweave-ota $mode submit"}
 if($result.Version -ne '1.6.9' -or $result.Command -ne $expected -or $result.Current -ne ($mode -eq 'current') -or $result.Capable -ne ($mode -eq 'capable')){throw "Status query changed channel selection: $mode"}
}
$rejected=$false
try{& $probe -Action submit -Config synthetic -Current -Capable|Out-Null}catch{$rejected=$_.Exception.Message -eq 'Choose exactly one update channel.'}
if(!$rejected){throw 'Conflicting channels must be rejected'}
Write-Output '4 publisher channel selection checks passed'
