// Voxel material: face shading is baked into vertex color by the greedy
// mesher (same table as Neo Terrestria), so the mesh reads flat-lit and
// game-parity without any scene lighting. Emissive palette colors carry
// vertex alpha 1 and glow — more as the night slider rises.
shader_type spatial;
render_mode unshaded, cull_back;

uniform float night : hint_range(0.0, 1.0) = 0.0;

varying vec4 vcol;

void vertex() {
    vcol = COLOR;
}

void fragment() {
    vec3 base = vcol.rgb;
    bool emissive = vcol.a > 0.99;
    bool glassy = vcol.a > 0.05 && !emissive;

    float dim = emissive ? 1.0 : 1.0 - 0.68 * night;
    vec3 col = base * dim;
    if (glassy) col = mix(col, vec3(0.75, 0.92, 1.0) * dim, 0.30);

    ALBEDO = col;
    if (emissive) {
        EMISSION = base * (0.25 + 1.6 * night);
    }
}
