using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AvellSucks.Core.Windows;

/// <summary>
/// Applies a hardened, inheritance-disabled DACL to the service config file so a
/// non-admin local user cannot rewrite it (e.g. flip <c>allowRemoteWrites</c> or
/// swap the bearer-token hash) and subvert the fail-closed auth model. The config
/// stores only the token HASH — never a plaintext secret — so world-READ is safe
/// and matches the spec's "only Administrators can write, everyone can read".
/// </summary>
[SupportedOSPlatform("windows")]
public static class ConfigFileSecurity
{
    /// <summary>
    /// On an EXISTING file at <paramref name="path"/>, apply a PROTECTED DACL that
    /// grants Administrators + SYSTEM FullControl and Everyone Read, dropping all
    /// inherited/other ACEs so a normal user has no write. Best-effort: returns
    /// <c>true</c> on success, <c>false</c> on any failure, and never throws (a
    /// non-elevated dev run may not be able to change ACLs and must not crash).
    /// </summary>
    public static bool Harden(string path)
    {
        try
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            var security = new FileSecurity();

            // Drop inheritance and do NOT copy inherited rules → start from a clean,
            // explicit-only DACL (no lingering "Users" write from ProgramData).
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            security.AddAccessRule(new FileSystemAccessRule(
                admins, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                everyone, FileSystemRights.Read, AccessControlType.Allow));

            // Apply the DACL first — this is the load-bearing part (the file owner
            // holds implicit WRITE_DAC, so this succeeds even when unelevated).
            var fileInfo = new FileInfo(path);
            fileInfo.SetAccessControl(security);

            // Set owner to Administrators too, separately and best-effort: changing
            // owner can require SeRestorePrivilege, and we must not let that failure
            // undo the DACL hardening above.
            try
            {
                var ownerSecurity = new FileSecurity();
                ownerSecurity.SetOwner(admins);
                fileInfo.SetAccessControl(ownerSecurity);
            }
            catch
            {
                // best-effort only
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// On an EXISTING directory at <paramref name="path"/> (e.g. %ProgramData%\AvellSucks),
    /// apply a PROTECTED (inheritance-disabled) DACL so the guarantee that a non-admin
    /// cannot delete/replace the config file does not rely on the inherited ProgramData
    /// default. Grants Administrators + SYSTEM FullControl and Everyone read+list/traverse
    /// only (no create/delete/write of children). Rules inherit to children so the
    /// elevated UI-created files/subdirs stay locked down. Best-effort: returns
    /// <c>true</c> on success, <c>false</c> on any failure, and never throws.
    /// </summary>
    public static bool HardenDirectory(string path)
    {
        try
        {
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            var security = new DirectorySecurity();

            // Drop inheritance and do NOT copy inherited rules → start from a clean,
            // explicit-only DACL (no lingering "Users"/"Authenticated Users" create
            // from the ProgramData default that would let a non-admin drop or replace
            // service.json).
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // Propagate to children the elevated UI creates (files + subdirs).
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

            security.AddAccessRule(new FileSystemAccessRule(
                admins, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            // Read + list/traverse only — no create/delete/write of children.
            security.AddAccessRule(new FileSystemAccessRule(
                everyone, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
                inherit, PropagationFlags.None, AccessControlType.Allow));

            // Apply the DACL first — this is the load-bearing part.
            var dirInfo = new DirectoryInfo(path);
            dirInfo.SetAccessControl(security);

            // Set owner to Administrators too, separately and best-effort: changing
            // owner can require SeRestorePrivilege, and we must not let that failure
            // undo the DACL hardening above.
            try
            {
                var ownerSecurity = new DirectorySecurity();
                ownerSecurity.SetOwner(admins);
                dirInfo.SetAccessControl(ownerSecurity);
            }
            catch
            {
                // best-effort only
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
