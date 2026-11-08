# agent-commit/lib/record-model.ps1
# Normalizing parsed record JSON (any schema / PowerShell version) into one
# record shape.
# Loaded by agent-commit.ps1.

function Get-JsonValue($Object, [string]$Name, $Default) {
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function ConvertTo-NullableInt($Value) {
    if ($null -eq $Value -or [string]$Value -eq '') { return $null }
    return [int]$Value
}

function ConvertTo-Record($Raw) {
    # Normalize a parsed record (any schema/PowerShell version) into one shape.
    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($f in @(Get-JsonValue $Raw 'files' @())) {
        if ($null -eq $f) { continue }
        $files.Add([pscustomobject]@{
            path   = [string](Get-JsonValue $f 'path' '')
            status = [string](Get-JsonValue $f 'status' '')
            add    = (ConvertTo-NullableInt (Get-JsonValue $f 'add' $null))
            del    = (ConvertTo-NullableInt (Get-JsonValue $f 'del' $null))
            oid    = [string](Get-JsonValue $f 'oid' '')
            wtOid  = [string](Get-JsonValue $f 'wtOid' '')
        })
    }
    $created = ConvertTo-IsoTime (Get-JsonValue $Raw 'createdAt' '')
    $updated = ConvertTo-IsoTime (Get-JsonValue $Raw 'updatedAt' '')
    if (-not $updated) { $updated = $created }
    return [pscustomobject]@{
        schema      = [int](Get-JsonValue $Raw 'schema' $script:RecordSchema)
        id          = [int](Get-JsonValue $Raw 'id' 0)
        revision    = [int](Get-JsonValue $Raw 'revision' 1)
        agent       = [string](Get-JsonValue $Raw 'agent' '')
        task        = [string](Get-JsonValue $Raw 'task' '')
        title       = [string](Get-JsonValue $Raw 'title' '')
        description = [string](Get-JsonValue $Raw 'description' '')
        bump        = ([string](Get-JsonValue $Raw 'bump' 'none')).ToLowerInvariant()
        files       = $files.ToArray()
        baseSha     = [string](Get-JsonValue $Raw 'baseSha' '')
        baseBranch  = [string](Get-JsonValue $Raw 'baseBranch' '')
        worktree    = [string](Get-JsonValue $Raw 'worktree' '')
        createdAt   = $created
        updatedAt   = $updated
        patch       = [string](Get-JsonValue $Raw 'patch' '')
        status      = [string](Get-JsonValue $Raw 'status' 'pending')
        commit      = [string](Get-JsonValue $Raw 'commit' '')
        pushed      = [bool](Get-JsonValue $Raw 'pushed' $false)
        appliedAt   = ConvertTo-IsoTime (Get-JsonValue $Raw 'appliedAt' '')
        discardedAt = ConvertTo-IsoTime (Get-JsonValue $Raw 'discardedAt' '')
    }
}
