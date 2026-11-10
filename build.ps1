$ErrorActionPreference = 'Stop'

$Project = Split-Path -Parent $MyInvocation.MyCommand.Path
$Managed = Join-Path $Project 'managed'
$GameManaged = 'F:\SteamLibrary\steamapps\common\Broforce\Broforce_beta_Data\Managed'
$Compiler = 'C:\Windows\Microsoft.NET\Framework\v3.5\csc.exe'
$Python = 'G:\Codex_mmx\2026-08-17\.venv\Scripts\python.exe'

if (-not (Test-Path -LiteralPath $Compiler)) {
    throw "C# compiler not found: $Compiler"
}
if (-not (Test-Path -LiteralPath (Join-Path $GameManaged 'Assembly-CSharp.dll'))) {
    throw "Broforce assemblies not found: $GameManaged"
}

& $Compiler /nologo /target:library /optimize+ /platform:x64 `
    "/out:$Managed\BroforceLifeBridge.dll" `
    "/reference:$GameManaged\Assembly-CSharp.dll" `
    "/reference:$GameManaged\UnityEngine.dll" `
    "/reference:$GameManaged\UnityEngine.CoreModule.dll" `
    "/reference:$GameManaged\UnityEngine.PhysicsModule.dll" `
    "/reference:$GameManaged\UnityEngine.TextRenderingModule.dll" `
    "$Managed\BroforceLifeBridge.cs" "$Managed\EnhancedVillagerAI.cs" "$Managed\VillagerTactics.cs" "$Managed\CompanionAI.cs" "$Managed\CompanionSpawner.cs" "$Managed\CombatAssist.cs"
if ($LASTEXITCODE -ne 0) {
    throw "Bridge compilation failed with exit code $LASTEXITCODE"
}

# Verify the compiled bridge's player slots without starting or injecting the game.
$SmokeExe = Join-Path $Project 'build\BridgeSlotsSmoke.exe'
New-Item -ItemType Directory -Path (Split-Path -Parent $SmokeExe) -Force | Out-Null
& $Compiler /nologo "/out:$SmokeExe" (Join-Path $Project 'tests\BridgeSlotsSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Bridge slot smoke test compilation failed' }
& $SmokeExe (Join-Path $Managed 'BroforceLifeBridge.dll') $GameManaged
if ($LASTEXITCODE -ne 0) { throw 'Bridge player slot isolation test failed' }

$AiSmokeExe = Join-Path $Project 'build\VillagerAiSmoke.exe'
& $Compiler /nologo /define:VILLAGER_AI_TEST "/out:$AiSmokeExe" `
    (Join-Path $Project 'tests\VillagerAiSmoke.cs') `
    (Join-Path $Managed 'EnhancedVillagerAI.cs') (Join-Path $Managed 'VillagerTactics.cs')
if ($LASTEXITCODE -ne 0) { throw 'Villager AI test compilation failed' }
& $AiSmokeExe
if ($LASTEXITCODE -ne 0) { throw 'Villager AI behavior tests failed' }

$CompanionSmokeExe = Join-Path $Project 'build\CompanionAiSmoke.exe'
& $Compiler /nologo /define:COMPANION_AI_TEST "/out:$CompanionSmokeExe" `
    (Join-Path $Project 'tests\CompanionAiSmoke.cs') `
    (Join-Path $Managed 'CompanionAI.cs') (Join-Path $Managed 'VillagerTactics.cs')
if ($LASTEXITCODE -ne 0) { throw 'Companion AI test compilation failed' }
& $CompanionSmokeExe
if ($LASTEXITCODE -ne 0) { throw 'Companion AI behavior tests failed' }

$CombatSmokeExe = Join-Path $Project 'build\CombatAssistSmoke.exe'
& $Compiler /nologo "/out:$CombatSmokeExe" `
    (Join-Path $Project 'tests\CombatAssistSmoke.cs') (Join-Path $Managed 'CombatAssist.cs')
if ($LASTEXITCODE -ne 0) { throw 'Combat assist test compilation failed' }
& $CombatSmokeExe
if ($LASTEXITCODE -ne 0) { throw 'Combat assist behavior tests failed' }

& $Python -m unittest discover -s (Join-Path $Project 'tests') -v
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE"
}

& $Python -m PyInstaller --noconfirm --clean (Join-Path $Project 'BroforceLifeTrainer.spec')
if ($LASTEXITCODE -ne 0) {
    throw "EXE packaging failed with exit code $LASTEXITCODE"
}

Write-Host "Build complete: $Project\dist\Broforce_加一命修改器_V14.exe"
