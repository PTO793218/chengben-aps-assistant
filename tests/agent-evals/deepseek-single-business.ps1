param([Parameter(Mandatory=$true)][string]$EvidencePath,[switch]$BusinessDataApproved)
$ErrorActionPreference='Stop'
if(!$BusinessDataApproved){throw 'Explicit approval of this business data transfer is required.'}
$resultPath=Join-Path $EvidencePath 'business-result.json'
if(Test-Path -LiteralPath $resultPath){throw 'A test record already exists. Do not automatically rerun a paid test.'}
$body=Get-Content -LiteralPath (Join-Path $EvidencePath 'request.json') -Raw -Encoding UTF8
$payload=$body | ConvertFrom-Json
$record=[ordered]@{
 timeUtc=[DateTime]::UtcNow.ToString('o')
 test=$payload.message
 agentRequests=1
 expectedModelHttpRequests=2
 maximumPlannedModelHttpRequests=3
 automaticTestRerun=$false
 explicitBusinessDataApproval=$true
 status='started'
 entry='http://127.0.0.1:18440/api/agent/chat/stream'
}
$record | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
try{
 $response=Invoke-WebRequest -Uri $record.entry -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 180
 $stream=[string]$response.Content
 $stream | Set-Content -LiteralPath (Join-Path $EvidencePath 'business-response.sse') -Encoding UTF8
 $events=@()
 foreach($part in ($stream -split '\r?\n\r?\n')){
  if($part -match '(?m)^event:\s*(.+)$'){
   $type=$matches[1].Trim()
   if($part -match '(?m)^data:\s*(.+)$'){
    $events+=@{event=$type;data=($matches[1] | ConvertFrom-Json)}
   }
  }
 }
 $done=($events | Where-Object event -eq 'done' | Select-Object -Last 1).data
 $start=($events | Where-Object event -eq 'start' | Select-Object -First 1).data
 $failure=($events | Where-Object event -eq 'error' | Select-Object -Last 1).data
 $text=($events | Where-Object event -eq 'delta' | ForEach-Object {$_.data.text}) -join ''
 $blocks=@($events | Where-Object event -eq 'ui_payload' | ForEach-Object {$_.data.blocks})
 $record.reply=$text
 $record.model=$start.model
 $record.requestId=$start.requestId
 $record.modelInvoked=$done.modelInvoked
 $record.toolCount=$done.toolCount
 $record.toolCalls=$done.toolCalls
 $record.uiBlockCount=$blocks.Count
 $record.elapsedMs=$done.elapsedMs
 $record.replyAdjusted=$done.replyAdjusted
 if($failure){$record.status='failed';$record.error=$failure}
 elseif($done.modelInvoked -eq $true -and $done.toolCount -ge 1 -and $blocks.Count -ge 1 -and $text){$record.status='passed'}
 else{$record.status='incomplete'}
 $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding UTF8
 $record | ConvertTo-Json -Depth 8
}catch{
 $record.status='failed'
 $record.errorType=$_.Exception.GetType().FullName
 $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding UTF8
 throw 'The single business test did not complete. Evidence saved; no automatic retry.'
}
