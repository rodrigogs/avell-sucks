using System.Net;
using AvellSucks.Core;
using AvellSucks.Core.Hardware;
using AvellSucks.Core.Platforms;
using AvellSucks.Core.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AvellSucks.Server.Hosting;

/// <summary>
/// Builds the AvellSucks control-server pipeline. Shared by console mode,
/// Windows Service mode, and integration tests so all three run the identical
/// host. (Extracted from the original Program.cs; auth/bind/MCP are layered on
/// in later composition steps.)
/// </summary>
public static class ServerHostBuilder
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // --- Shared service config (hot-reloaded from %ProgramData%\AvellSucks\service.json) ---
        var configDir = Path.GetDirectoryName(ServiceConfigPaths.ConfigFile)!;
        Directory.CreateDirectory(configDir);
        builder.Configuration.AddJsonFile(new Microsoft.Extensions.FileProviders.PhysicalFileProvider(configDir),
            Path.GetFileName(ServiceConfigPaths.ConfigFile),
            optional: true, reloadOnChange: true);
        builder.Services.Configure<NetworkServiceConfig>(builder.Configuration);

        // --- Run as a Windows Service when launched by the SCM (no-op under console) ---
        builder.Host.UseWindowsService(o => o.ServiceName = "AvellSucks Control Service");

        // Resolve the current config once for bind decisions (Kestrel URL is fixed at start;
        // rebinding requires a service restart — auth/remote-write/mcp toggles apply live).
        var config = ServiceConfigStore.Load(ServiceConfigPaths.ConfigFile);

        // Bind decisions. An explicit arg still wins for console/dev use; otherwise the
        // config drives it. env GAMINGCENTER_REQUIRE_HTTPS still forces https.
        var argPort = ResolvePort(args);
        if (args.Length > 0) config.Port = argPort;
        if (RequireHttps() && config.Scheme != "https") config.Scheme = "https";

        // HTTPS-cert wiring: when serving https with a configured PFX, bind Kestrel
        // explicitly with the cert (app.Urls alone can't attach a PFX). Plain http keeps
        // app.Urls / BuildListenUrl (set after Build below).
        var useHttpsCert = config.Scheme == "https" && !string.IsNullOrEmpty(config.HttpsCertPath);
        if (useHttpsCert)
        {
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(IPAddress.Parse(config.BindAddress), config.Port,
                    listenOptions => listenOptions.UseHttps(config.HttpsCertPath!, password: null, https =>
                    {
                        // mTLS is an ADDITIONAL accepted credential, not a hard requirement:
                        // REQUEST a client cert (AllowCertificate) so bearer-only clients can
                        // still connect without one. RequireCertificate would break cert-less
                        // bearer clients. When mTLS is off, the https branch is untouched.
                        if (config.Auth.MtlsEnabled)
                            https.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
                    }));
            });
        }

        builder.Services.AddLogging(cfg => cfg.AddConsole());
        builder.Services.AddControllers();
        builder.Services.AddOpenApi();

        builder.Services.AddSingleton<IEventPublisher, ConsoleEventPublisher>();

        builder.Services.AddSingleton<WmiEcBackend>();
        builder.Services.AddSingleton<IEcBackend>(sp => sp.GetRequiredService<WmiEcBackend>());
        builder.Services.AddSingleton<IEcWriter>(sp => sp.GetRequiredService<WmiEcBackend>());

        builder.Services.AddSingleton<WriteGate>(WriteGate.FromEnvironment());
        builder.Services.AddSingleton<EcWriteAllowlist>();
        builder.Services.AddSingleton<IWriteAuditLog>(_ =>
        {
            var dir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
                      ?? Path.Combine(
                          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                          "AvellSucks");
            return new JsonlAuditLog(Path.Combine(dir, "ec-write-audit.jsonl"));
        });
        builder.Services.AddSingleton<SafeEcWriter>();

        builder.Services.AddSingleton<WindowsMachineControlBackend>();
        builder.Services.AddSingleton<IPlatformMachineControlBackend>(sp =>
            sp.GetRequiredService<WindowsMachineControlBackend>());
        builder.Services.AddSingleton<IMachineControlAuditLog>(_ =>
        {
            var dir = Environment.GetEnvironmentVariable("GAMINGCENTER_AUDIT_DIR")
                      ?? Path.Combine(
                          Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                          "AvellSucks");
            return new JsonlMachineControlAuditLog(Path.Combine(dir, "machine-control-audit.jsonl"));
        });
        builder.Services.AddSingleton<IMachineControlService, MachineControlService>();

        // Session 0 wireless boot-restore. Self-skips when not running as a
        // Windows Service (console/dev), so it is safe to register unconditionally.
        builder.Services.AddHostedService<WirelessBootRestoreService>();

        // --- Request context + remote-write authorizer (shared with MCP) ---
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<AvellSucks.Api.Security.RemoteWriteAuthorizer>();

        // --- Authentication: bearer always registered; certificate when configured ---
        var authBuilder = builder.Services
            .AddAuthentication(Security.BearerAuthenticationHandler.Scheme)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, Security.BearerAuthenticationHandler>(
                Security.BearerAuthenticationHandler.Scheme, _ => { });
        if (config.Auth.MtlsEnabled)
        {
            authBuilder.AddCertificate(options =>
            {
                // Trust is established by THUMBPRINT PINNING (OnCertificateValidated below),
                // not by a system-trusted CA chain. The product's own UI generates a
                // self-signed client cert (SelfSignedCertFactory) for home/Tailscale use, so
                // the default chained validation would reject exactly the certs operators are
                // meant to use. The load-bearing enablers are AllowedCertificateTypes=All and
                // ValidateCertificateUse=false (see ApplyThumbprintPinChainTrust's doc for the
                // full, source-verified rationale). Kept fail-closed by the events below.
                ApplyThumbprintPinChainTrust(options);
                // FAIL-CLOSED: always wire OnCertificateValidated when mTLS is enabled so no
                // presented client cert is ever left unchecked. If no allowed thumbprint is
                // configured there is nothing to match against — reject every cert rather than
                // fail open and authenticate any (e.g. self-signed) client. CertificateThumbprint
                // .Matches already returns false on a null/empty configured thumbprint, so an
                // unconditional !Matches(...) check covers both the empty and mismatch cases.
                options.Events = new Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationEvents
                {
                    // Thumbprint match uses the pure, unit-tested CertificateThumbprint.Matches
                    // helper. The full TLS client-cert handshake can't run through
                    // WebApplicationFactory (TestServer bypasses Kestrel TLS), so end-to-end
                    // mTLS is validated by the Task 18 elevated smoke test.
                    OnCertificateValidated = ctx =>
                    {
                        if (string.IsNullOrEmpty(config.Auth.MtlsCaThumbprint))
                        {
                            ctx.Fail("mTLS enabled but no allowed client-certificate thumbprint configured.");
                        }
                        else if (!Security.CertificateThumbprint.Matches(
                                     ctx.ClientCertificate.Thumbprint, config.Auth.MtlsCaThumbprint))
                        {
                            ctx.Fail("Client certificate thumbprint not allowed.");
                        }
                        return Task.CompletedTask;
                    },
                };
            });
        }

        // --- Authorization: fail-closed exposure policy as the global fallback ---
        // Both policies must evaluate EVERY registered auth scheme so the principal
        // is populated from bearer OR client cert before the loopback-or-authenticated
        // check runs. Name the Certificate scheme only when mTLS is enabled (otherwise
        // it isn't registered and AddAuthenticationSchemes would throw "scheme not
        // registered"). This makes both cert-only and bearer-only clients work.
        var authSchemes = config.Auth.MtlsEnabled
            ? new[]
              {
                  Security.BearerAuthenticationHandler.Scheme,
                  Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationDefaults.AuthenticationScheme,
              }
            : new[] { Security.BearerAuthenticationHandler.Scheme };

        builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
            Security.ExposureAuthorizationHandler>();
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Security.ExposureAuthorization.PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(authSchemes);
                policy.Requirements.Add(new Security.LoopbackOrAuthenticatedRequirement());
            })
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(authSchemes)
                .AddRequirements(new Security.LoopbackOrAuthenticatedRequirement())
                .Build());

        // --- MCP server (enabled per config) ---
        if (config.McpEnabled)
        {
            builder.Services.AddMcpServer()
                .WithHttpTransport(o => o.Stateless = true)
                .WithTools<AvellSucks.Mcp.AvellSucksTools>();
        }

        var app = builder.Build();

        // mTLS can only engage over TLS. If the operator enabled it on a plain-http
        // (or https-without-cert) bind, client certs are never requested — warn loudly
        // at startup rather than silently doing nothing. Do not crash.
        if (config.Auth.MtlsEnabled && !useHttpsCert)
        {
            app.Logger.LogWarning(
                "mTLS is enabled but the server is not bound to HTTPS with a certificate " +
                "(scheme='{Scheme}', httpsCertPath set={HasCert}). Client certificates will " +
                "not be requested and mTLS will not engage. Configure scheme=https with a PFX " +
                "to use client-cert auth.",
                config.Scheme, !string.IsNullOrEmpty(config.HttpsCertPath));
        }

        app.Use((context, next) =>
        {
            context.Response.Headers["XLocalApi"] = bool.TrueString;
            return next(context);
        });

        // HTTPS enforcement preserved (env or config).
        if (RequireHttps() || config.Scheme == "https")
        {
            app.Use(async (context, next) =>
            {
                if (!context.Request.IsHttps)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Bad Request: HTTPS is required.");
                    return;
                }
                await next(context);
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi();
        app.MapControllers().RequireAuthorization(Security.ExposureAuthorization.PolicyName);

        // --- MCP endpoint (only mapped — so it only exists — when enabled) ---
        if (config.McpEnabled)
            app.MapMcp("/mcp").RequireAuthorization(Security.ExposureAuthorization.PolicyName);

        // Plain-http bind via app.Urls (https-with-cert already bound Kestrel above).
        if (!useHttpsCert)
        {
            app.Urls.Clear();
            app.Urls.Add(BuildListenUrl(config));
        }

        // Open/close the firewall port to match config (no-op unless FirewallAutoOpen).
        try
        {
            var fw = new Network.FirewallManager(new Network.NetshCommandRunner());
            if (config.FirewallAutoOpen) fw.OpenPort(config.Port);
            else fw.ClosePort(config.Port);
        }
        catch { /* best-effort; non-elevated console runs may not have rights */ }

        return app;
    }

    public static int ResolvePort(string[] args)
    {
        if (args.Length > 0 && int.TryParse(args[0], out var p) && p is > 0 and < 65536) return p;
        return 5055;
    }

    /// <summary>Kestrel listen URL for a config: "{scheme}://{address}:{port}".</summary>
    public static string BuildListenUrl(NetworkServiceConfig cfg)
        => $"{cfg.Scheme}://{cfg.BindAddress}:{cfg.Port}";

    /// <summary>
    /// Configures the mTLS <see cref="Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationOptions"/>
    /// so that a PINNED self-signed client cert is accepted: trust comes from the
    /// thumbprint pin (OnCertificateValidated), not a system CA chain.
    ///
    /// WHY a self-signed client cert is accepted (verified against the ASP.NET Core 10
    /// CertificateAuthenticationHandler source — get the attribution right so a future
    /// cleanup does not silently reinstate the "untrusted root" rejection):
    /// <list type="bullet">
    /// <item><b>Load-bearing:</b> <c>AllowedCertificateTypes = All</c>. The default
    /// (<c>Chained</c>) rejects a self-signed cert with <c>NoSelfSigned</c> BEFORE any
    /// chain is built. This is the option that lets the self-signed cert through at all.</item>
    /// <item><b>Load-bearing:</b> <c>ValidateCertificateUse = false</c>. The default
    /// (<c>true</c>) injects the clientAuth EKU (1.3.6.1.5.5.7.3.2) into the chain's
    /// ApplicationPolicy; the product's SelfSignedCertFactory cert has no such EKU, so
    /// chain-build would fail. Turning this off keeps the pin as the sole authority.</item>
    /// <item><b>Load-bearing:</b> <c>ValidateValidityPeriod = true</c> still rejects an
    /// expired cert (kept on deliberately).</item>
    /// <item><b>Belt-and-suspenders (near-inert for self-signed):</b>
    /// <c>ChainTrustValidationMode = CustomRootTrust</c> + an EMPTY <c>CustomTrustStore</c>
    /// and <c>RevocationMode = NoCheck</c>. For a self-signed cert the handler takes its
    /// own self-signed branch (sets AllowUnknownCertificateAuthority, forces NoCheck), so
    /// these are effectively no-ops for the certs this targets — but they keep the intent
    /// explicit and also cover a (non-self-signed) CA-issued client cert that shouldn't be
    /// chained to a Windows system root. Do NOT remove the two load-bearing options above
    /// believing these handle it; they do not.</item>
    /// </list>
    /// Extracted as a pure seam so the wiring is unit-testable (ChainTrustModeTests);
    /// the full TLS handshake is validated by the elevated mTLS smoke test
    /// (scripts/mtls-positive.ps1) since TestServer/WebApplicationFactory bypass Kestrel TLS.
    /// The thumbprint pin (OnCertificateValidated) is applied separately and remains the
    /// deciding, fail-closed check.
    /// </summary>
    public static void ApplyThumbprintPinChainTrust(
        Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationOptions options)
    {
        options.AllowedCertificateTypes = Microsoft.AspNetCore.Authentication.Certificate.CertificateTypes.All;
        options.ChainTrustValidationMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust;
        options.CustomTrustStore = new System.Security.Cryptography.X509Certificates.X509Certificate2Collection();
        options.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;
        options.ValidateCertificateUse = false;
        options.ValidateValidityPeriod = true;
    }

    private static bool RequireHttps()
    {
        var flag = Environment.GetEnvironmentVariable("GAMINGCENTER_REQUIRE_HTTPS");
        return string.Equals(flag, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }
}
