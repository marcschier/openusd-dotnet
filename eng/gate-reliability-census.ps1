param(
  [int]$Lookback = 50,
  [string]$Repo = "",
  [string]$Since = "",
  [switch]$FetchLogs,
  [int]$MaxLogJobs = 20
)
$ErrorActionPreference = 'Stop'
$env:Platform = ''
$failConclusions = @('failure', 'timed_out', 'startup_failure', 'action_required')
if (-not $Repo) { $Repo = gh repo view --json nameWithOwner -q .nameWithOwner }
function Invoke-GhJson([string]$Path) {
  $last = $null
  for ($try = 1; $try -le 3; $try++) {
    $json = & gh api $Path 2>&1
    if ($LASTEXITCODE -eq 0) { return ($json | ConvertFrom-Json) }
    $last = ($json | Out-String).Trim()
    Start-Sleep -Seconds (2 * $try)
  }
  throw "gh api failed after retries for ${Path}: $last"
}
function Resolve-Since($Value) {
  if (-not $Value) { return $null }
  $date = [datetime]::MinValue
  if ([datetime]::TryParse($Value, [ref]$date)) {
    return [pscustomobject]@{ value = $Value; sha = ''; date = $date.ToUniversalTime() }
  }
  $commit = Invoke-GhJson "/repos/$Repo/commits/$Value"
  [pscustomobject]@{
    value = $Value
    sha = $commit.sha
    date = ([datetime]$commit.commit.committer.date).ToUniversalTime()
  }
}
function Get-Runs($WorkflowId) {
  $out = @(); $page = 1
  while ($out.Count -lt $Lookback) {
    $path = "/repos/$Repo/actions/workflows/$WorkflowId/runs?per_page=100&page=$page"
    $items = @((Invoke-GhJson $path).workflow_runs)
    if ($items.Count -eq 0) { break }
    $out += $items
    if ($items.Count -lt 100) { break }
    $page++
  }
  $out | Sort-Object created_at -Descending | Select-Object -First $Lookback
}
function Get-Jobs($RunId, $Attempt) {
  (Invoke-GhJson "/repos/$Repo/actions/runs/$RunId/attempts/$Attempt/jobs?per_page=100").jobs
}
function Get-Symptom($JobId) {
  try {
    $log = gh api "/repos/$Repo/actions/jobs/$JobId/logs" 2>$null | Out-String
    $words = 'error:|failed|failure|assert|exception|timed out|timeout|panic|segfault|core dumped'
    $words += '|expected|actual|no such|unable|not found|working set|soak'
    $hits = $log -split "`n" | Where-Object { $_ -match "(?i)($words)" } | Select-Object -Last 8
    (($hits -join ' | ') -replace '\x1b\[[0-9;]*m', '').Trim()
  } catch { "log unavailable: $($_.Exception.Message)" }
}
function New-Summary($JobRows) {
  foreach ($g in ($JobRows | Group-Object workflow, job)) {
    $items = @($g.Group)
    $fails = @($items | Where-Object conclusion -in $failConclusions)
    $passes = @($items | Where-Object conclusion -eq 'success')
    $skips = @($items | Where-Object conclusion -eq 'skipped')
    $reruns = @($items | Group-Object runId | Where-Object {
        ($_.Group.conclusion -contains 'failure') -and ($_.Group.conclusion -contains 'success') })
    $sameCommit = @($items | Group-Object sha | Where-Object {
        ($_.Group.conclusion -contains 'failure') -and ($_.Group.conclusion -contains 'success') })
    $recent = @($items | Sort-Object created -Descending | Select-Object -First 5)
    if ($fails.Count -eq 0) { $class = 'clean' }
    elseif ($passes.Count -eq 0 -and $items.Count -ge 3) { $class = 'known-red/permanent' }
    elseif ($reruns.Count -gt 0) { $class = 'intermittent-rerun' }
    elseif ($sameCommit.Count -gt 0) { $class = 'intermittent-same-commit' }
    elseif (@($recent | Where-Object conclusion -ne 'success').Count -eq 0) { $class = 'deterministic-fixed?' }
    else { $class = 'intermittent' }
    [pscustomobject]@{
      class = $class; workflow = $items[0].workflow; job = $items[0].job; attempts = $items.Count
      pass = $passes.Count; fail = $fails.Count; skip = $skips.Count
      rate = if ($items.Count) { [math]::Round(100 * $fails.Count / $items.Count, 1) } else { 0 }
      latestFail = ($fails | Sort-Object created -Descending | Select-Object -First 1).created
      rerunFlakes = $reruns.Count; sameCommit = $sameCommit.Count
    }
  }
}
function Write-Section($Name, $RunRows, $AttemptRows, $JobRows) {
  ""; "=== $Name ==="
  $cancelled = @($RunRows | Where-Object conclusion -eq 'cancelled')
  $completed = @($RunRows | Where-Object { $_.status -eq 'completed' -and $_.conclusion -ne 'cancelled' })
  "Runs: $($RunRows.Count) total; $($completed.Count) completed counted; $($cancelled.Count) cancelled excluded"
  "Attempts counted: $($AttemptRows.Count)"
  "Workflow run windows:"
  @(
  foreach ($g in ($RunRows | Group-Object workflow)) {
    $runs = @($g.Group)
    $done = @($runs | Where-Object { $_.status -eq 'completed' -and $_.conclusion -ne 'cancelled' })
    $cx = @($runs | Where-Object conclusion -eq 'cancelled')
    $wfAttempts = @($AttemptRows | Where-Object workflow -eq $g.Name)
    $failed = @($wfAttempts | Where-Object conclusion -in $failConclusions |
      Select-Object -ExpandProperty runId -Unique)
    [pscustomobject]@{ workflow = $g.Name; runs = $runs.Count; completed = $done.Count;
      cancelled = $cx.Count; failedRuns = $failed.Count;
      rate = if ($done.Count) { [math]::Round(100 * $failed.Count / $done.Count, 1) } else { 0 } }
  }) | Sort-Object @{Expression='rate';Descending=$true}, workflow | Format-Table -AutoSize
  "Workflow attempt failure rates:"
  @(
  foreach ($g in ($AttemptRows | Group-Object workflow)) {
    $items = @($g.Group); $fails = @($items | Where-Object conclusion -in $failConclusions)
    [pscustomobject]@{ workflow = $g.Name; attempts = $items.Count; failures = $fails.Count;
      rate = if ($items.Count) { [math]::Round(100 * $fails.Count / $items.Count, 1) } else { 0 } }
  }) | Sort-Object @{Expression='rate';Descending=$true}, workflow | Format-Table -AutoSize
  $summary = @(New-Summary $JobRows)
  "Intermittent job candidates (ranked):"
  $summary | Where-Object {$_.class -like 'intermittent*'} |
    Sort-Object @{Expression='rate';Descending=$true}, @{Expression='fail';Descending=$true} |
    Format-Table -AutoSize
  "Known-red/permanent failing jobs (excluded from intermittent rates):"
  $summary | Where-Object class -eq 'known-red/permanent' |
    Sort-Object workflow, job | Format-Table -AutoSize
  "Deterministic-fixed candidates:"
  $summary | Where-Object class -eq 'deterministic-fixed?' |
    Sort-Object latestFail -Descending | Format-Table -AutoSize
  "Skipped jobs seen:"
  $summary | Where-Object {$_.skip -gt 0} |
    Sort-Object @{Expression='skip';Descending=$true} | Format-Table -AutoSize
}
$sinceInfo = Resolve-Since $Since
$workflows = (Invoke-GhJson "/repos/$Repo/actions/workflows?per_page=100").workflows | Sort-Object path
$runRows = @(); $attempts = @(); $rows = @(); $rerunCount = 0
foreach ($wf in $workflows) {
  foreach ($run in @(Get-Runs $wf.id)) {
    $runRows += [pscustomobject]@{ workflow = $wf.path; runId = $run.id; run = $run.run_number;
      status = $run.status; conclusion = $run.conclusion; sha = $run.head_sha;
      created = [datetime]$run.created_at; url = $run.html_url }
    if ($run.status -ne 'completed' -or $run.conclusion -eq 'cancelled') { continue }
    $maxAttempt = [int]$run.run_attempt
    if ($maxAttempt -gt 1) { $rerunCount++ }
    for ($a = 1; $a -le $maxAttempt; $a++) {
      $conclusion = $run.conclusion
      if ($a -lt $maxAttempt) {
        $conclusion = (Invoke-GhJson "/repos/$Repo/actions/runs/$($run.id)/attempts/$a").conclusion
      }
      if ($conclusion -eq 'cancelled') { continue }
      $attempts += [pscustomobject]@{ workflow = $wf.path; runId = $run.id; attempt = $a;
        conclusion = $conclusion; sha = $run.head_sha; created = [datetime]$run.created_at }
      foreach ($job in @(Get-Jobs $run.id $a)) {
        if ($job.conclusion -eq 'cancelled') { continue }
        $rows += [pscustomobject]@{ workflow = $wf.path; job = $job.name;
          conclusion = $job.conclusion; run = $run.run_number; runId = $run.id;
          attempt = $a; maxAttempt = $maxAttempt; jobId = $job.id;
          sha = $run.head_sha; created = [datetime]$run.created_at; url = $run.html_url }
      }
    }
  }
}
"Repository: $Repo"
"Lookback: last $Lookback runs per workflow; cancelled runs/jobs excluded from rates"
"Re-run runs inspected: $rerunCount"
Write-Section 'Pooled window' $runRows $attempts $rows
if ($sinceInfo) {
  $sinceRuns = @($runRows | Where-Object {
      $_.created.ToUniversalTime() -gt $sinceInfo.date -and $_.sha -ne $sinceInfo.sha })
  $ids = @($sinceRuns | Select-Object -ExpandProperty runId)
  ""; "Since boundary: $($sinceInfo.value) at $($sinceInfo.date.ToString('u'))"
  Write-Section "After $($sinceInfo.value)" $sinceRuns `
    @($attempts | Where-Object { $ids -contains $_.runId }) `
    @($rows | Where-Object { $ids -contains $_.runId })
}
if ($FetchLogs) {
  ""; "Failure symptoms (sampled newest failed attempts):"
  $toLog = $rows | Where-Object conclusion -in $failConclusions |
    Sort-Object created -Descending | Select-Object -First $MaxLogJobs
  foreach ($f in $toLog) {
    [pscustomobject]@{ workflow = $f.workflow; job = $f.job; run = $f.run;
      attempt = $f.attempt; sha = $f.sha.Substring(0, 7); symptom = Get-Symptom $f.jobId } |
      Format-List
  }
}
