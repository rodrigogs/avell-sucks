using System;

namespace AvellSucks.UI.Controls;

/// <summary>
/// A re-entrant "we're hydrating, don't actuate" flag for views. During
/// construction / initial load / programmatic control updates, event handlers
/// fire but must NOT write to hardware; they early-out on <see cref="Active"/>.
///
/// <see cref="Begin"/> returns a scope that sets the flag and, on dispose,
/// restores the PREVIOUS value (not just false) — so nested Begins compose
/// correctly and replace the manual <c>var wasLoading = _loading; … = wasLoading;</c>
/// save/restore dance.
///
/// <code>
/// if (_loading.Active) return;      // handler bailout
/// using (_loading.Begin()) { … }    // suppress writes while updating controls
/// </code>
/// </summary>
public sealed class LoadingGate
{
    /// <summary>True while a load/hydration scope is active; handlers should not actuate.</summary>
    public bool Active { get; private set; }

    /// <param name="startActive">Start suppressed (true) until the first scope ends,
    /// for views that hydrate in their constructor.</param>
    public LoadingGate(bool startActive = false) => Active = startActive;

    /// <summary>Enter a suppression scope; disposing restores the prior state.</summary>
    public Scope Begin() => new(this);

    /// <summary>Clear the flag directly (for constructors that set startActive and finish loading once).</summary>
    public void End() => Active = false;

    public readonly struct Scope : IDisposable
    {
        private readonly LoadingGate _gate;
        private readonly bool _previous;

        internal Scope(LoadingGate gate) { _gate = gate; _previous = gate.Active; gate.Active = true; }

        public void Dispose() => _gate.Active = _previous;
    }
}
