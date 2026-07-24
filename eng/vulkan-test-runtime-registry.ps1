#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.

function Test-VulkanTestRuntimeElevation
{
    if (-not $IsWindows)
    {
        return $false
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try
    {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    finally
    {
        $identity.Dispose()
    }
}

function Test-VulkanTestRuntimeRegistryValueEqual
{
    param(
        [AllowNull()][object]$Left,
        [AllowNull()][object]$Right
    )

    if ($null -eq $Left -or $null -eq $Right)
    {
        return $null -eq $Left -and $null -eq $Right
    }
    if ($Left -is [Array] -and $Right -is [Array])
    {
        if ($Left.Length -ne $Right.Length)
        {
            return $false
        }
        for ($index = 0; $index -lt $Left.Length; $index++)
        {
            if (-not [object]::Equals($Left[$index], $Right[$index]))
            {
                return $false
            }
        }
        return $true
    }
    return [object]::Equals($Left, $Right)
}

function Get-VulkanTestRuntimeDriverRegistration
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,

        [Microsoft.Win32.RegistryHive]$Hive =
            [Microsoft.Win32.RegistryHive]::LocalMachine,

        [Microsoft.Win32.RegistryView]$View =
            [Microsoft.Win32.RegistryView]::Registry64,

        [string]$SubKeyPath = 'SOFTWARE\Khronos\Vulkan\Drivers'
    )

    $result = [pscustomobject]@{
        Exists = $false
        Name = $null
        Kind = $null
        Value = $null
    }
    if (-not $IsWindows)
    {
        return $result
    }

    $manifest = [System.IO.Path]::GetFullPath($ManifestPath)
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, $View)
    try
    {
        $key = $baseKey.OpenSubKey($SubKeyPath, $false)
        if ($null -eq $key)
        {
            return $result
        }
        try
        {
            $name = $key.GetValueNames() |
                Where-Object {
                    [string]::Equals(
                        $_,
                        $manifest,
                        [StringComparison]::OrdinalIgnoreCase)
                } |
                Select-Object -First 1
            if ($null -eq $name)
            {
                return $result
            }

            $result.Exists = $true
            $result.Name = $name
            $result.Kind = $key.GetValueKind($name)
            $result.Value = $key.GetValue(
                $name,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            return $result
        }
        finally
        {
            $key.Dispose()
        }
    }
    finally
    {
        $baseKey.Dispose()
    }
}

function Register-VulkanTestRuntimeDriver
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,

        [Microsoft.Win32.RegistryHive]$Hive =
            [Microsoft.Win32.RegistryHive]::LocalMachine,

        [Microsoft.Win32.RegistryView]$View =
            [Microsoft.Win32.RegistryView]::Registry64,

        [string]$SubKeyPath = 'SOFTWARE\Khronos\Vulkan\Drivers',

        [switch]$ForceRegistration
    )

    $manifest = [System.IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path -LiteralPath $manifest -PathType Leaf))
    {
        throw "The Vulkan driver manifest does not exist: $manifest"
    }

    $elevated = Test-VulkanTestRuntimeElevation
    $required = $IsWindows -and (
        $ForceRegistration -or
        ($Hive -eq [Microsoft.Win32.RegistryHive]::LocalMachine -and $elevated))
    $state = [pscustomobject]@{
        PSTypeName = 'OpenUsd.VulkanDriverRegistrationState'
        ManifestPath = $manifest
        Hive = $Hive
        View = $View
        SubKeyPath = $SubKeyPath
        IsElevated = $elevated
        RegistrationRequired = $required
        RegistrationAttempted = $false
        ValueMutationStarted = $false
        Applied = $false
        PreviousValueExists = $false
        PreviousValueName = $null
        PreviousValueKind = $null
        PreviousValue = $null
        CreatedSubKeyPaths = @()
        Restored = $false
    }
    if (-not $IsWindows)
    {
        Write-Host '[vulkan-runtime] registry=not-applicable platform=non-Windows'
        return $state
    }
    if (-not $required)
    {
        Write-Host (
            "[vulkan-runtime] registry=not-required elevated=$elevated " +
            'driverSelection=environment')
        return $state
    }

    $state.RegistrationAttempted = $true
    $baseKey = $null
    $key = $null
    try
    {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, $View)
        $currentPath = ''
        $createdPaths = [System.Collections.Generic.List[string]]::new()
        foreach ($segment in $SubKeyPath -split '\\')
        {
            $currentPath = if ([string]::IsNullOrEmpty($currentPath))
            {
                $segment
            }
            else
            {
                "$currentPath\$segment"
            }
            $existingKey = $baseKey.OpenSubKey($currentPath, $false)
            if ($null -eq $existingKey)
            {
                $createdPaths.Add($currentPath)
            }
            else
            {
                $existingKey.Dispose()
            }
        }
        $state.CreatedSubKeyPaths = @($createdPaths)

        $key = $baseKey.CreateSubKey($SubKeyPath, $true)
        if ($null -eq $key)
        {
            throw "Could not open the Vulkan driver registry key '$SubKeyPath'."
        }
        $previous = Get-VulkanTestRuntimeDriverRegistration `
            -ManifestPath $manifest `
            -Hive $Hive `
            -View $View `
            -SubKeyPath $SubKeyPath
        if ($previous.Exists)
        {
            $state.PreviousValueExists = $true
            $state.PreviousValueName = $previous.Name
            $state.PreviousValueKind = $previous.Kind
            $state.PreviousValue = $previous.Value
        }

        $state.ValueMutationStarted = $true
        $key.SetValue(
            $manifest,
            0,
            [Microsoft.Win32.RegistryValueKind]::DWord)
        $state.Applied = $true
        $active = Get-VulkanTestRuntimeDriverRegistration `
            -ManifestPath $manifest `
            -Hive $Hive `
            -View $View `
            -SubKeyPath $SubKeyPath
        if (-not $active.Exists -or
            $active.Kind -ne [Microsoft.Win32.RegistryValueKind]::DWord -or
            [int]$active.Value -ne 0)
        {
            throw 'The temporary Vulkan driver registry value was not activated.'
        }

        $prior = if ($state.PreviousValueExists)
        {
            "kind=$($state.PreviousValueKind)"
        }
        else
        {
            'absent'
        }
        $hiveName = if ($Hive -eq [Microsoft.Win32.RegistryHive]::LocalMachine)
        {
            'HKLM'
        }
        else
        {
            $Hive.ToString()
        }
        $viewName = if ($View -eq [Microsoft.Win32.RegistryView]::Registry64)
        {
            '64'
        }
        else
        {
            '32'
        }
        Write-Host (
            "[vulkan-runtime] registry=$hiveName$viewName\$SubKeyPath " +
            "value='$manifest' data=0 prior=$prior")
        return $state
    }
    catch
    {
        $failure = $_
        if ($null -ne $key)
        {
            $key.Dispose()
            $key = $null
        }
        if ($null -ne $baseKey)
        {
            $baseKey.Dispose()
            $baseKey = $null
        }
        try
        {
            Restore-VulkanTestRuntimeDriver -State $state
        }
        catch
        {
            throw [AggregateException]::new(
                'Vulkan driver registration and rollback both failed.',
                @($failure.Exception, $_.Exception))
        }
        throw $failure
    }
    finally
    {
        if ($null -ne $key)
        {
            $key.Dispose()
        }
        if ($null -ne $baseKey)
        {
            $baseKey.Dispose()
        }
    }
}

