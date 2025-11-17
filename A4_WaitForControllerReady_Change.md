# A4 Adımı: GCodeExecutionManager.WaitForControllerReadyAsync

## Hedef
`WaitForControllerReadyAsync` metodundaki manuel "?" sorgusunu kaldırmak ve CentralStatusQuerier'a güvenmek.

## Dosya
`CncControlApp/Managers/GCodeExecutionManager.cs`

## Değişiklik
**Satır 228** civarında şu satırı **KALDIR**:
```csharp
await SendGCodeCommandAsync("?");
```

## Önce (Satır 220-230):
```csharp
private async Task<bool> WaitForControllerReadyAsync(int timeoutMs = 4000)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string lastStatus = string.Empty;
    while (sw.ElapsedMilliseconds < timeoutMs)
    {
    var s = GetMachineStatusSafe();
      if (!string.Equals(s, lastStatus)) { _log($"> Status: {s}"); lastStatus = s; }
  if (!StatusIsAlarm(s) && !StatusIsHold(s)) return true;
  if (StatusIsAlarm(s)) await SendGCodeCommandAsync("$X");
        await Task.Delay(250); await SendGCodeCommandAsync("?");  // ❌ BU SATIRI KALDIR
    }
    return false;
}
```

## Sonra:
```csharp
private async Task<bool> WaitForControllerReadyAsync(int timeoutMs = 4000)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    string lastStatus = string.Empty;
  while (sw.ElapsedMilliseconds < timeoutMs)
    {
    var s = GetMachineStatusSafe();
        if (!string.Equals(s, lastStatus)) { _log($"> Status: {s}"); lastStatus = s; }
   if (!StatusIsAlarm(s) && !StatusIsHold(s)) return true;
        if (StatusIsAlarm(s)) await SendGCodeCommandAsync("$X");
        // ✅ A4: Removed manual "?" - rely on CentralStatusQuerier
        await Task.Delay(250);
    }
    return false;
}
```

## Açıklama
- **ÖNCE:** Her 250ms'de bir "?" gönderiyordu
- **SONRA:** Sadece 250ms bekliyor, status güncellemeleri CentralStatusQuerier'dan geliyor
- **Sonuç:** Alarm/Hold recovery sırasında "?" spam yok, MCU yükü azaldı

## Test
1. Alarm durumuna gir (örn. Ctrl+X, limit switch tetikle)
2. `$X` gönder
3. Recovery sırasında "?" gönderilmediğini doğrula
4. Status güncellemelerinin CentralStatusQuerier'dan geldiğini doğrula
