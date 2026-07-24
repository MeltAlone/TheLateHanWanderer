param(
    [Parameter(Mandatory = $false)]
    [string]$ScenarioDirectory = "data/scenarios/189-luoyang-crisis"
)

$ErrorActionPreference = "Stop"
$scenarioPath = (Resolve-Path -LiteralPath $ScenarioDirectory).Path
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$diagnostics = New-Object System.Collections.Generic.List[string]

function Add-Diagnostic {
    param([string]$Code, [string]$Path, [string]$Message)
    $diagnostics.Add("$Code $Path $Message")
}

function Read-JsonFile {
    param([string]$Path)
    try {
        return Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json
    }
    catch {
        Add-Diagnostic "SCN-JSON-001" $Path $_.Exception.Message
        return $null
    }
}

function Test-Reference {
    param(
        [string]$Value,
        [System.Collections.Generic.HashSet[string]]$Allowed,
        [string]$Path,
        [bool]$AllowNull = $false
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        if (-not $AllowNull) {
            Add-Diagnostic "SCN-REF-002" $Path "Required reference is empty."
        }
        return
    }

    if (-not $Allowed.Contains($Value)) {
        Add-Diagnostic "SCN-REF-001" $Path "Unknown id '$Value'."
    }
}

function Test-ProvenanceCoverage {
    param([object]$Record, [string]$Path)

    $provenanceProperty = $Record.PSObject.Properties["provenance"]
    if ($null -eq $provenanceProperty -or @($Record.provenance).Count -eq 0) {
        Add-Diagnostic "SCN-PROV-001" $Path "Record has no provenance."
        return
    }

    $covered = New-Object System.Collections.Generic.HashSet[string]
    foreach ($entry in @($Record.provenance)) {
        foreach ($field in @($entry.applies_to)) {
            if (-not [string]::IsNullOrWhiteSpace($field)) {
                $null = $covered.Add([string]$field)
            }
        }
    }

    foreach ($property in $Record.PSObject.Properties) {
        if ($property.Name -in @("id", "kind", "provenance")) {
            continue
        }
        if ($null -eq $property.Value) {
            continue
        }
        if (-not $covered.Contains($property.Name)) {
            Add-Diagnostic "SCN-PROV-002" "$Path.$($property.Name)" "Business field is not covered by provenance.applies_to."
        }
    }
}

$manifestPath = Join-Path $scenarioPath "manifest.json"
$manifest = Read-JsonFile $manifestPath
if ($null -eq $manifest) {
    $diagnostics | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}

$components = @{}
foreach ($componentName in @($manifest.components)) {
    $componentPath = Join-Path $scenarioPath $componentName
    if (-not (Test-Path -LiteralPath $componentPath -PathType Leaf)) {
        Add-Diagnostic "SCN-FILE-001" $componentPath "Declared component does not exist."
        continue
    }
    $components[$componentName] = Read-JsonFile $componentPath
}

if ($diagnostics.Count -gt 0 -or $components.Count -ne @($manifest.components).Count) {
    $diagnostics | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}

$world = $components["world.json"]
$actors = $components["actors.json"]
$state = $components["state.json"]
if ($null -eq $world -or $null -eq $actors -or $null -eq $state) {
    Add-Diagnostic "SCN-FILE-002" $scenarioPath "Required MVP components are missing."
}

if ($manifest.rng.version -ne "xoshiro256ss.v1") {
    Add-Diagnostic "SCN-RNG-001" "manifest.rng.version" "Expected xoshiro256ss.v1."
}
if ($manifest.rng.derivation -ne "sha256-le.v1") {
    Add-Diagnostic "SCN-RNG-002" "manifest.rng.derivation" "Expected sha256-le.v1."
}
if ([string]$manifest.rng.root_seed_hex -notmatch '^[0-9A-Fa-f]{1,16}$') {
    Add-Diagnostic "SCN-RNG-003" "manifest.rng.root_seed_hex" "Expected 1-16 hexadecimal digits."
}

foreach ($componentName in $components.Keys) {
    $component = $components[$componentName]
    if ($component.scenario_id -ne $manifest.scenario_id) {
        Add-Diagnostic "SCN-VERSION-001" $componentName "scenario_id does not match manifest."
    }
    if ($component.schema_version -ne $manifest.schema_version) {
        Add-Diagnostic "SCN-VERSION-002" $componentName "schema_version does not match manifest."
    }
}

