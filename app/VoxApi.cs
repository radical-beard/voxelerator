namespace Voxelerator.App;

/// The `vox` capability api exposed to EvaLuate .evt scripts — the same
/// convention as Neo Terrestria's `sim` api: primitives in, strings out.
/// Panels can read editor state but never reach past this surface.
public sealed class VoxApi(AppHost host)
{
    public string version() => "voxelerator 0.1.0";

    /// One-line editor status for the shell strip.
    public string status() => host.StatusFn();

    /// Context-sensitive hint line (hotkeys for the active view).
    public string hint() => host.HintFn();
}
