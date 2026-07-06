using Godot;

namespace Voxelerator.App;

// Host shim: builds the whole UI in code (zero editor-authored scenes beyond
// the root), hands live-panel composition to EvaLuate. Scaffold stub — the
// real shell lands with the library screen.
public partial class AppHost : Node
{
    public override void _Ready()
    {
        GD.Print("[voxelerator] boot");
    }
}