$recordSets = @(
    @{ Name = "organizations"; Records = @($world.organizations) },
    @{ Name = "access_rules"; Records = @($world.access_rules) },
    @{ Name = "places"; Records = @($world.places) },
    @{ Name = "routes"; Records = @($world.routes) },
    @{ Name = "persons"; Records = @($actors.persons) },
    @{ Name = "groups"; Records = @($actors.groups) },
    @{ Name = "items"; Records = @($state.items) },
    @{ Name = "propositions"; Records = @($state.propositions) },
    @{ Name = "beliefs"; Records = @($state.beliefs) },
    @{ Name = "commitments"; Records = @($state.commitments) },
    @{ Name = "plans"; Records = @($state.plans) }
)

$allIds = New-Object System.Collections.Generic.HashSet[string]
foreach ($set in $recordSets) {
    for ($index = 0; $index -lt $set.Records.Count; $index++) {
        $record = $set.Records[$index]
        $path = "$($set.Name)[$index]"
        if ([string]::IsNullOrWhiteSpace($record.id)) {
            Add-Diagnostic "SCN-ID-001" $path "Record id is empty."
        }
        elseif (-not $allIds.Add([string]$record.id)) {
            Add-Diagnostic "SCN-ID-002" "$path.id" "Duplicate id '$($record.id)'."
        }
        Test-ProvenanceCoverage $record $path
    }
}

for ($index = 0; $index -lt @($state.place_states).Count; $index++) {
    Test-ProvenanceCoverage @($state.place_states)[$index] "place_states[$index]"
}

function New-IdSet {
    param([object[]]$Records)
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($record in $Records) {
        $null = $set.Add([string]$record.id)
    }
    return $set
}

$organizationIds = New-IdSet @($world.organizations)
$accessIds = New-IdSet @($world.access_rules)
$placeIds = New-IdSet @($world.places)
$personIds = New-IdSet @($actors.persons)
$groupIds = New-IdSet @($actors.groups)
$actorIds = New-Object System.Collections.Generic.HashSet[string]
foreach ($id in $personIds) { $null = $actorIds.Add($id) }
foreach ($id in $groupIds) { $null = $actorIds.Add($id) }
$holderIds = New-Object System.Collections.Generic.HashSet[string]
foreach ($id in $actorIds) { $null = $holderIds.Add($id) }
foreach ($id in $organizationIds) { $null = $holderIds.Add($id) }
$itemIds = New-IdSet @($state.items)
$propositionIds = New-IdSet @($state.propositions)
$beliefIds = New-IdSet @($state.beliefs)
$commitmentIds = New-IdSet @($state.commitments)

Test-Reference $manifest.player_actor_id $personIds "manifest.player_actor_id"

for ($index = 0; $index -lt @($world.organizations).Count; $index++) {
    $record = @($world.organizations)[$index]
    if ($null -ne $record.parent) {
        Test-Reference $record.parent $organizationIds "organizations[$index].parent"
    }
}

for ($index = 0; $index -lt @($world.places).Count; $index++) {
    $record = @($world.places)[$index]
    if ($null -ne $record.parent) { Test-Reference $record.parent $placeIds "places[$index].parent" }
    Test-Reference $record.access_rule $accessIds "places[$index].access_rule"
    if ($null -ne $record.controller) { Test-Reference $record.controller $organizationIds "places[$index].controller" }
}

for ($index = 0; $index -lt @($world.routes).Count; $index++) {
    $record = @($world.routes)[$index]
    Test-Reference $record.from $placeIds "routes[$index].from"
    Test-Reference $record.to $placeIds "routes[$index].to"
    if ([int64]$record.distance_li_q10 -lt 0) { Add-Diagnostic "SCN-NUM-001" "routes[$index].distance_li_q10" "Distance cannot be negative." }
    foreach ($mode in $record.minutes.PSObject.Properties) {
        if ([int64]$mode.Value -lt 0) { Add-Diagnostic "SCN-NUM-002" "routes[$index].minutes.$($mode.Name)" "Duration cannot be negative." }
    }
}

for ($index = 0; $index -lt @($actors.persons).Count; $index++) {
    $record = @($actors.persons)[$index]
    Test-Reference $record.location $placeIds "persons[$index].location"
    foreach ($membership in @($record.memberships)) {
        Test-Reference $membership.organization $organizationIds "persons[$index].memberships.organization"
    }
}

for ($index = 0; $index -lt @($actors.groups).Count; $index++) {
    $record = @($actors.groups)[$index]
    Test-Reference $record.location $placeIds "groups[$index].location"
    if ($null -ne $record.organization) { Test-Reference $record.organization $organizationIds "groups[$index].organization" }
    if ([int64]$record.count -lt 0) { Add-Diagnostic "SCN-NUM-003" "groups[$index].count" "Count cannot be negative." }
}

