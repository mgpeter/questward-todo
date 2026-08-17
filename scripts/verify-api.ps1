<#
.SYNOPSIS
    Exercises the Questward API end to end and asserts the gamification maths.

.DESCRIPTION
    Creates tasks of every difficulty, completes them, and checks XP totals, level
    thresholds, idempotent completion, reopen refunds and achievement unlocks.

    Destructive: it deletes every task it creates and resets nothing else, so run it
    against a development database.

.EXAMPLE
    pwsh ./scripts/verify-api.ps1 -BaseUrl http://localhost:5080
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5080"
)

$ErrorActionPreference = "Stop"
$script:Failures = 0

function Assert-Equal {
    param($Expected, $Actual, [string]$Label)

    if ($Expected -eq $Actual) {
        Write-Host "  PASS  $Label (= $Actual)" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL  $Label - expected '$Expected', got '$Actual'" -ForegroundColor Red
        $script:Failures++
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Label)

    if ($Condition) {
        Write-Host "  PASS  $Label" -ForegroundColor Green
    }
    else {
        Write-Host "  FAIL  $Label" -ForegroundColor Red
        $script:Failures++
    }
}

function Get-LevelForXp {
    <# Mirrors LevelCurve.cs: cumulative XP to reach level L is 25 * L * (L - 1). #>
    param([int]$TotalXp)

    $level = 1
    while (25 * ($level + 1) * $level -le $TotalXp) { $level++ }

    return $level
}

function Assert-BadgeUnlocked {
    <#
        A badge can only ever be earned once, so a run against a database that already
        has it will not see it in unlockedAchievements. Both outcomes are correct: what
        matters is that the badge is unlocked once the completion has happened.
    #>
    param($Result, [string]$Key, [string]$Label)

    $justEarned = $Result.unlockedAchievements.key -contains $Key
    $alreadyHeld = ((Invoke-Api GET "/api/achievements") |
        Where-Object { $_.key -eq $Key }).unlocked

    Assert-True ($justEarned -or $alreadyHeld) "$Label$(if (-not $justEarned) { ' (held from an earlier run)' })"
}

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)

    $params = @{
        Method      = $Method
        Uri         = "$BaseUrl$Path"
        ContentType = "application/json"
    }

    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 6)
    }

    return Invoke-RestMethod @params
}

Write-Host "`nQuestward API verification against $BaseUrl" -ForegroundColor Cyan

# --- Clean slate -------------------------------------------------------------
Write-Host "`n[setup] removing existing tasks"
foreach ($existing in (Invoke-Api GET "/api/tasks")) {
    Invoke-RestMethod -Method Delete -Uri "$BaseUrl/api/tasks/$($existing.id)" | Out-Null
}

$before = Invoke-Api GET "/api/character"
Write-Host "  character starts at level $($before.level) with $($before.totalXp) XP"

# --- Create ------------------------------------------------------------------
Write-Host "`n[create] one task per difficulty"
$easy = Invoke-Api POST "/api/tasks" @{ title = "Verify: easy"; difficulty = "easy" }
$medium = Invoke-Api POST "/api/tasks" @{ title = "Verify: medium"; difficulty = "medium" }
$hard = Invoke-Api POST "/api/tasks" @{ title = "Verify: hard"; difficulty = "hard" }
$epic = Invoke-Api POST "/api/tasks" @{ title = "Verify: epic"; difficulty = "epic" }

Assert-Equal 10 $easy.xpValue "Easy is worth 10 XP"
Assert-Equal 25 $medium.xpValue "Medium is worth 25 XP"
Assert-Equal 50 $hard.xpValue "Hard is worth 50 XP"
Assert-Equal 100 $epic.xpValue "Epic is worth 100 XP"
Assert-True ($easy.sortOrder -gt $epic.sortOrder) "Newest task sorts to the top"

# --- Validation --------------------------------------------------------------
Write-Host "`n[validate] empty title is rejected"
try {
    Invoke-Api POST "/api/tasks" @{ title = "" } | Out-Null
    Assert-True $false "Empty title returns 400"
}
catch {
    Assert-Equal 400 ([int]$_.Exception.Response.StatusCode) "Empty title returns 400"
}

# --- XP maths ----------------------------------------------------------------
$baseXp = $before.totalXp

Write-Host "`n[complete] medium task"
$r1 = Invoke-Api POST "/api/tasks/$($medium.id)/complete" @{ utcOffsetMinutes = 0 }
Assert-Equal 25 $r1.xpGained "Completing Medium grants 25 XP"
Assert-Equal ($baseXp + 25) $r1.character.totalXp "Total XP is base + 25"
Assert-BadgeUnlocked $r1 "first-blood" "First Blood unlocked"

Write-Host "`n[complete] the same task again is a no-op"
$r1again = Invoke-Api POST "/api/tasks/$($medium.id)/complete" @{ utcOffsetMinutes = 0 }
Assert-Equal 0 $r1again.xpGained "Re-completing grants 0 XP"
Assert-Equal ($baseXp + 25) $r1again.character.totalXp "Total XP is unchanged"

