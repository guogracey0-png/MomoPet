param([string]$Exe)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Web.Extensions
$assembly=[Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$controllerType=$assembly.GetType('MomoPetApp.PetController')
$ruleType=$assembly.GetType('MomoPetApp.ComplianceRuleData')
$controller=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($controllerType)
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
function Call($name,$values){
    $argsList=New-Object 'System.Collections.Generic.List[object]'
    foreach($value in $values){$argsList.Add($value.PSObject.BaseObject)}
    $controllerType.GetMethod($name,$flags).Invoke($controller,$argsList.ToArray())
}
function Assert($ok,$message){if(!$ok){throw $message};Write-Output "PASS: $message"}
function U($value){[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))}

$rule=[Activator]::CreateInstance($ruleType)
$rule.Id='BD-W-07'
$rule.Category=U '6aOO6Zmp5o+Q56S66K+N'
$rule.RequiredWarning=U '5Z+66YeR55qE6L+H5b6A5Lia57up5bm25LiN6aKE56S65YW25pyq5p2l6KGo546w77yM5Z+66YeR566h55CG5Lq6566h55CG55qE5YW25LuW5Z+66YeR55qE5Lia57up5bm25LiN5p6E5oiQ5Z+66YeR5Lia57up6KGo546w55qE5L+d6K+B44CC'

$full=U '5Lqn5ZOB5YeA5YC86L+R5pyf5pyJ5omA5aKe6ZW/44CC6aOO6Zmp5o+Q56S677ya5Z+66YeR55qE6L+H5b6A5Lia57up5bm25LiN6aKE56S65YW25pyq5p2l6KGo546w77yM5Z+66YeR566h55CG5Lq6566h55CG55qE5YW25LuW5Z+66YeR55qE5Lia57up5bm25LiN5p6E5oiQ5Z+66YeR5Lia57up6KGo546w55qE5L+d6K+B44CC'
$method=$controllerType.GetMethod('HasRequiredDisclosure',$flags)
$invokeArgs=@($full,$rule,'')
$covered=$method.Invoke($controller,$invokeArgs)
Assert $covered 'Matching specialist disclosure suppresses the warning-type hit'

$generic=U '5Lqn5ZOB5YeA5YC86L+R5pyf5pyJ5omA5aKe6ZW/44CC5oqV6LWE5pyJ6aOO6Zmp77yM6YCJ5oup6ZyA6LCo5oWO44CC'
$invokeArgs=@($generic,$rule,'')
$covered=$method.Invoke($controller,$invokeArgs)
Assert (-not $covered) 'Generic disclaimer does not suppress a specialist warning'

$locator=$controllerType.GetMethod('TryLocateCompliancePhrase',$flags)
$invokeArgs=@((U '5pyq5p2lIOaUtuebiueOh++8jOWPr+iDveWPmOWMlg=='),(U '5pyq5p2l5pS255uK546H5Y+v6IO95Y+Y5YyW'),-1,0)
$located=$locator.Invoke($controller,$invokeArgs)
Assert ($located -and $invokeArgs[2] -eq 0 -and $invokeArgs[3] -gt 0) 'Highlight locator tolerates spaces and punctuation'

$negated=Call 'IsNegatedOrExplanatoryUse' @((U '5pys5Lqn5ZOB5LiN5L+d5pys77yM5Y+v6IO95Y+R55Sf5LqP5o2f'),4,(U '5L+d5pys'))
Assert $negated 'Negated risk disclosure is not treated as a prohibited promise'

$guaranteeText=U '5Z+66YeR566h55CG5Lq6566h55CG55qE5YW25LuW5Z+66YeR55qE5Lia57up5bm25LiN5p6E5oiQ5Z+66YeR5Lia57up6KGo546w55qE5L+d6K+B'
$guarantee=U '5L+d6K+B'
Assert (Call 'IsNegatedOrExplanatoryUse' @($guaranteeText,$guaranteeText.IndexOf($guarantee),$guarantee)) 'Negative guarantee wording inside a disclosure is excluded'

$promiseText=U '5LiN5Luj6KGo5pS255uK5L+d6Zqc5oiW5YW25LuW5Lu75L2V5b2i5byP55qE5pS255uK5om/6K+6'
$promise=U '5om/6K+6'
Assert (Call 'IsNegatedOrExplanatoryUse' @($promiseText,$promiseText.IndexOf($promise),$promise)) 'Negative promise wording inside a disclosure is excluded'

$findingType=$assembly.GetType('MomoPetApp.ComplianceFindingData')
$finding=[Activator]::CreateInstance($findingType)
$finding.Id='hit-1';$finding.RuleId='BD-S-01';$finding.Text='stable';$finding.Start=0;$finding.Length=6
$finding.Category='sensitive';$finding.Severity='high';$finding.Reason='local keyword';$finding.Suggestion='review';$finding.Disposition='pending'
$findingsField=$controllerType.GetField('currentComplianceFindings',$flags)
$coveredField=$controllerType.GetField('currentComplianceCoveredFindings',$flags)
$findings=[Activator]::CreateInstance($findingsField.FieldType);$findings.Add($finding)
$coveredItems=[Activator]::CreateInstance($coveredField.FieldType)
$findingsField.SetValue($controller,$findings);$coveredField.SetValue($controller,$coveredItems)
$serializer=New-Object System.Web.Script.Serialization.JavaScriptSerializer
$controllerType.GetField('json',$flags).SetValue($controller,$serializer)
$structured='{"conclusion":"pass","summary":"context checked","items":[{"hit_id":"hit-1","excerpt":"stable","rule_id":"BD-S-01","decision":"dismiss","category":"sensitive","severity":"high","reason":"objective context","suggestion":"none","confidence":0.92}]}'
$structuredMethod=$controllerType.GetMethod('ApplyStructuredComplianceReview',$flags)
$structuredArgs=@('stable statement',$structured,'')
$structuredOk=$structuredMethod.Invoke($controller,$structuredArgs)
Assert ($structuredOk -and $findings.Count -eq 0 -and $coveredItems.Count -eq 1) 'Structured model review can remove a high-confidence contextual false positive'

Write-Output 'All compliance regression tests passed; no window was shown.'