for ($index = 0; $index -lt @($state.place_states).Count; $index++) {
    Test-Reference @($state.place_states)[$index].place $placeIds "place_states[$index].place"
}

for ($index = 0; $index -lt @($state.items).Count; $index++) {
    $record = @($state.items)[$index]
    Test-Reference $record.holder $holderIds "items[$index].holder"
    if ($null -ne $record.location) { Test-Reference $record.location $placeIds "items[$index].location" }
    if ($null -ne $record.author) { Test-Reference $record.author $personIds "items[$index].author" }
    if ($null -ne $record.intended_recipient) { Test-Reference $record.intended_recipient $personIds "items[$index].intended_recipient" }
    if ($null -ne $record.PSObject.Properties["proposition_ids"]) {
        foreach ($proposition in @($record.proposition_ids)) { Test-Reference $proposition $propositionIds "items[$index].proposition_ids" }
    }
    if ($null -ne $record.PSObject.Properties["valid_for"]) {
        foreach ($access in @($record.valid_for)) { Test-Reference $access $accessIds "items[$index].valid_for" }
    }
}

for ($index = 0; $index -lt @($state.beliefs).Count; $index++) {
    $record = @($state.beliefs)[$index]
    Test-Reference $record.holder $actorIds "beliefs[$index].holder"
    Test-Reference $record.proposition $propositionIds "beliefs[$index].proposition"
    if ($record.source -match "^(person|group)\.") { Test-Reference $record.source $actorIds "beliefs[$index].source" }
    if ([int64]$record.confidence_bp -lt 0 -or [int64]$record.confidence_bp -gt 10000) {
        Add-Diagnostic "SCN-NUM-004" "beliefs[$index].confidence_bp" "Confidence must be between 0 and 10000."
    }
}

for ($index = 0; $index -lt @($state.commitments).Count; $index++) {
    $record = @($state.commitments)[$index]
    Test-Reference $record.debtor $actorIds "commitments[$index].debtor"
    Test-Reference $record.creditor $actorIds "commitments[$index].creditor"
    Test-Reference $record.recipient $actorIds "commitments[$index].recipient"
    Test-Reference $record.target $allIds "commitments[$index].target"
}

for ($index = 0; $index -lt @($state.plans).Count; $index++) {
    $record = @($state.plans)[$index]
    Test-Reference $record.owner $actorIds "plans[$index].owner"
    foreach ($belief in @($record.belief_requirements)) { Test-Reference $belief $beliefIds "plans[$index].belief_requirements" }
}

$claimsPath = Join-Path $repoRoot "data/historical/claims/189-luoyang.json"
$claims = Read-JsonFile $claimsPath
$claimIds = New-IdSet @($claims.claims)
$provenanceSets = New-Object System.Collections.Generic.List[object]
$provenanceSets.Add(@($manifest.provenance))
foreach ($set in $recordSets) {
    foreach ($record in $set.Records) { $provenanceSets.Add(@($record.provenance)) }
}
foreach ($record in @($state.place_states)) { $provenanceSets.Add(@($record.provenance)) }

foreach ($entries in $provenanceSets) {
    foreach ($entry in @($entries)) {
        foreach ($claimId in @($entry.claim_ids) + @($entry.basis_claim_ids)) {
            if (-not [string]::IsNullOrWhiteSpace($claimId)) {
                Test-Reference $claimId $claimIds "provenance.claim_ids"
            }
        }
    }
}

if ($diagnostics.Count -gt 0) {
    $diagnostics | Sort-Object | ForEach-Object { [Console]::Error.WriteLine($_) }
    Write-Host "Scenario validation failed with $($diagnostics.Count) error(s)."
    exit 1
}

$summary = [ordered]@{
    scenario = $manifest.scenario_id
    organizations = @($world.organizations).Count
    places = @($world.places).Count
    routes = @($world.routes).Count
    persons = @($actors.persons).Count
    groups = @($actors.groups).Count
    items = @($state.items).Count
    propositions = @($state.propositions).Count
    beliefs = @($state.beliefs).Count
    commitments = @($state.commitments).Count
    plans = @($state.plans).Count
}

Write-Host "Scenario validation passed."
$summary.GetEnumerator() | ForEach-Object { Write-Host ("{0}: {1}" -f $_.Key, $_.Value) }
