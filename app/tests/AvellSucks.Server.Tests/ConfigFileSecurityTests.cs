using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using AvellSucks.Core.Windows;
using Xunit;

namespace AvellSucks.Server.Tests;

/// <summary>
/// Verifies that <see cref="ConfigFileSecurity.Harden"/> locks the service config
/// down to admin-write / world-read so a non-admin cannot subvert the auth model.
/// Server.Tests only runs on Windows; guard defensively anyway.
/// </summary>
public sealed class ConfigFileSecurityTests
{
    [Fact]
    public void Harden_ProtectsDacl_AdminFullControl_EveryoneReadOnly_NoNonAdminWrite()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // not applicable off Windows

        var path = Path.Combine(Path.GetTempPath(), $"acltest_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{}");
        try
        {
            // (a) Harden succeeds.
            var ok = ConfigFileSecurity.Harden(path);
            Assert.True(ok, "Harden should return true on an existing file when run with sufficient rights.");

            var security = new FileInfo(path).GetAccessControl();

            // (b) inheritance disabled (protected DACL).
            Assert.True(security.AreAccessRulesProtected, "DACL should be protected (inheritance disabled).");

            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            // (c) allow FullControl for Administrators.
            var adminFull = rules
                .Cast<FileSystemAccessRule>()
                .Any(r => r.AccessControlType == AccessControlType.Allow
                          && r.IdentityReference is SecurityIdentifier sid && sid == admins
                          && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
            Assert.True(adminFull, "Administrators must have an allow FullControl rule.");

            // (d) Everyone allow rule is Read only — no Write/Modify/FullControl.
            var everyoneAllows = rules
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow
                            && r.IdentityReference is SecurityIdentifier sid && sid == everyone)
                .ToList();
            Assert.NotEmpty(everyoneAllows);
            foreach (var r in everyoneAllows)
            {
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Write), "Everyone must not have Write.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Modify), "Everyone must not have Modify.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.FullControl), "Everyone must not have FullControl.");
                Assert.True(r.FileSystemRights.HasFlag(FileSystemRights.Read), "Everyone should have Read.");
            }

            // (e) NO allow rule grants Write/Modify/FullControl to a non-admin identity.
            var admin = admins;
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var offenders = rules
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow)
                .Where(r => r.FileSystemRights.HasFlag(FileSystemRights.Write)
                            || r.FileSystemRights.HasFlag(FileSystemRights.Modify)
                            || r.FileSystemRights.HasFlag(FileSystemRights.FullControl))
                .Where(r => !(r.IdentityReference is SecurityIdentifier sid && (sid == admin || sid == system)))
                .ToList();
            Assert.Empty(offenders);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void HardenDirectory_ProtectsDacl_AdminFullControl_EveryoneReadListOnly_NoNonAdminCreateOrDelete()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // not applicable off Windows

        var dir = Path.Combine(Path.GetTempPath(), $"acldirtest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // (a) HardenDirectory succeeds.
            var ok = ConfigFileSecurity.HardenDirectory(dir);
            Assert.True(ok, "HardenDirectory should return true on an existing directory when run with sufficient rights.");

            var security = new DirectoryInfo(dir).GetAccessControl();

            // (b) inheritance disabled (protected DACL).
            Assert.True(security.AreAccessRulesProtected, "Directory DACL should be protected (inheritance disabled).");

            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

            // (c) allow FullControl for Administrators.
            var adminFull = rules
                .Cast<FileSystemAccessRule>()
                .Any(r => r.AccessControlType == AccessControlType.Allow
                          && r.IdentityReference is SecurityIdentifier sid && sid == admins
                          && r.FileSystemRights.HasFlag(FileSystemRights.FullControl));
            Assert.True(adminFull, "Administrators must have an allow FullControl rule.");

            // (d) Everyone allow rules limited to read/list — no Write/CreateFiles/Delete/Modify/FullControl.
            var everyoneAllows = rules
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow
                            && r.IdentityReference is SecurityIdentifier sid && sid == everyone)
                .ToList();
            Assert.NotEmpty(everyoneAllows);
            foreach (var r in everyoneAllows)
            {
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Write), "Everyone must not have Write.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.CreateFiles), "Everyone must not have CreateFiles.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.CreateDirectories), "Everyone must not have CreateDirectories.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Delete), "Everyone must not have Delete.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles), "Everyone must not have DeleteSubdirectoriesAndFiles.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.Modify), "Everyone must not have Modify.");
                Assert.False(r.FileSystemRights.HasFlag(FileSystemRights.FullControl), "Everyone must not have FullControl.");
                Assert.True(r.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute), "Everyone should have ReadAndExecute (read + list/traverse).");
            }

            // (e) NO allow rule grants create/write/delete to a non-admin/non-system identity.
            var offenders = rules
                .Cast<FileSystemAccessRule>()
                .Where(r => r.AccessControlType == AccessControlType.Allow)
                .Where(r => r.FileSystemRights.HasFlag(FileSystemRights.Write)
                            || r.FileSystemRights.HasFlag(FileSystemRights.CreateFiles)
                            || r.FileSystemRights.HasFlag(FileSystemRights.CreateDirectories)
                            || r.FileSystemRights.HasFlag(FileSystemRights.Delete)
                            || r.FileSystemRights.HasFlag(FileSystemRights.DeleteSubdirectoriesAndFiles)
                            || r.FileSystemRights.HasFlag(FileSystemRights.Modify)
                            || r.FileSystemRights.HasFlag(FileSystemRights.FullControl))
                .Where(r => !(r.IdentityReference is SecurityIdentifier sid && (sid == admins || sid == system)))
                .ToList();
            Assert.Empty(offenders);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
