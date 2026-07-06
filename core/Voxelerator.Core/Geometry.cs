namespace Voxelerator.Core;

/// Double-precision vector for mesh math (float only at the buffer boundary).
public readonly record struct V3d(double X, double Y, double Z)
{
    public static V3d operator +(V3d a, V3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3d operator -(V3d a, V3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3d operator *(V3d a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double LengthSq => X * X + Y * Y + Z * Z;
    public double Dot(V3d o) => X * o.X + Y * o.Y + Z * o.Z;
    public V3d Cross(V3d o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public V3d Normalized()
    {
        double len = Math.Sqrt(LengthSq);
        return len < 1e-12 ? this : new V3d(X / len, Y / len, Z / len);
    }
}

/// Linear-ish color with the same alpha convention as Neo Terrestria's
/// renderer: A = 0 matte, A = 1 emissive (glows at night), 0 < A < 1 is the
/// alpha of a glass material. The PNG layer files never carry this — it comes
/// from palette tags.
public readonly record struct Rgba(float R, float G, float B, float A = 0f)
{
    public Rgba Scale(float k) => new(R * k, G * k, B * k, A);
    public Rgba Lerp(Rgba o, float t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t, A + (o.A - A) * t);
    public Rgba WithA(float a) => new(R, G, B, a);
}
