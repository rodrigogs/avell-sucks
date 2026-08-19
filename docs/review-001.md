# Review 001 — Security & architecture before enabling write controls

**Reviewer:** Claude Code (direct execution, board `gaming-center-replacement`, card `t_cbf99b5a`)
**Date:** 2026-08-19
**Base commit:** `9e34fc5` (`fix(wireless): reconcile radios after Windows resume`)
**Method:** source read of `AvellSucks.Core`, `.Core.Windows`, `.Api`, `.Server`, `.Mcp`,
`.UI` (security-relevant paths only) plus `installer/AvellSucks.iss`, `docs/adr/*`,
`SECURITY.md`. Full solution build + test run: **Core 138/138, Server 95/95, 0 failures**.
No hardware was touched; nothing was executed against a live EC.

This review deliberately covers the five focus areas from the card and the topics the
parent card asked for. It does **not** re-litigate what `SECURITY.md` already documents
correctly (write gate → allowlist → snapshot → verify → rollback → audit; loopback-exempt
fail-closed exposure; hash-only token storage). Those were re-read and confirmed present in
code, not just prose.

---

## Verdict

The safety harness is genuinely good: the write pipeline is well-layered, the exposure
policy is fail-closed in the right direction, and the "honest result, never a silent
success" discipline is real and consistently applied (`SafeEcWriter` returns typed results
instead of throwing; `RemoteWriteGate` returns a truthful denial reason). The gaps below are
mostly **at the edges the harness does not reach**: the trust boundary between an
unprivileged local user and the elevated process, the integrity of the files that hold the
safety decisions, and one missing guard that the codebase already implements elsewhere.

