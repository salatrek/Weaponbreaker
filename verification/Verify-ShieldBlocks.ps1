param(
    [string]$GamePath = 'D:\Games\Steam\steamapps\common\Valheim',
    [string]$ProfilePath = 'C:\Users\Casie\AppData\Roaming\com.kesomannen.gale\valheim\profiles\newProfile'
)

$ErrorActionPreference = 'Stop'
$managed = Join-Path $GamePath 'valheim_Data\Managed'
[Reflection.Assembly]::LoadFrom((Join-Path $managed 'UnityEngine.CoreModule.dll')) | Out-Null
$game = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'assembly_valheim.dll'))
[Reflection.Assembly]::LoadFrom((Join-Path $ProfilePath 'BepInEx\core\0Harmony.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $ProfilePath 'BepInEx\core\BepInEx.dll')) | Out-Null
$plugin = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\bin\Debug\net48\Weaponbreaker.dll'))
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$tracker = $plugin.GetType('Weaponbreaker.Breaker.Detection.BlockOutcomeTracker')
$record = $tracker.GetMethod('Record', $flags)
$query = $tracker.GetMethod('WasBlockedByShield', $flags)
$clear = $tracker.GetMethod('Clear', $flags)

function New-Uninitialized([type]$type) {
    [Runtime.Serialization.FormatterServices]::GetUninitializedObject($type)
}
function Assert-Condition([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

# Pure per-hit state tests, using real game types without starting Unity.
$defender = New-Uninitialized ($game.GetType('Humanoid'))
$otherDefender = New-Uninitialized ($game.GetType('Humanoid'))
$hit = New-Uninitialized ($game.GetType('HitData'))
$otherHit = New-Uninitialized ($game.GetType('HitData'))
$blocker = New-Uninitialized ($game.GetType('ItemDrop+ItemData'))
$blocker.m_shared = New-Uninitialized ($game.GetType('ItemDrop+ItemData+SharedData'))
$blocker.m_shared.m_itemType = [Enum]::Parse($game.GetType('ItemDrop+ItemData+ItemType'), 'Shield')
$record.Invoke($null, @($defender, $hit, $blocker)) | Out-Null
Assert-Condition ($query.Invoke($null, @($defender, $hit))) 'Successful shield block must protect its hit'
Assert-Condition (!$query.Invoke($null, @($otherDefender, $hit))) 'Block must not protect another character'
Assert-Condition (!$query.Invoke($null, @($defender, $otherHit))) 'Block must not protect another hit'
$clear.Invoke($null, @($hit)) | Out-Null
Assert-Condition (!$query.Invoke($null, @($defender, $hit))) 'Protection must not leak into a reused hit'
$blocker.m_shared.m_itemType = [Enum]::Parse($game.GetType('ItemDrop+ItemData+ItemType'), 'OneHandedWeapon')
$record.Invoke($null, @($defender, $hit, $blocker)) | Out-Null
Assert-Condition (!$query.Invoke($null, @($defender, $hit))) 'Weapon block must retain player disarm eligibility'
$clear.Invoke($null, @($hit)) | Out-Null
Assert-Condition (!$query.Invoke($null, @($defender, $hit))) 'Failed or absent block must not grant immunity'

# Shield immunity must short-circuit before rolling probability or dropping items.
$serviceType = $plugin.GetType('Weaponbreaker.Breaker.Services.PlayerDisarmService')
$service = New-Uninitialized $serviceType
$serviceType.GetMethod('TryFromRealDamage').Invoke($service, @($null, $null, $null, $true)) | Out-Null
'PASS: shield, weapon, failed block, separate hit/character, cleanup, and shield immunity guard.'

# Read actual game IL via Cecil. Harmony's legacy detour runtime cannot initialize
# outside Unity on PowerShell Core; this test does not install a live game patch.
[Reflection.Assembly]::LoadFrom((Join-Path $ProfilePath 'BepInEx\core\Mono.Cecil.dll')) | Out-Null
$definition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $managed 'assembly_valheim.dll'))
$blockMethod = $definition.MainModule.GetType('Humanoid').Methods | Where-Object Name -eq 'BlockAttack' | Select-Object -First 1
$opcodes = @{}
foreach ($field in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
    $opcode = $field.GetValue($null)
    $opcodes[$opcode.Name] = $opcode
}
$original = [Collections.Generic.List[HarmonyLib.CodeInstruction]]::new()
foreach ($instruction in $blockMethod.Body.Instructions) {
    $operand = $instruction.Operand
    if ($operand -is [Mono.Cecil.MethodReference] -and $operand.FullName -eq 'System.Void HitData::BlockDamage(System.Single)') {
        $operand = $game.GetType('HitData').GetMethod('BlockDamage')
    }
    $original.Add([HarmonyLib.CodeInstruction]::new($opcodes[$instruction.OpCode.Name], $operand))
}
$patches = $plugin.GetType('Weaponbreaker.Breaker.Detection.WeaponbreakerPatches')
$transpiler = $patches.GetMethod('HumanoidBlockAttackTranspiler', $flags)
$transpilerArgs = [object[]]::new(1)
$transpilerArgs[0] = $original
$rewritten = $transpiler.Invoke($null, $transpilerArgs)
$callbacks = @($rewritten | Where-Object { $_.operand -is [Reflection.MethodInfo] -and $_.operand.Name -eq 'RecordSuccessfulBlock' })
Assert-Condition ($callbacks.Count -eq 1) 'Expected exactly one successful-block callback'
Assert-Condition ($rewritten.Count -eq $original.Count + 3) 'Unexpected IL rewrite size'
$callbackIndex = $rewritten.IndexOf($callbacks[0])
Assert-Condition ($rewritten[$callbackIndex - 3].operand.Name -eq 'BlockDamage') 'Callback must follow vanilla successful damage block'
'PASS: actual Valheim BlockAttack IL contains one matching branch; callback inserted directly after BlockDamage.'
