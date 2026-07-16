using System;
using System.Security.Cryptography;
using AvellSucks.Core.Service;
using AvellSucks.Core.Windows;

namespace AvellSucks.UI.Services;

/// <summary>
/// UI-side read/write of the shared service config in %ProgramData%\AvellSucks.
/// The service hot-reloads on save. Token generation returns the plaintext ONCE
/// (to show the user); only its hash is persisted.
/// </summary>
public sealed class ServiceConfigManager
{
    public NetworkServiceConfig Load() => ServiceConfigStore.Load(ServiceConfigPaths.ConfigFile);

    public void Save(NetworkServiceConfig cfg)
    {
        // Store.Save creates the directory and writes the file.
        ServiceConfigStore.Save(ServiceConfigPaths.ConfigFile, cfg);

        // Lock the directory down so a non-admin local user can't delete/replace
        // service.json — the file ACL alone can't stop that; this makes the guarantee
        // explicit instead of relying on the inherited ProgramData default. Best-effort.
        if (!ConfigFileSecurity.HardenDirectory(ServiceConfigPaths.Dir))
            App.Trace("ServiceConfigManager.Save: ConfigFileSecurity.HardenDirectory returned false — service config directory ACL not applied.");

        // Lock the file down to admin-write / world-read so a non-admin local user
        // can't rewrite it and subvert the fail-closed auth model. Best-effort: the
        // UI is the elevated writer so this normally succeeds in production.
        if (!ConfigFileSecurity.Harden(ServiceConfigPaths.ConfigFile))
            App.Trace("ServiceConfigManager.Save: ConfigFileSecurity.Harden returned false — service config ACL not applied.");
    }

    /// <summary>Generate a URL-safe token, store its hash on <paramref name="cfg"/>, return the plaintext.</summary>
    public string GenerateAndStoreToken(NetworkServiceConfig cfg)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        cfg.Auth.BearerTokenSha256 = TokenHasher.HashHex(raw);
        return raw;
    }
}