Nothing here blocks enabling write controls on the author's own verified machine. Findings
**H1** and **H2** should be fixed before the app is offered to anyone else's machine, and
**H3**/**H4** before `FirewallAutoOpen` or HTTPS exposure is recommended to users.

| ID | Severity | Finding | Anchor |
|---|---|---|---|
| H1 | High | EC write path has no machine-identity guard, while device controls do | `Core/Platforms/SafeEcWriter.cs:41` |
| H2 | High | Elevated auto-update runs an unverified binary staged in a user-writable directory | `UI/Startup/Updater.cs:125` |
| H3 | High | Self-signed listener PFX written with a null password into a world-readable directory | `UI/Views/SettingsView.xaml.cs:303` |
| H4 | Medium | `FirewallAutoOpen` opens the port to any source, on all profiles | `Server/Network/FirewallManager.cs:42` |
| M1 | Medium | Service `ImagePath` is registered unquoted; no check that the binary directory is admin-only | `UI/Services/WindowsServiceControl.cs:31` |
| M2 | Medium | Autostart grants silent elevation to whatever sits at the exe path | `UI/Startup/AutoStart.cs:72` |
| M3 | Medium | Hardware-write kill switch is persisted in a user-writable file | `UI/Settings/AppSettings.cs:41` |
| M4 | Medium | UI and Server write to two different audit files; neither sees the other | `Server/Hosting/ServerHostBuilder.cs:79` |
| M5 | Medium | Audit append is outside the lock; UI drops records on contention | `Core/Platforms/JsonlAuditLog.cs:45` |
| M6 | Medium | No `Host`-header allowlist, so DNS rebinding reaches the loopback-exempt API | `Server/Hosting/ServerHostBuilder.cs:204` |
| L1 | Low | Local write enablement is only a machine-wide env var, with no ACL-protected knob | `Server/Hosting/ServerHostBuilder.cs:75` |
| L2 | Low | System binaries launched by bare name from an elevated/SYSTEM process | `UI/Services/WindowsServiceControl.cs:53` |
| L3 | Low | `UseLoopbackOnly` is dead code that reads as an active control | `Server/Middleware/EnforceLoopbackMiddleware.cs:19` |
| L4 | Low | Windows-only backend declared in the portable `Core.Hardware` namespace | `Core.Windows/WmiEcBackend.cs:10` |

---

## 1. Hardware safety and least privilege

### H1 — The EC write path has no machine-identity guard, but the device-control path does

`MachineControlService` refuses every mutation on an unrecognised machine. Wireless,
touchpad, webcam, brightness and display-off all check `platform.SupportedMachine`
(`Core/Hardware/MachineControlService.cs:105`, `:232`), which resolves to a real WMI
identity read gated by `IsSupportedMachineIdentity` — Avell + model `1555`/`G1555`
(`Core.Windows/WindowsMachineControlBackend.cs:119`).

The EC write path has no equivalent check. `SafeEcWriter.TryWriteAsync` goes
gate → allowlist → snapshot → write (`Core/Platforms/SafeEcWriter.cs:41-73`), and
`EcWriteAllowlist` is a static address/value table with no notion of which machine it was
derived from (`Core/Platforms/EcWriteAllowlist.cs:41`).

This inverts the risk ordering. The controls that *are* guarded are recoverable — a
disabled touchpad is an annoyance. The controls that are *not* guarded are the ones
`SECURITY.md` itself describes as able to "overheat, destabilize, or permanently damage"
the machine: the fan control byte `0x751` and the power-limit registers
`0x783`/`0x784`/`0x785`, whose allowlists span `0..254` W.

Combined with `EnableHardwareWrites = true` by default (`UI/Settings/AppSettings.cs:41`),
the shipped behaviour on a non-Avell laptop is: writes enabled, no model guard, blind
writes to reverse-engineered addresses. The read-back verification does not help here —
as `SECURITY.md` correctly says, it "only confirms that the byte landed, not that it was
safe."

The mitigation currently in place is documentation ("On any other model, turn writes off").
That asks the user to act on a warning before the first write, which is the wrong
ordering for an irreversible outcome.

**Recommendation.** Lift the identity check to a shared precondition and apply it in
`SafeEcWriter` (or in `EcPipeline.BuildWriter`, so both front-ends inherit it):

- Add an `IMachineIdentityGuard` to `Core` with a `SupportedMachine` predicate, injected
  the same way `WriteGate` is. Keep `Core` portable — the WMI implementation stays in
  `Core.Windows`.
- Deny with a distinct, truthful result (`Error: "Denied: unverified machine model …"`) and
  audit it, exactly like the existing gate/allowlist denials. Do not throw.
- Invert the default for unrecognised hardware: `EnableHardwareWrites` should default to
  `true` only when the identity guard passes, and `false` otherwise. That preserves the
  "control center for the machine it was built for" intent without shipping a loaded gun to
  everyone else.
- Provide an explicit, deliberately awkward override (a typed model string in settings, not
  a checkbox) for users who genuinely want to experiment.

### M3 — The hardware-write kill switch lives in a user-writable file

`WriteGateInfo.EcWritesEnabled` resolves to the env override, else
`SettingsStore.Current.Settings.EnableHardwareWrites` (`UI/Services/StubServices.cs:45-47`).
That setting is persisted at `%AppData%\AvellSucks\settings.json`
(`UI/Settings/SettingsStore.cs:18-19`) — the roaming profile of the *unprivileged* user.

The project already understood this problem for the *network* config: `service.json` gets a
protected DACL via `ConfigFileSecurity.Harden` / `HardenDirectory`, precisely so "a non-admin
local user cannot rewrite it … and subvert the fail-closed auth model"
(`Core.Windows/ConfigFileSecurity.cs:9-14`, called from `UI/Services/ServiceConfigManager.cs:25-32`).
The same reasoning applies with more force to a switch that governs hardware damage, and it
is not applied.

Concretely: a user who read the safety warning and turned writes **off** has that decision
stored where any non-elevated process running as them can turn it back on, with no audit
entry for the policy change. The gate is re-read live on every write
(`Core/Platforms/WriteGate.cs:26-33`), so the change takes effect immediately.

**Recommendation.** Move `EnableHardwareWrites` (and only that field) into the
ACL-hardened `%ProgramData%` config alongside the network settings, or mirror it there and
have the gate read the hardened copy. Audit every transition of the gate to the existing
JSONL log — a policy flip is at least as interesting as a write.

### Least privilege — the whole UI runs as full administrator

`app.manifest:7` requests `requireAdministrator`, so the entire WPF process runs elevated:
XAML, the update `HttpClient`, LibreHardwareMonitor, `Process.Start` calls, the lot. Only
two things actually need privilege — the `root\WMI` EC calls and the PnP/service/schtasks
operations.

`docs/adr/002-local-api-surface.md:5,14,26` already records the right shape and explicitly
defers it: "Named-pipe gRPC only for privileged local UI↔service IPC … deferred until
privileged/local-UI separation ships." I agree with the deferral for the MVP, but the cost
is now concrete rather than theoretical: it is what makes H2 (elevated updater) and M2
(silent-elevation autostart) reachable at all, and what makes the "least privilege" claim in
this card's focus list currently unmet.

Keep it deferred, but record the trigger: **the first time a non-author machine is
supported, or the first time the server is exposed off-loopback by default, the split should
land.** See §3 for the ACL spec that separation will need.

---

## 2. Local API exposure

The exposure model is sound and I could not find a way to make it fail open. Worth stating
explicitly, since it is the part most likely to be "fixed" into a regression later:

- `CallerInfo.IsLoopback` is the single definition of machine-local, correctly excludes
  IPv6 link-local, and treats `null` as not-loopback (`Core/Service/CallerInfo.cs:20`).
- `BearerAuthenticationHandler` returns `NoResult()` rather than `Fail()` on a bad token
  (`Server/Security/BearerAuthenticationHandler.cs:37,41,46`), which is what lets the
  authorization policy — not the handler — make the loopback-exempt decision. Constant-time
  hash compare via `TokenHasher.FixedTimeEqualsHex`, length-checked, `FormatException`-safe.
- The fallback policy is registered as the global fallback *and* explicitly on
  `MapControllers()` and `MapMcp()` (`Server/Hosting/ServerHostBuilder.cs:176-179,228,232`) —
  belt and suspenders, correctly done.
- mTLS is fail-closed in the non-obvious direction: `OnCertificateValidated` is wired
  unconditionally when mTLS is on, and an empty configured thumbprint rejects *every* cert
  rather than accepting any (`Server/Hosting/ServerHostBuilder.cs:137-147`). The long comment
  block at `:263-295` explaining why `AllowedCertificateTypes = All` and
  `ValidateCertificateUse = false` are load-bearing is exactly the kind of note that prevents
  a future "cleanup" from silently reopening the hole. Leave it there.
- The remote-write gate is applied on **every** mutating endpoint. I checked all eight
  `[HttpPost]` actions across `FanController`, `PowerController`, `DevicesController` and all
  seven mutating MCP tools; each calls `remoteWrite.Check()` before touching hardware
  (`Api/Controllers/*.cs`, `Mcp/AvellSucksTools.cs:53,68,112`). No gap.

### M6 — No `Host`-header allowlist: DNS rebinding reaches the loopback-exempt API

There is no CORS policy, no antiforgery, and no `Host` validation anywhere in the pipeline
(`Server/Hosting/ServerHostBuilder.cs:204-232`). Absent CORS, a browser blocks a
cross-origin JSON `POST` at preflight, so ordinary CSRF is not the issue.

DNS rebinding is. An attacker's page on `evil.example` whose DNS is rebound to `127.0.0.1`
is *same-origin* with the loopback listener, so CORS never applies. Since loopback is exempt
from authentication (`Server/Security/ExposureAuthorization.cs:23`) **and** from the
remote-write gate (`Core/Service/RemoteWriteGate.cs:16`), such a page gets:

- unauthenticated read of `/api/system/snapshot` (memory and top process list — a
  reasonable fingerprint/recon primitive), and
- full fan/power/device actuation whenever local writes are enabled.

The `XLocalApi` response header (`:206`) is informational and enforces nothing.

**Recommendation.** Add a middleware that rejects any request whose `Host` header is not in
`{127.0.0.1, [::1], localhost}` ∪ `{configured BindAddress}` ∪ configured DNS name, before
authentication. Cheap, no false positives for the intended clients (`curl`, MCP,
Tailscale-by-IP), and it also blocks the same trick over a Tailscale name.

### H3 — The listener PFX is written unencrypted into a world-readable directory

Turning on HTTPS in Settings generates a self-signed cert and exports it with a **null
password**:

```csharp
// UI/Views/SettingsView.xaml.cs:302-305
var pfx = System.IO.Path.Combine(ServiceConfigPaths.Dir, "listener.pfx");
using var cert = SelfSignedCertFactory.Create("avellsucks-local");
SelfSignedCertFactory.ExportPfx(cert, pfx, null);
```

`ServiceConfigPaths.Dir` is `%ProgramData%\AvellSucks`. A PFX exported with a null password
carries the RSA-2048 private key with no encryption. The directory's DACL grants
`Everyone: ReadAndExecute` with `ObjectInherit`
(`Core.Windows/ConfigFileSecurity.cs:107-109`) — that is correct and deliberate for
`service.json`, which stores only a token *hash*, but the private key inherits the same
world-read. `ConfigFileSecurity.Harden` is only ever applied to `service.json`
(`UI/Services/ServiceConfigManager.cs:31`); nothing hardens `listener.pfx`. Note also the
ordering: the PFX is written at `:304` and `Save()` (which applies the DACLs) runs at `:309`.

Consequence chain: any unprivileged local user reads the server's TLS private key → stands
up an impersonating listener on the LAN/Tailscale address → since operators are told to
trust this self-signed cert explicitly, clients connect happily and present
`Authorization: Bearer <plaintext token>` → token disclosure → full authenticated remote
access, and hardware actuation if `AllowRemoteWrites` is on. The careful hash-only storage
of the token is bypassed, not broken.

**Recommendation.**
1. Do not export a bare PFX. Persist the key in the machine store
   (`X509Store(StoreName.My, StoreLocation.LocalMachine)`) and reference it by thumbprint in
   `service.json`; ACLs on the machine key container then do the work.
2. If a file must be used, generate a random password, protect it with DPAPI
   (`CryptProtectData`, machine scope), and apply an admin-only DACL to the PFX **before**
   writing the key — not a world-read inherited one. `ConfigFileSecurity` already has the
   right shape; it needs a `HardenSecret` variant with no `Everyone` ACE.
3. Regenerate any `listener.pfx` produced by a current build; treat existing ones as
   compromised.

### H4 — `FirewallAutoOpen` opens the port to any source, on every profile

```csharp
// Server/Network/FirewallManager.cs:42-44
runner.Run("netsh",
    $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}",
    out _);
```

No `remoteip=`, no `profile=`. `netsh` defaults to all profiles (including **Public**) and
any remote address. `SECURITY.md` advises "Prefer a Tailscale address; do not bind
`0.0.0.0`", and the bind address is indeed operator-chosen — but the firewall rule does not
follow the bind. An operator who binds a Tailscale IP and ticks auto-open still gets a hole
for `Any` on every network they subsequently join, including coffee-shop Wi-Fi. The listener
itself remains bound to the chosen address, so this is exposure of the *rule*, widening
blast radius for any future bind change or bind-address misconfiguration rather than an
immediate RCE. Port is an `int`, so no `netsh` argument injection.

**Recommendation.** Scope the rule to the configured bind address and to the Tailscale
subnet when applicable: add `remoteip=` (`100.64.0.0/10` for Tailscale, `LocalSubnet` for
LAN) and `profile=private` at minimum. Refuse to auto-open at all when
`BindAddress == "0.0.0.0"`. Also re-key the rule name per port so `ClosePort` cannot orphan
a rule from a previous port (it currently deletes by name only, which happens to work but is
accidental).

---

## 3. Windows service and named-pipe ACLs

**There is no named pipe.** `UseLoopbackOnly` notwithstanding, the only IPC is loopback TCP;
`docs/adr/002-local-api-surface.md:26,43` deferred named-pipe gRPC. So this focus area splits
into a real finding about the service today (M1) and a forward-looking spec (below).

### M1 — Unquoted `ImagePath`, and no check that the service binary directory is admin-only

```csharp
// UI/Services/WindowsServiceControl.cs:31
Sc($"create \"{ServiceName}\" binPath= \"{exePath}\" DisplayName= \"{DisplayName}\" start= auto");
```

`sc.exe` strips the surrounding quotes when parsing its command line, so the `ImagePath`
registry value ends up **unquoted**. Writing a quoted `ImagePath` requires the doubled form
`binPath= "\"C:\path\app.exe\""`. Two consequences, given the service is `start= auto` as
`LocalSystem` (`:31,35`):

1. **Unquoted service path.** The install path is `{autopf}\AvellSucks`
   (`installer/AvellSucks.iss:47`) — i.e. `C:\Program Files\AvellSucks\…`, which contains a
   space. The service controller will try `C:\Program.exe` first. On a default Windows
   install the `C:\` root DACL does not let a non-admin create files there, so this is not
   directly exploitable out of the box; on any machine whose root ACL has been loosened
   (common enough after third-party installers or a non-default disk setup) it is a
   straightforward LocalSystem privilege escalation.
2. **The more likely path.** `exePath` is derived from `AppContext.BaseDirectory`
   (`UI/Views/SettingsView.xaml.cs:265`), and `PrivilegesRequiredOverridesAllowed=dialog`
   (`installer/AvellSucks.iss:46`) lets the installer target a user-writable location — as
   does simply running the published output from a folder in the user profile. In that case a
   `LocalSystem` auto-start service is registered against a binary that the *unprivileged*
   user can overwrite. Any code running as that user, at medium integrity, replaces
   `AvellSucks.Server.exe` and gets SYSTEM at next boot. No check prevents this.

I did not execute `sc create` on the box (that would install a service on the user's
machine). The claim is verifiable in one read-only command:
`reg query "HKLM\SYSTEM\CurrentControlSet\Services\AvellSucksControl" /v ImagePath`.

**Recommendation.**
- Quote it: `binPath= "\"{exePath}\""`.
- Before installing, verify the binary's directory grants no write/create/delete to
  `Everyone`, `Users`, `Authenticated Users`, or the current non-admin SID; refuse the
  install with an honest message if it does. This same predicate fixes M2.
- Apply an explicit service DACL rather than inheriting the default. `sc sdset` with
  `D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)`
  grants SYSTEM and Administrators control and interactive users query-only — closing
  `SERVICE_CHANGE_CONFIG` / `WRITE_DAC` for anyone else. Worth doing even though the current
  default is not permissive, because it is the thing a future `sc config` refactor would
  otherwise silently rely on.
- Set `sc failure`/`sc failureflag` deliberately rather than leaving restart behaviour
  implicit, since the service now performs boot-time hardware reconciliation
  (`Server/Hosting/WirelessBootRestoreService.cs`).

### Spec for the named pipe, when privilege separation lands

Recording this now so the ACL decision is not made ad hoc under time pressure:

- Create with `PipeSecurity` explicitly; never rely on the default DACL, and never grant
  `PipeAccessRights.FullControl` to `WorldSid` or `AuthenticatedUserSid`.
- Grant `ReadWrite | Synchronize` to the specific interactive user SID (or
  `WellKnownSidType.InteractiveSid` if multi-user is required), and `FullControl` to
  `BuiltinAdministratorsSid` + `LocalSystemSid`. Deny `NetworkSid` outright so the pipe is
  never reachable over SMB.
- Set `PipeOptions.CurrentUserOnly` on the client, and on the server side always create the
  **first** instance with `NamedPipeServerStream(..., maxNumberOfServerInstances)` known and
  bounded — a squatting process that creates the pipe name first otherwise intercepts
  connections.
- Do not blanket-`ImpersonateClient()`; call it only to *check* the caller's identity/
  integrity level, then revert before touching hardware, so a compromised low-integrity
  client cannot borrow the broker's privilege.
- Log the resolved caller SID into the same audit trail (the `origin`/`identity` parameters
  on `SafeEcWriter.TryWriteAsync` already exist for exactly this and are currently only fed
  HTTP-shaped strings).

### L1 — Local write enablement has no ACL-protected knob

The Server's gate comes only from the environment
(`Server/Hosting/ServerHostBuilder.cs:75` → `WriteGate.FromEnvironment`,
`Core/Platforms/WriteGate.cs:41-46`). A `LocalSystem` service started by the SCM inherits no
per-user environment, so the only way to enable Server-side writes is a **machine-wide**
`GAMINGCENTER_ALLOW_EC_WRITES=1`. That is coarse (it also flips every other process that
reads the var) and it lives in the registry rather than in the file the project already
hardened for exactly this class of decision. The safe default is preserved, so this is Low —
but the moment an operator does enable it, every local process at any integrity level can
actuate hardware through the loopback exemption, unauthenticated.

**Recommendation.** Add `AllowLocalWrites` to `NetworkServiceConfig` (ACL-protected, and
consumed via `IOptionsMonitor` so it hot-reloads like the rest), and keep the env var as an
override only.

### L2 — System binaries launched by bare name from elevated/SYSTEM processes

`sc.exe` (`UI/Services/WindowsServiceControl.cs:53`), `schtasks.exe`
(`UI/Startup/AutoStart.cs:99`, `UI/App.xaml.cs:321`), `powercfg`
(`UI/Services/WindowsPowerPlan.cs:140`) and `netsh` (`Server/Network/FirewallManager.cs:16`)
are all started by name, resolved through `PATH`, from a process running elevated or as
SYSTEM. If any `PATH` entry is user-writable, that is a hijack into a privileged context.

The codebase already has the correct pattern — `PnPUtilPath` builds
`Path.Combine(Environment.SystemDirectory, "pnputil.exe")`
(`Core.Windows/WindowsMachineControlBackend.cs:124-125`). Apply it to the other five call
sites.

### L3 — `UseLoopbackOnly` is dead code that reads as an active control

`LoopbackSecurityExtensions.UseLoopbackOnly` is defined
(`Server/Middleware/EnforceLoopbackMiddleware.cs:19`) and **never called** anywhere in the
solution. Its doc comment states "Blocks non-loopback clients at the middleware layer before
controller code runs," which is exactly what an auditor skimming for the enforcement point
wants to find. Enforcement actually lives in the authorization policy that superseded it.

Two ways this bites: an auditor credits a control that is not running, and a well-meaning
future contributor wires it up and silently breaks all remote access. Delete it; the
`CallerInfo` delegation comment worth keeping is already duplicated at
`Core/Service/CallerInfo.cs:12-19`.

---

## 4. Autostart and elevation

### H2 — The elevated auto-update runs an unverified binary staged in a user-writable directory

```csharp
// UI/Startup/Updater.cs:125,138,153
var setupPath = Path.Combine(Path.GetTempPath(), $"AvellSucks-Setup-{check.LatestVersion}.exe");
…
if (new FileInfo(setupPath).Length < 1_000_000) { … return false; }
…
Process.Start(new ProcessStartInfo("cmd.exe", args) { … });
```

Three problems compound:

1. **No signature verification.** The only integrity check on a binary that is about to run
   with inherited administrator rights and no UAC prompt is "larger than 1 MB". The file's
   own header comment is candid about the elevation inheritance
   (`UI/Startup/Updater.cs:30-34`).
2. **User-writable staging directory.** For an elevated process launched from the user's
   desktop, `Path.GetTempPath()` resolves inside `%LOCALAPPDATA%\Temp` — writable by the
   *unprivileged* user. The filename is fully predictable (version-stamped).
3. **TOCTOU.** The size check at `:138` and the launch at `:153` are separate operations on a
   path an attacker controls. A `FileSystemWatcher`-driven swap in that window turns a
   medium-integrity foothold into SYSTEM-equivalent code execution.

Transport is HTTPS to the GitHub API with `EnsureSuccessStatusCode` (`:130`), which covers
the network leg; it does nothing about (2) or (3), which are local.

**Recommendation.**
1. Verify Authenticode on `setupPath` before launching — `WinVerifyTrust`, or at minimum
   `X509Certificate.CreateFromSignedFile` plus a pinned publisher thumbprint — and refuse on
   failure. Additionally pin the asset's SHA-256, published in the release notes or a signed
   manifest, and compare it against the bytes actually written.
2. Stage the download in a directory only Administrators can write (create it fresh with a
   protected DACL — `ConfigFileSecurity.HardenDirectory` is close to what is needed), not in
   `%TEMP%`.
3. Verify **after** the final write and hold an exclusive handle (`FileShare.None`) across
   verify-and-launch so the checked bytes are the executed bytes.
4. Drop the `cmd.exe /c "…" & start "" "…"` chain if possible; it re-parses attacker-relevant
   paths through the shell. Launch the installer directly and let its own restart logic
   relaunch the app.

### M2 — Autostart grants silent elevation to whatever sits at the exe path

```csharp
// UI/Startup/AutoStart.cs:72
RunSchTasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
```

The reasoning in the header comment (`:10-21`) is correct and well-researched: with
`requireAdministrator`, the HKCU `Run` key genuinely cannot auto-launch the app, and a
`/RL HIGHEST` `ONLOGON` task genuinely is the supported mechanism. Cleaning up the legacy
`Run` value (`:85-94`) so the two cannot fight is a nice touch, as is the uninstall-time task
deletion (`installer/AvellSucks.iss:101`).

The residual issue is the same missing predicate as M1: `/RL HIGHEST` means "run this path
elevated at every logon, no prompt". `exe` comes from
`Process.GetCurrentProcess().MainModule?.FileName` (`:32-43`). If that path is user-writable,
the task is a permanent UAC-bypass and elevated-persistence primitive for any code running as
that user. Under a default `Program Files` install it is fine; nothing enforces that this is
where the app lives.

**Recommendation.** Reuse the M1 directory-writability predicate: refuse to create the
autostart task (with an honest message in Settings) when the exe directory is writable by
non-admins. Also consider `/IT` semantics explicitly and pin the task author, so the task
cannot be quietly redefined by a non-admin — `schtasks /Create` under HKCU-scoped task
folders is modifiable by the owning user, which is the same class of gap.

---

## 5. Linux portability realism

**Assessment: the boundary is honestly drawn but currently one layer too shallow. The
realistic near-term scope is "read-only telemetry and the API tier on Linux", not hardware
control.**

What holds up:

- `AvellSucks.Core` really is `net10.0` with no Windows package references and no Windows
  types in the write pipeline. `SafeEcWriter`, `WriteGate`, `EcWriteAllowlist`,
  `RemoteWriteGate`, `CallerInfo`, `TokenHasher`, `ServiceConfigStore` are all portable. The
  seams (`IEcBackend`, `IEcWriter`, `IWriteAuditLog`, `IPlatformMachineControlBackend`) are
  the right ones.
- `docs/adr/001-stack-and-ipc.md:44,82,96` is refreshingly honest: "hardware backend stays
  Windows-only for MVP and likely indefinitely for EC/HID/oem-service", and the port
  "continues as Avalonia shell only once/if hardware access has a Linux implementation."
  That is the correct expectation to have set.

What does not:

- **The Server tier is Windows-pinned, and it need not be.** `AvellSucks.Server.csproj`
  targets `net10.0-windows` and depends on `Microsoft.Extensions.Hosting.WindowsServices`,
  while `AvellSucks.Api` and `AvellSucks.Mcp` are already portable `net10.0`. The Windows
  pinning is a *composition* choice (`ServerHostBuilder` news up `WmiEcBackend` and
  `WindowsMachineControlBackend` directly at `:71-73,87-89` and calls `UseWindowsService` at
  `:32`), not an inherent one. So the automation surface — the piece most likely to be wanted
  on a Linux box — cannot even build there.
- **No Linux backend exists, not even a refusing stub.** `WmiEcBackend` is the only
  `IEcBackend` implementation in `app/src`. On Linux the honest EC story is
  `/sys/kernel/debug/ec/ec0/io` (requires `CONFIG_ACPI_EC_DEBUGFS`, root, and is explicitly
  unsupported by kernel maintainers) or a model-specific platform driver. Neither is
  equivalent to the `AcpiTest_MULong` WMI method this project depends on, which is a
  *vendor-supplied ACPI-WMI* entry point (`Core.Windows/WmiEcBackend.cs:30-32`). There is no
  ACPI-WMI equivalent on Linux; you would be doing raw port I/O with none of the firmware
  arbitration. On the safety grounds established in §1 alone, EC *writes* should not be
  ported.
- **L4 — a namespace choice that undermines the boundary.** `WmiEcBackend.cs` lives in the
  `AvellSucks.Core.Windows` *project* but declares `namespace AvellSucks.Core.Hardware`
  (`Core.Windows/WmiEcBackend.cs:10`) — the portable namespace. `WindowsMachineControlBackend`
  is in `namespace AvellSucks.Core.Windows` correctly, so this is an inconsistency rather than
  a pattern. The effect is that `using AvellSucks.Core.Hardware;` can resolve a Windows-only
  type, so the compiler stops being the thing that tells you when you have crossed the
  portability line. Move it to `AvellSucks.Core.Windows`.

**Recommendation — the realistic split, in order.**
1. Move `WmiEcBackend` into the `AvellSucks.Core.Windows` namespace (L4) so the boundary is
   compiler-enforced.
2. Extract composition out of `ServerHostBuilder` into a platform module
   (`AddWindowsHardware()` / `AddLinuxHardware()`), retarget `AvellSucks.Server` to plain
   `net10.0`, and guard `UseWindowsService` with `OperatingSystem.IsWindows()`. That alone
   makes the API/MCP tier run on Linux against a read-only or stub backend, which is useful
   for development and CI without a Windows box.
3. Add a `NullMachineControlBackend` / read-only `IEcBackend` for non-Windows that reports
   unsupported *truthfully* rather than throwing — consistent with the existing "`null`
   renders as n/a, never a fake 0" principle.
4. Do **not** implement Linux EC writes. If a Linux read path is ever wanted, source it from
   `hwmon`/`coretemp`/`amdgpu` sysfs, which are supported interfaces, and keep the write
   surface Windows-only. Record that as an explicit ADR so it stops being an open question.

---

## 6. Can we avoid legacy native DLLs and drivers?

**Mostly yes, and the project is already in a much better position than the OEM app. One
residual ring-0 dependency remains, and it is in the telemetry path, not the control path.**

The control path is clean. EC access goes through the vendor's ACPI-WMI method
`root\WMI:AcpiTest_MULong::GetSetULong` (`Core.Windows/WmiEcBackend.cs:30-32`), reached with
`System.Management` / `Microsoft.Management.Infrastructure`. There is **no** `WinRing0`,
no `inpout32`, no custom `.sys`, and no P/Invoke into an OEM DLL. The only native imports in
the entire solution are four Windows API calls — three `cfgmgr32.dll` and one `user32.dll`
(`Core.Windows/WindowsMachineControlBackend.cs:319-328`) — which are documented OS APIs, not
legacy redistributables. That is the right answer and it should be stated as a design
commitment somewhere durable.

One correction for `SECURITY.md`: it says AvellSucks "writes reverse-engineered Embedded
Controller (EC) registers and CPU power limits **at ring-0**." That is not what the code
does. The writes are ring-3 calls into a firmware-provided ACPI-WMI method; the ACPI
interpreter in the kernel performs the actual EC transaction. This matters beyond pedantry:
the ring-0 phrasing implies a kernel driver in the threat model that does not exist, and it
undersells the genuinely safer architecture the project chose. The *hardware* risk statement
around it is accurate and should stay exactly as it is.

The residual dependency: `LibreHardwareMonitorLib` 0.9.6 (`UI/AvellSucks.UI.csproj`) is
configured with `IsCpuEnabled = true` (`UI/Hardware/HardwareMonitor.cs:23`). CPU sensor
support requires MSR reads, which LHM performs by installing and loading its own signed
kernel driver (the `WinRing0`-derived `LibreHardwareMonitor.sys`). That is precisely the class
of component this focus area wants to avoid: a general-purpose read/write-MSR and
read/write-port primitive, exposed to user mode, in a driver family with a history of being
abused as a bring-your-own-vulnerable-driver primitive and of appearing on Microsoft's
vulnerable-driver blocklist. Once it is loaded, *any* process that can open its device object
inherits arbitrary MSR/port access — a far broader privilege than anything this app needs.

Credit where due: `IsMotherboardEnabled = false` and `IsControllerEnabled = false`
(`:26-27`) already avoid the LPC/SuperIO direct-port-access paths, which are the worst part of
LHM. So the exposure is narrower than the default.

**Recommendation.**
1. **The driverless path is already built and already in use.** `UI/Hardware/CpuThermalZone.cs`
   exists precisely because LHM does not expose these on this platform, and it reads CPU
   temperature from the Thermal Zone **performance counter** (Kelvin → Celsius, "the way the
   OEM Gaming Center does") and effective clock from base-clock × `% Processor Performance`
   ("the way Task Manager does") — no driver, no MSR. Its own header comment names the one
   remaining gap: "Package power (RAPL) needs ring-0 MSR access and is not covered here."
2. So the question is narrow and answerable: **does the dashboard actually need RAPL package
   power?** If not, set `IsCpuEnabled = false` and the kernel driver stops loading entirely,
   with `CpuThermalZone` already covering temperature and clock, performance counters covering
   load, `GlobalMemoryStatusEx` covering memory, and vendor user-mode APIs (NVML / ADL)
   covering GPU. That would remove the last component in the product that could plausibly be
   called a legacy driver — a genuine security win for one config flag.
3. If some are load-bearing, make the driver load **opt-in and visible**: a Settings toggle,
   default off, with an honest explanation, and check the driver's device-object DACL after
   load. Do not let it load as an invisible side effect of opening the Dashboard tab.
4. Either way, document the decision in an ADR — "no custom kernel driver; ACPI-WMI only for
   control; telemetry driver opt-in" — so it survives the next contributor who wants one more
   sensor reading.

---

## 7. EC write audit trail

The record content is good: `EcWriteResult` captures attempt, allowed, executed, verified,
before, after, rollback state and error, and **every** outcome is audited — including gate and
allowlist denials (`Core/Platforms/SafeEcWriter.cs:59,71,92,114,183,192`). Denials being
audited is the part people usually forget. The `origin`/`identity` fields are populated with a
meaningful caller description on the API and MCP paths
(`Api/Security/RemoteWriteAuthorizer.cs:25-32`).

Two defects make the trail less trustworthy than it reads.

### M4 — Two audit files that do not know about each other

The Server writes its audit under `LocalApplicationData`:

```csharp
// Server/Hosting/ServerHostBuilder.cs:79-84
var dir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AvellSucks");
return new JsonlAuditLog(Path.Combine(dir, "ec-write-audit.jsonl"));
```

The UI writes to `AppPaths.AuditDir` = `%ProgramData%\AvellSucks\audit`
(`UI/AppPaths.cs:24`, `UI/Services/HardwareServices.cs:114-117`).

For a `LocalSystem` service, `LocalApplicationData` resolves to
`C:\Windows\System32\config\systemprofile\AppData\Local\AvellSucks` — a location no one will
think to look at, and one that a support-minded user cannot easily reach. So every remote and
MCP-originated write, which is exactly the set an incident responder cares most about, is
absent from the audit file that `docs/ARCHITECTURE.md` points at. `SECURITY.md`'s "Every write
goes through: … → JSONL audit" is true per-process but misleading as a system property.

**Recommendation.** Point both front-ends at `%ProgramData%\AvellSucks\audit` (already
DACL-hardened for integrity, and world-readable, which is fine — the records contain no
secrets). Keep the `GAMINGCENTER_AUDIT_DIR` override for tests. Include the process identity
and PID in each record so a merged file is still unambiguous. Then fix M5, because a single
shared file makes the locking bug reachable across processes.

### M5 — The append is outside the lock, so records are dropped under contention

```csharp
// Core/Platforms/JsonlAuditLog.cs:44-48
string line;
lock (_lock) { line = JsonSerializer.Serialize(result, s_json); }
File.AppendAllText(_path, line + Environment.NewLine);
```

The lock guards serialization — which needs no guarding, `JsonSerializerOptions` is
thread-safe for reuse — and leaves the actual file append unsynchronized.
`File.AppendAllText` opens with `FileShare.Read`, so a second concurrent append throws
`IOException` rather than interleaving. Under `swallowWriteErrors: true`, which is exactly how
the UI constructs it (`UI/Services/HardwareServices.cs:117`), that exception is swallowed and
**the audit record is silently lost**.

This is reachable today without any adversary: the UI applies a five-point fan curve as
five writes, and `FanStateMonitor`/`PowerStateMonitor`/`ProfileRestorer` actuate from timer
callbacks. Any overlap loses records from the log that is supposed to be the forensic record
of hardware mutation. Once M4 is fixed and both processes share a file, cross-process
contention makes it routine, and on the Server side (`swallowWriteErrors: false`) it surfaces
as a 500 instead.

Related, lower priority: the audit write happens *after* the hardware write
(`:192`), so a durability failure means the write happened with no record. For an
irreversible hardware action, an intent record written *before* the write, completed after,
is the stronger design.

**Recommendation.** Move `File.AppendAllText` inside the lock, and open with
`FileShare.ReadWrite` plus a bounded retry so cross-process appends serialize instead of
failing. Consider a single long-lived `FileStream` opened `FileMode.Append` with
`FileShare.Read` and an explicit `Flush(true)`, which also removes the per-record open/close
cost. Add a test that fires N concurrent `RecordAsync` calls and asserts N lines land — the
current suite does not cover concurrency, which is why this survived 233 green tests.

---

## Prioritized recommendations

**Before writes are enabled on any machine other than the author's verified Avell 1555:**
1. H1 — machine-identity guard on the EC write path; invert the write default on unrecognised
   hardware.
2. H2 — Authenticode/hash verification of the update binary, staged in an admin-only
   directory, verified under an exclusive handle.
3. M3 — move the hardware-write kill switch into the ACL-hardened config; audit gate flips.
4. M1/M2 — quote `ImagePath`; refuse service install and autostart when the binary directory
   is writable by non-admins.

**Before HTTPS or `FirewallAutoOpen` is recommended to users:**
5. H3 — stop exporting a null-password PFX into a world-readable directory; treat existing
   `listener.pfx` files as compromised.
6. H4 — scope the firewall rule with `remoteip=` and `profile=`; refuse auto-open on
   `0.0.0.0`.
7. M6 — `Host`-header allowlist middleware ahead of authentication.

**Audit-trail integrity (cheap, do them together):**
8. M5 — append inside the lock, shared-read open, concurrency test.
9. M4 — one audit location for both front-ends, with process identity in each record.

**Hygiene and boundary enforcement:**
10. L2 — full `System32` paths for `sc`, `schtasks`, `powercfg`, `netsh`.
11. L3 — delete the dead `UseLoopbackOnly`.
12. L4 — move `WmiEcBackend` into the `AvellSucks.Core.Windows` namespace.
13. L1 — `AllowLocalWrites` in the hardened config instead of a machine-wide env var.
14. Correct the "ring-0" phrasing in `SECURITY.md` (§6); it overstates the threat model and
    undersells the architecture.
15. `app/Makefile` still refers to `GamingCenter.Replacement.slnx` and `src/GamingCenter.*`,
    which no longer exist — every target is broken. Not security, but it is the documented
    entry point for `make test`.

## Out of scope / not verified here

- No hardware was actuated; no EC register was read or written. Every claim about hardware
  behaviour is sourced from code comments and `docs/`, not from measurement.
- `sc create` was not executed, so the unquoted-`ImagePath` claim (M1) rests on documented
  `sc.exe` argument parsing. Verify with
  `reg query "HKLM\SYSTEM\CurrentControlSet\Services\AvellSucksControl" /v ImagePath`.
- No mTLS handshake was exercised. As the code comments note, `TestServer` bypasses Kestrel
  TLS; end-to-end coverage lives in `scripts/mtls-positive.ps1` and needs an elevated Windows
  run.
- The RGB/HID surface is a stub (`HardwareServices.CreateRgbService` returns
  `LocalRgbService`, `UI/Services/HardwareServices.cs:70`) and was not reviewed.
- Allowlist *values* were not re-derived from the decompiled OEM app; this review takes the
  address/value map as given and comments only on the guards around it.
- Supply chain beyond the updater (CI signing, release provenance, `LibreHardwareMonitorLib`
  pinning) was not examined.