function Restore-VulkanTestRuntimeDriver
{
    [CmdletBinding()]
    param(
        [AllowNull()]
        [psobject]$State
    )

    if ($null -eq $State -or
        -not $State.RegistrationAttempted -or
        $State.Restored)
    {
        return
    }

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        $State.Hive,
        $State.View)
    try
    {
        if ($State.ValueMutationStarted -and $State.PreviousValueExists)
        {
            $key = $baseKey.CreateSubKey($State.SubKeyPath, $true)
            try
            {
                $key.DeleteValue($State.ManifestPath, $false)
                $key.SetValue(
                    $State.PreviousValueName,
                    $State.PreviousValue,
                    $State.PreviousValueKind)
            }
            finally
            {
                $key.Dispose()
            }

            $restored = Get-VulkanTestRuntimeDriverRegistration `
                -ManifestPath $State.ManifestPath `
                -Hive $State.Hive `
                -View $State.View `
                -SubKeyPath $State.SubKeyPath
            if (-not $restored.Exists -or
                $restored.Kind -ne $State.PreviousValueKind -or
                -not (Test-VulkanTestRuntimeRegistryValueEqual `
                    -Left $restored.Value `
                    -Right $State.PreviousValue))
            {
                throw 'The prior Vulkan driver registry value was not restored.'
            }
            Write-Host (
                "[vulkan-runtime] registry-restored value=" +
                "'$($State.PreviousValueName)' kind=$($State.PreviousValueKind)")
        }
        elseif ($State.ValueMutationStarted)
        {
            $key = $baseKey.OpenSubKey($State.SubKeyPath, $true)
            if ($null -ne $key)
            {
                try
                {
                    $key.DeleteValue($State.ManifestPath, $false)
                }
                finally
                {
                    $key.Dispose()
                }
            }
            $restored = Get-VulkanTestRuntimeDriverRegistration `
                -ManifestPath $State.ManifestPath `
                -Hive $State.Hive `
                -View $State.View `
                -SubKeyPath $State.SubKeyPath
            if ($restored.Exists)
            {
                throw 'The temporary Vulkan driver registry value was not removed.'
            }
            Write-Host (
                "[vulkan-runtime] registry-deleted value='$($State.ManifestPath)'")
        }

        foreach ($path in @($State.CreatedSubKeyPaths) |
            Sort-Object Length -Descending)
        {
            $createdKey = $baseKey.OpenSubKey($path, $false)
            if ($null -eq $createdKey)
            {
                continue
            }
            try
            {
                $empty = $createdKey.GetSubKeyNames().Count -eq 0 -and
                    $createdKey.GetValueNames().Count -eq 0
            }
            finally
            {
                $createdKey.Dispose()
            }
            if ($empty)
            {
                $baseKey.DeleteSubKey($path, $false)
            }
        }
        $State.Restored = $true
    }
    finally
    {
        $baseKey.Dispose()
    }
}
