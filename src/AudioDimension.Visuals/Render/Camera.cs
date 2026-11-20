using AudioDimension.Visuals.Config;
using OpenTK.Mathematics;

namespace AudioDimension.Visuals.Render;

public sealed class Camera
{
    public Matrix4 View { get; private set; } = Matrix4.Identity;
    public Matrix4 Projection { get; private set; } = Matrix4.Identity;
    public Matrix4 ViewProj { get; private set; } = Matrix4.Identity;
    public Vector3 Eye { get; private set; }
    public bool IsPerspective { get; private set; }
    public float Distance { get; private set; } = 1;

    /// <summary>2D visualizers use a fixed ortho view (y in -1..1, x in -aspect..aspect); 3D ones orbit the origin.</summary>
    public void Update(CameraSettings s, float aspect, double time, float bassKick, bool is3D)
    {
        if (!is3D)
        {
            float halfH = 1f / (float)Math.Max(0.2, s.Zoom2D);
            Projection = Matrix4.CreateOrthographicOffCenter(-halfH * aspect, halfH * aspect, -halfH, halfH, -10, 10);
            View = Matrix4.Identity; Eye = new Vector3(0, 0, 5); IsPerspective = false; Distance = 1;
        }
        else
        {
            float dist = (float)Math.Max(0.5, s.Distance) * (1f - (float)s.AudioShake * 0.12f * bassKick);
            float yaw = MathHelper.DegreesToRadians((float)(s.Yaw + s.AutoRotateSpeed * time));
            float pitch = MathHelper.DegreesToRadians((float)Math.Clamp(s.Pitch, -85, 85));
            Eye = new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * dist;
            View = Matrix4.LookAt(Eye, Vector3.Zero, Vector3.UnitY);
            float fov = MathHelper.DegreesToRadians((float)Math.Clamp(s.Fov, 20, 120));
            if (s.Orthographic)
            {
                float hh = dist * MathF.Tan(fov / 2);
                Projection = Matrix4.CreateOrthographicOffCenter(-hh * aspect, hh * aspect, -hh, hh, 0.1f, 400f);
                IsPerspective = false;
            }
            else
            {
                Projection = Matrix4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, 400f);
                IsPerspective = true;
            }
            Distance = dist;
        }
        ViewProj = View * Projection;
    }
}
