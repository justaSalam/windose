// Frame:
//     var gpu = GpuCompositor.TryCreate(canvas);          // null => no 3D, keep the CPU path
//     gpu.BeginFrame(Color.Teal);
//     for (int i = 0; i < windowsBackToFront.Count; i++)  // bottom window first
//         gpu.Draw(windowsBackToFront[i], i);
//     gpu.EndFrame();                                     // calls canvas.Display()

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Rendering3D;

namespace Windose.Compositing;

/// <summary>What the compositor needs from a window (or wallpaper, or cursor).</summary>
public interface ICompositedSurface
{
    int X { get; }                 // top-left on screen, pixels
    int Y { get; }
    int Width { get; }
    int Height { get; }
    bool Visible { get; }

    /// <summary>At least Width*Height pixels, 0xAARRGGBB. Must be the window's OWN bitmap.</summary>
    int[] Buffer { get; }

    /// <summary>True when Pixels changed since the last ClearDirty(). Moving a window is NOT a change.</summary>
    bool IsDirty { get; }
    void ClearDirty();
}

public sealed class GpuCompositor : IDisposable
{
    /// <summary>Depth step between stacked surfaces. Keep >= 1: the z-buffer is shallow at this distance.</summary>
    public const float ZStep = 2f;

    /// <summary>Frames a surface may go undrawn (hidden/minimized) before its GPU memory is released.</summary>
    public const int ReleaseAfterFrames = 120;

    private sealed class Entry
    {
        public Texture? Texture;
        public Mesh? Mesh;
        public byte[]? Staging;
        public int Width;
        public int Height;
        public int LastFrame;
    }

    private readonly Canvas3D _canvas;
    private readonly Dictionary<ICompositedSurface, Entry> _entries = new();
    private readonly List<ICompositedSurface> _scratch = new();
    private int _frame;
    private int _camW;
    private int _camH;

    private GpuCompositor(Canvas3D canvas)
    {
        _canvas = canvas;
    }

    /// <summary>Returns null unless the display negotiated 3D (real VMware with SVGA3D enabled).</summary>
    public static GpuCompositor? TryCreate(Canvas canvas)
        => canvas is Canvas3D c3d ? new GpuCompositor(c3d) : null;

    public void BeginFrame(Color background)
    {
        _frame++;
        EnsureCamera();
        _canvas.ClearScene(background);
    }

    // Pixel-exact setup: 90 degree vertical FOV with the camera H/2 away from the z=0 plane makes
    // one world unit equal one screen pixel there. +Y is up, so screen y is flipped when placing.
    private void EnsureCamera()
    {
        int w = _canvas.Width;
        int h = _canvas.Height;
        if (w == _camW && h == _camH) return;

        _camW = w;
        _camH = h;
        float d = h * 0.5f;
        _canvas.Camera = new Camera3D(
            new Vector3(w * 0.5f, h * 0.5f, d),
            new Vector3(w * 0.5f, h * 0.5f, 0f),
            Vector3.UnitY,
            90f);
    }

    /// <param name="order">Stacking index; higher draws in front. 0 = bottom (wallpaper).</param>
    public void Draw(ICompositedSurface s, int order)
    {
        if (!s.Visible || s.Width <= 0 || s.Height <= 0) return;

        if (!_entries.TryGetValue(s, out Entry? e))
        {
            e = new Entry();
            _entries[s] = e;
        }
        e.LastFrame = _frame;

        if (e.Mesh == null || s.IsDirty || e.Width != s.Width || e.Height != s.Height)
            Upload(s, e);

        // Stacked surfaces sit at different depths, and perspective would scale the nearer ones.
        // Pre-scale by k = (d - z) / d and pull the position toward the view axis so each surface
        // still lands on exactly (X, Y) at 1:1.
        float d = _camH * 0.5f;
        float z = order * ZStep;
        if (z > d - 1f) z = d - 1f;
        float k = (d - z) / d;

        float cx = _camW * 0.5f;
        float cy = _camH * 0.5f;
        float wx = cx + (s.X - cx) * k;
        float wy = cy + ((_camH - s.Y) - cy) * k;

        Matrix4x4 world = Matrix4x4.CreateScale(k, k, 1f) * Matrix4x4.CreateTranslation(wx, wy, z);
        _canvas.DrawMesh(e.Mesh!, in world);
    }

    // The API has no texture update call, and a mesh binds its texture at creation, so a changed
    // surface gets a new texture + mesh and the old pair is disposed. Moves never come here.
    private void Upload(ICompositedSurface s, Entry e)
    {
        int w = s.Width;
        int h = s.Height;
        int n = w * h;

        if (e.Staging == null || e.Staging.Length != n * 4)
            e.Staging = new byte[n * 4];

        // 0xAARRGGBB ints are B,G,R,A bytes on little-endian = the Bitmap layout. Force alpha to
        // 255 so blend state can never make an opaque window see-through.
        Span<int> dst = MemoryMarshal.Cast<byte, int>(e.Staging.AsSpan());
        ReadOnlySpan<int> src = s.Buffer.AsSpan(0, n);
        for (int i = 0; i < n; i++)
            dst[i] = src[i] | unchecked((int)0xFF000000);

        Texture texture = _canvas.CreateTexture(new Bitmap(w, h, e.Staging, ColorDepth.ColorDepth32));

        // Quad with its top-left at the model origin, extending right (+X) and down (-Y).
        ReadOnlySpan<Vector3> positions = [new(0, 0, 0), new(w, 0, 0), new(w, -h, 0), new(0, -h, 0)];
        ReadOnlySpan<Vector2> uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        Mesh mesh = _canvas.CreateMesh(positions, uvs, texture, indices);

        e.Mesh?.Dispose();
        e.Texture?.Dispose();
        e.Mesh = mesh;
        e.Texture = texture;
        e.Width = w;
        e.Height = h;

        s.ClearDirty();
    }

    /// <summary>Presents the frame and frees GPU memory of surfaces that stopped being drawn.</summary>
    public void EndFrame()
    {
        _scratch.Clear();
        foreach (KeyValuePair<ICompositedSurface, Entry> kv in _entries)
        {
            if (_frame - kv.Value.LastFrame > ReleaseAfterFrames)
                _scratch.Add(kv.Key);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Forget(_scratch[i]);

        _canvas.Display();
    }

    /// <summary>Call when a window closes so its texture is released immediately.</summary>
    public void Forget(ICompositedSurface s)
    {
        if (!_entries.TryGetValue(s, out Entry? e)) return;
        e.Mesh?.Dispose();
        e.Texture?.Dispose();
        _entries.Remove(s);
    }

    public void Dispose()
    {
        _scratch.Clear();
        foreach (KeyValuePair<ICompositedSurface, Entry> kv in _entries)
            _scratch.Add(kv.Key);
        for (int i = 0; i < _scratch.Count; i++)
            Forget(_scratch[i]);
    }
}