param(
    [string]$NewProjectName
)

# Get the script's own name to exclude it from processing
$ScriptName = $MyInvocation.MyCommand.Name

function Delete-BinAndObjFolders {
    # Get the current directory
    $Path = Get-Location

    # Find and delete all bin and obj folders
    Get-ChildItem -Path $Path -Recurse -Directory | Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" } | ForEach-Object {
        Remove-Item $_.FullName -Force -Recurse
        Write-Host "Deleted folder: $($_.FullName)"
    }
}

function Rename-ItemRecursive {
    param (
        [string]$OldValue,
        [string]$NewValue
    )

    # Get the current directory
    $Path = Get-Location

    # Rename all subdirectories recursively, skipping .git and .vs folders
    Get-ChildItem -Path $Path -Recurse | Where-Object { $_.PSIsContainer -and $_.FullName -notmatch '\\\.git\\' -and $_.FullName -notmatch '\\\.vs\\' } | ForEach-Object {
        $newDirectoryName = $_.Name -replace $OldValue, $NewValue
        if ($newDirectoryName -ne $_.Name) {
            Rename-Item $_.FullName -NewName $newDirectoryName
            Write-Host "Renamed directory: $($_.Name) to $newDirectoryName"
        }
    }

    # Rename all files, excluding the script itself, DLLs, PDBs, and image files
    Get-ChildItem -Path $Path -Recurse | Where-Object { -not $_.PSIsContainer -and $_.Name -ne $ScriptName -and $_.Extension -notmatch '\.(dll|pdb|jpg|jpeg|png|gif)$' -and $_.FullName -notmatch '\\\.git\\' -and $_.FullName -notmatch '\\\.vs\\' } | ForEach-Object {
        $newFileName = $_.Name -replace $OldValue, $NewValue
        if ($newFileName -ne $_.Name) {
            Rename-Item $_.FullName -NewName $newFileName
            Write-Host "Renamed file: $($_.Name) to $newFileName"
        }
    }
}

function Update-FileContent {
    param (
        [string]$OldValue,
        [string]$NewValue,
        [string]$CaseInsensitiveOldValue
    )

    # Get the current directory
    $Path = Get-Location

    # Update content inside each file, excluding the script file, DLLs, PDBs, and image files
    Get-ChildItem -Path $Path -Recurse | Where-Object { -not $_.PSIsContainer -and $_.Name -ne $ScriptName -and $_.Extension -notmatch '\.(dll|pdb|jpg|jpeg|png|gif)$' -and $_.FullName -notmatch '\\\.git\\' -and $_.FullName -notmatch '\\\.vs\\' } | ForEach-Object {
        $contentChanged = $false
        $content = Get-Content $_.FullName
        $updatedContent = $content -replace $OldValue, $NewValue -replace $CaseInsensitiveOldValue, $NewValue.ToLower()

        if ($updatedContent -ne $content) {
            $contentChanged = $true
        }

        if ($contentChanged) {
            $updatedContent | Set-Content $_.FullName
            Write-Host "Updated content in file: $($_.Name)"
        }
    }
}

# Usage
$OldValue = "Template"
$CaseInsensitiveOldValue = "template"

# First, delete all bin and obj folders
Delete-BinAndObjFolders

# Ensure there is a backup before running operations that modify your files and directories
Rename-ItemRecursive -OldValue $OldValue -NewValue $NewProjectName
Update-FileContent -OldValue $OldValue -NewValue $NewProjectName -CaseInsensitiveOldValue $CaseInsensitiveOldValue

Write-Host "DONE"
