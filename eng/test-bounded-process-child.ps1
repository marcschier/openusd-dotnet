#!/usr/bin/env pwsh
# Copyright (c) marcschier. Licensed under the MIT License.
param(
    [ValidateSet('arguments', 'allocate', 'sleep', 'exit', 'descendant')]
    [string]$Mode,
    [Parameter(ValueFromRemainingArguments)][AllowEmptyString()][string[]]$Values
)
$ErrorActionPreference = 'Stop'
switch ($Mode)
{
    'arguments' { ConvertTo-Json -InputObject @($Values) -Compress; break }
    'allocate'
    {
        $blocks = [Collections.Generic.List[IntPtr]]::new()
        $refused = $false
        try
        {
            for ($index = 0; $index -lt 32; $index++)
            {
                try
                {
                    $block = [Runtime.InteropServices.Marshal]::AllocHGlobal(8 * 1024 * 1024)
                }
                catch
                {
                    if ($_.Exception.GetBaseException() -isnot [OutOfMemoryException]) { throw }
                    $refused = $true
                    break
                }
                $blocks.Add($block)
                for ($offset = 0; $offset -lt 8 * 1024 * 1024; $offset += 4096)
                {
                    [Runtime.InteropServices.Marshal]::WriteByte($block, $offset, 1)
                }
            }
            [ordered]@{ refused = $refused; allocatedBytes = $blocks.Count * 8 * 1024 * 1024 } |
                ConvertTo-Json -Compress
        }
        finally
        {
            foreach ($block in $blocks) { [Runtime.InteropServices.Marshal]::FreeHGlobal($block) }
        }
        if (-not $refused) { exit 3 }
        break
    }
    'sleep' { Start-Sleep -Seconds 30; break }
    'exit' { exit 7 }
    'descendant'
    {
        $child = Start-Process -FilePath (Get-Process -Id $PID).Path -PassThru -NoNewWindow `
            -ArgumentList @('-NoLogo', '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 30')
        [Console]::WriteLine($child.Id)
        [Console]::Out.Flush()
        break
    }
}