Write-Host "`n[complete] hard task crosses the level 2 threshold at 50 XP"
$r2 = Invoke-Api POST "/api/tasks/$($hard.id)/complete" @{ utcOffsetMinutes = 0 }
Assert-Equal 50 $r2.xpGained "Completing Hard grants 50 XP"
Assert-Equal ($baseXp + 75) $r2.character.totalXp "Total XP is base + 75"
Assert-True ($r2.character.level -ge 2) "Level is at least 2 past 50 XP"
Assert-BadgeUnlocked $r2 "deep-work" "Deep Work unlocked by a 50 XP task"

Write-Host "`n[complete] epic task"
$r3 = Invoke-Api POST "/api/tasks/$($epic.id)/complete" @{ utcOffsetMinutes = 0 }
Assert-Equal 100 $r3.xpGained "Completing Epic grants 100 XP"
Assert-Equal ($baseXp + 175) $r3.character.totalXp "Total XP is base + 175"
Assert-BadgeUnlocked $r3 "epic-slayer" "Epic Slayer unlocked"

Write-Host "`n[level] curve lands where the design says"
Assert-Equal (Get-LevelForXp ($baseXp + 175)) (Invoke-Api GET "/api/character").level `
    "$($baseXp + 175) XP maps to the level the curve predicts"
Assert-Equal 1 (Get-LevelForXp 49) "49 XP is still level 1"
Assert-Equal 2 (Get-LevelForXp 50) "50 XP is exactly level 2"
Assert-Equal 3 (Get-LevelForXp 150) "150 XP is exactly level 3"

# --- Reopen ------------------------------------------------------------------
Write-Host "`n[reopen] epic task refunds its XP"
$levelBeforeReopen = (Invoke-Api GET "/api/character").level
$reopened = Invoke-Api POST "/api/tasks/$($epic.id)/reopen"
Assert-Equal 100 $reopened.xpLost "Reopening refunds exactly what was awarded"
Assert-Equal ($baseXp + 75) $reopened.character.totalXp "Total XP drops back"
Assert-Equal ($reopened.character.level -lt $levelBeforeReopen) $reopened.leveledDown `
    "leveledDown agrees with the actual level change"
Assert-Equal $false $reopened.task.isCompleted "Task is open again"

Write-Host "`n[reopen] achievements are never revoked"
$achievements = Invoke-Api GET "/api/achievements"
$epicSlayer = $achievements | Where-Object { $_.key -eq "epic-slayer" }
Assert-True $epicSlayer.unlocked "Epic Slayer stays unlocked after reopening"

Write-Host "`n[reopen] reopening an open task is a no-op"
$noop = Invoke-Api POST "/api/tasks/$($epic.id)/reopen"
Assert-Equal 0 $noop.xpLost "Reopening an open task refunds nothing"

# --- Difficulty edit does not rewrite banked XP -------------------------------
Write-Host "`n[update] editing difficulty after completion leaves banked XP alone"
$xpBeforeEdit = (Invoke-Api GET "/api/character").totalXp
Invoke-Api PUT "/api/tasks/$($hard.id)" @{
    title      = "Verify: hard (edited to easy)"
    difficulty = "easy"
    priority   = "normal"
} | Out-Null
Assert-Equal $xpBeforeEdit (Invoke-Api GET "/api/character").totalXp "Total XP unchanged by the edit"
Assert-Equal 50 (Invoke-Api GET "/api/tasks/$($hard.id)").xpAwarded "Snapshotted award is unchanged"

# --- Filters and stats --------------------------------------------------------
Write-Host "`n[query] filters"
Assert-Equal 2 ((Invoke-Api GET "/api/tasks?status=open") | Measure-Object).Count "Two tasks are open"
Assert-Equal 2 ((Invoke-Api GET "/api/tasks?status=done") | Measure-Object).Count "Two tasks are done"
Assert-Equal 1 ((Invoke-Api GET "/api/tasks?search=epic") | Measure-Object).Count "Search matches one task"

$stats = Invoke-Api GET "/api/stats?utcOffsetMinutes=0"
Assert-Equal 4 $stats.totalTasks "Stats sees four tasks"
Assert-Equal 14 ($stats.last14Days | Measure-Object).Count "Trend covers 14 days"
Assert-Equal 4 ($stats.byDifficulty | Measure-Object).Count "Every difficulty is present in the breakdown"

# --- Character ----------------------------------------------------------------
Write-Host "`n[character] rename"
$renamed = Invoke-Api PUT "/api/character" @{ name = "Verifier"; avatarKey = "owl" }
Assert-Equal "Verifier" $renamed.name "Name updates"
Assert-Equal "owl" $renamed.avatarKey "Avatar updates"

# --- Not found ----------------------------------------------------------------
Write-Host "`n[404] unknown ids and routes"
try {
    Invoke-Api GET "/api/tasks/$([Guid]::NewGuid())" | Out-Null
    Assert-True $false "Unknown task returns 404"
}
catch {
    Assert-Equal 404 ([int]$_.Exception.Response.StatusCode) "Unknown task returns 404"
}

try {
    Invoke-Api GET "/api/nope" | Out-Null
    Assert-True $false "Unknown API route returns 404 rather than the SPA shell"
}
catch {
    Assert-Equal 404 ([int]$_.Exception.Response.StatusCode) "Unknown API route returns 404 rather than the SPA shell"
}

# --- Summary ------------------------------------------------------------------
Write-Host ""
if ($script:Failures -eq 0) {
    Write-Host "All API checks passed." -ForegroundColor Green
    exit 0
}

Write-Host "$($script:Failures) API check(s) failed." -ForegroundColor Red
exit 1
