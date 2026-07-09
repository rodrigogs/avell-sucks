# EC write approval checklist

> **The first fan-mode write is done** — 0x751 writes are verified on hardware and
> the read-back + rollback are now automated in `SafeEcWriter` (settle+retry). This
> stays as the **template runbook for any future risky EC write** (a new register,
> a wider value range), not a pending one-shot.

Before the first EC write to a new address, every item below must be checked off.
Writing the embedded controller is a destructive operation — errors can brick the
machine or trigger thermal protection. Treat a new target as irreversible until
proven otherwise.

## Preconditions

- [ ] EC read-only probe has been run on the target machine and the
      fan-control addresses (0x743..0x747, 0x751, 0x75D..0x768, 0x782)
      return plausible values.
- [ ] The OEM GamingCenter app is **fully closed** (GamingCenter.exe,
      GamingCenterTray.exe, LaunchServGM.exe) so it cannot race the EC.
- [ ] No other EC-monitoring tool (HWiNFO, RWEverything, etc.) is running.
- [ ] The laptop is on AC power, not battery.
- [ ] Temperatures are normal (CPU < 70 °C, GPU < 70 °C).
- [ ] A backup/restore path is known: the original byte value at 0x751
      has been recorded and the rollback procedure below is understood.
- [ ] `GAMINGCENTER_ALLOW_EC_WRITES=1` is set **only** in the process
      that will perform the write, not system-wide.

## Write plan (first test)

The safest first write is the **auto/normal** mode:

```
Address: 0x751 (1873)
Value:   0x00 (0 = normal/auto fan)
Reason:  "first-write-test: restore auto"
```

This is idempotent on most ECs because `0x00` is the factory default.
Do **not** test boost (0x40) or custom levels (0x81..0x85) first.

## Execution steps

1. **Snapshot before.**
   ```powershell
   .\scripts\ec-snapshot.ps1 -Label pre-first-write -OutFile .\snapshots\pre.json
   ```
   Confirm 0x751 value is recorded.

2. **Run the write** through `SafeEcWriter` (never call the backend directly).
   The writer will:
   - check the gate (`IsWriteAllowed`),
   - check the allowlist (`0x751, 0x00` is registered),
   - snapshot before,
   - execute the WMI `GetSetULong` write,
   - read-back and verify,
   - audit-log the full attempt.

3. **Observe.** Listen to the fan for 10 seconds. It should stay at auto RPM.

4. **Snapshot after.**
   ```powershell
   .\scripts\ec-snapshot.ps1 -Label post-first-write -OutFile .\snapshots\post.json
   ```

5. **Diff.**
   ```powershell
   .\scripts\ec-snapshot.ps1 -Diff -DiffBefore .\snapshots\pre.json -DiffAfter .\snapshots\post.json
   ```

## Rollback procedure

If the read-back mismatches, the fan behaves abnormally, or temperatures rise:

1. Write the original value back to 0x751 (captured in the "before" snapshot).
2. If the EC seems stuck, power-cycle: full shutdown, unplug AC, hold power
   30 s, restart.
3. If the machine does not recover, boot into BIOS and restore defaults.

## Abort criteria

Stop immediately and roll back if:

- WMI returns an error (insufficient privilege, timeout).
- The read-back value does not match what was written.
- The fan spins to maximum and stays there for > 15 s after a normal-mode write.
- CPU temperature rises above 85 °C within 60 s of the write.
- Any visible artifact, freeze, or BSOD.

## Sign-off

| Role | Name | Date |
|------|------|------|
| Operator (performs write) | | |
| Approver (Rodrigo) | | |

No write to 0x751 may proceed without both signatures above.
