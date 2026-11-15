using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using WeaponLab.Core.Viewport;

namespace WeaponLab.App;

/// <summary>A stretch of a clip in frames, played from <paramref name="From"/> to <paramref name="To"/>.</summary>
public readonly record struct PlaybackSegment(double From, double To);

/// <summary>
/// The Animations tab's 3D view: the weapon model shaded, its skeleton drawn
/// on top (a custom skeleton's new bones in amber, the selected bone in
/// white), an orbit camera (left drag turns, right or middle drag pans, wheel
/// zooms) and click-to-pick bones. World space is the game's model space,
/// Z up, in game units. Draws only when something changed.
/// </summary>
public sealed class WeaponViewport : OpenGlControlBase, Avalonia.Rendering.ICustomHitTest
{
    // GL enums used here (core GL 3.3 and GLES 3.0 share them).
    private const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, StaticDraw = 0x88E4, Float = 0x1406, UnsignedInt = 0x1405;
    private const int Triangles = 4, Lines = 1, Points = 0;
    private const int DepthTest = 0x0B71, ColorBufferBit = 0x4000, DepthBufferBit = 0x0100, LessOrEqual = 0x0203, ProgramPointSize = 0x8642;
    private const int VertexShader = 0x8B31, FragmentShader = 0x8B30;

    private static readonly Vector4 BoneColour = new(0.56f, 0.72f, 0.87f, 1f);
    private static readonly Vector4 ExtraBoneColour = new(0.91f, 0.64f, 0.24f, 1f);
    private static readonly Vector4 SelectedColour = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 ArmBoneColour = new(0.55f, 0.55f, 0.6f, 0.9f);

    private ViewportScene? _scene;
    private bool _sceneDirty;
    private string? _selectedBone;

    private int _meshProgram, _lineProgram;
    private readonly List<GpuMesh> _meshes = [];
    private bool _meshesStale;
    private int _lineVao, _lineBuffer;
    private bool _isEs;
    private unsafe delegate* unmanaged[Stdcall]<int, float, float, float, float, void> _uniform4f;

    // Orbit camera around a target point.
    private Vector3 _target;
    private float _distance = 60, _yaw = -MathF.PI / 2, _pitch = 0.25f;
    private Point? _dragStart;
    private Point _dragLast;
    private bool _dragged;
    private MouseButton _dragButton;

    public bool ShowMesh { get; set => Set(ref field, value); } = true;

    public bool ShowBones { get; set => Set(ref field, value); } = true;

    /// <summary>Show the viewmodel arms (sleeves, gloves and their bones) when the scene has them.</summary>
    public bool ShowArms { get; set { field = value; _sceneDirty = true; RequestNextFrameRendering(); } } = true;

    /// <summary>Raised when a bone is clicked in the view (its name).</summary>
    public event Action<string>? BonePicked;

    /// <summary>Raised after each draw with the selected bone's position on screen (in control pixels), for a label; null when hidden.</summary>
    public event Action<string, Point?>? SelectedBoneMoved;

    public ViewportScene? Scene
    {
        get => _scene;
        set
        {
            var first = _scene is null || value is null || _scene.Skeleton != value.Skeleton || _scene.HasArms != value.HasArms;
            _scene = value;
            _sceneDirty = true;
            _meshesStale = true;
            if (first)
            {
                Frame();
            }

            RequestNextFrameRendering();
        }
    }

    public string? SelectedBone
    {
        get => _selectedBone;
        set
        {
            _selectedBone = value;
            _sceneDirty = true; // Bone colours live in the line buffer.
            RequestNextFrameRendering();
        }
    }

    /// <summary>The whole view takes the mouse (a GL surface has no background to hit by default).</summary>
    public bool HitTest(Point point) => true;

    /// <summary>Points the camera at the whole weapon.</summary>
    public void Frame()
    {
        var points = ScenePoints().ToList();
        if (points.Count == 0)
        {
            return;
        }

        var min = points.Aggregate(Vector3.Min);
        var max = points.Aggregate(Vector3.Max);
        _target = (min + max) / 2;
        _distance = MathF.Max(5, (max - min).Length() * 1.1f);
        RequestNextFrameRendering();
    }

    private void Set(ref bool field, bool value)
    {
        field = value;
        RequestNextFrameRendering();
    }

    private IEnumerable<ViewportMesh> SceneMeshes() =>
        _scene is null ? [] : (_scene.Mesh is { } weapon ? [weapon] : Array.Empty<ViewportMesh>()).Concat(_scene.ArmMeshes);

    private bool Shows(ViewportMesh mesh) => ShowMesh && (!mesh.Arms || ShowArms);

    private bool Shows(ViewportBone bone) => !bone.Arms || ShowArms;

    /// <summary>Points to fit, in view space (see <see cref="World"/>).</summary>
    private IEnumerable<Vector3> ScenePoints()
    {
        foreach (var mesh in SceneMeshes().Where(Shows))
        {
            for (var vertex = 0; vertex < mesh.VertexCount; vertex += 7)
            {
                yield return Vector3.Transform(new Vector3(mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 1], mesh.Positions[vertex * 3 + 2]), World);
            }
        }

        foreach (var bone in (_scene?.Bones ?? []).Where(Shows))
        {
            yield return Vector3.Transform(bone.Rest.Translation, World);
        }
    }

    // ---- Camera ----

    /// <summary>
    /// The viewmodel rig is Y up; a weapon on its own is Z up. With arms the
    /// scene is turned Y up to Z up for viewing (as the Blender build presents it).
    /// </summary>
    private Matrix4x4 World => Matrix4x4.Identity;

    private Vector3 Eye => _target + _distance * new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));

    private Matrix4x4 ViewProjection(double width, double height)
    {
        var view = World * Matrix4x4.CreateLookAt(Eye, _target, Vector3.UnitZ);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, (float)Math.Max(width / Math.Max(height, 1), 0.1), MathF.Max(_distance / 500, 0.01f), _distance * 20);
        return view * projection;
    }

    /// <summary>A world point on screen in control pixels, or null behind the camera.</summary>
    private Point? Project(Vector3 point, Matrix4x4 viewProjection)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), viewProjection);
        if (clip.W <= 0)
        {
            return null;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        return new Point((ndc.X + 1) / 2 * Bounds.Width, (1 - ndc.Y) / 2 * Bounds.Height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        _dragButton = point.Properties.IsLeftButtonPressed ? MouseButton.Left : point.Properties.IsRightButtonPressed ? MouseButton.Right : MouseButton.Middle;
        _dragStart = _dragLast = point.Position;
        _dragged = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _dragLast;
        _dragLast = position;
        if (Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) > 3)
        {
            _dragged = true;
        }

        if (_dragButton == MouseButton.Left)
        {
            _yaw -= (float)delta.X * 0.01f;
            _pitch = Math.Clamp(_pitch + (float)delta.Y * 0.01f, -1.5f, 1.5f);
        }
        else
        {
            // Pan in the view plane, scaled so the point under the cursor roughly follows it.
            var forward = Vector3.Normalize(_target - Eye);
            var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
            var up = Vector3.Cross(right, forward);
            var scale = _distance / (float)Math.Max(Bounds.Height, 1) * 0.83f;
            _target += (-right * (float)delta.X + up * (float)delta.Y) * scale;
        }

        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_dragStart is not null && !_dragged && _dragButton == MouseButton.Left && _scene is { } scene && ShowBones)
        {
            // A click picks the nearest bone joint within a few pixels.
            var viewProjection = ViewProjection(Bounds.Width, Bounds.Height);
            var position = e.GetPosition(this);
            var pose = Pose();
            var nearest = scene.Bones
                .Select((bone, index) => (bone.Name, Screen: Shows(bone) ? Project(pose[index].Translation, viewProjection) : null))
                .Where(item => item.Screen is not null)
                .Select(item => (item.Name, Distance: Point.Distance(item.Screen!.Value, position)))
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (nearest.Name is not null && nearest.Distance < 12)
            {
                BonePicked?.Invoke(nearest.Name);
            }
        }

        _dragStart = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _distance = Math.Clamp(_distance * MathF.Pow(0.88f, (float)e.Delta.Y), 0.5f, 5000);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    // ---- GL ----

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        _isEs = GlVersion.Type == GlProfileType.OpenGLES;
        _uniform4f = (delegate* unmanaged[Stdcall]<int, float, float, float, float, void>)gl.GetProcAddress("glUniform4f");
        _meshProgram = Program(gl,
            """
            in vec3 aPosition;
            in vec3 aNormal;
            uniform mat4 uViewProjection;
            out vec3 vNormal;
            void main() { vNormal = aNormal; gl_Position = uViewProjection * vec4(aPosition, 1.0); }
            """,
            """
            in vec3 vNormal;
            uniform vec4 uColour;
            out vec4 fragColour;
            void main()
            {
                vec3 light = normalize(vec3(0.4, -0.6, 0.7));
                float lit = 0.35 + 0.65 * abs(dot(normalize(vNormal), light));
                fragColour = vec4(uColour.rgb * lit, uColour.a);
            }
            """,
            ["aPosition", "aNormal"]);
        _lineProgram = Program(gl,
            """
            in vec3 aPosition;
            in vec4 aColour;
            uniform mat4 uViewProjection;
            uniform float uPointSize;
            out vec4 vColour;
            void main() { vColour = aColour; gl_PointSize = uPointSize; gl_Position = uViewProjection * vec4(aPosition, 1.0); }
            """,
            """
            in vec4 vColour;
            out vec4 fragColour;
            void main() { fragColour = vColour; }
            """,
            ["aPosition", "aColour"]);
        _lineVao = gl.GenVertexArray();
        _lineBuffer = gl.GenBuffer();
        _sceneDirty = true;
        _meshesStale = true; // A new GL context (the view moved to another tab) has none of the old mesh buffers.
    }

    private int Program(GlInterface gl, string vertex, string fragment, string[] attributes)
    {
        var header = _isEs ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";
        var program = gl.CreateProgram();
        foreach (var (kind, source) in new[] { (VertexShader, vertex), (FragmentShader, fragment) })
        {
            var shader = gl.CreateShader(kind);
            if (gl.CompileShaderAndGetError(shader, header + source) is { Length: > 0 } error)
            {
                throw new InvalidOperationException("Viewport shader: " + error);
            }

            gl.AttachShader(program, shader);
        }

        for (var index = 0; index < attributes.Length; index++)
        {
            gl.BindAttribLocationString(program, index, attributes[index]);
        }

        if (gl.LinkProgramAndGetError(program) is { Length: > 0 } linkError)
        {
            throw new InvalidOperationException("Viewport shader: " + linkError);
        }

        return program;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        DeleteMeshes(gl);
        gl.DeleteBuffer(_lineBuffer);
        gl.DeleteVertexArray(_lineVao);
        gl.DeleteProgram(_meshProgram);
        gl.DeleteProgram(_lineProgram);
    }

    private static unsafe void Upload<T>(GlInterface gl, int target, int buffer, T[] data) where T : unmanaged
    {
        gl.BindBuffer(target, buffer);
        fixed (T* pointer = data)
        {
            gl.BufferData(target, new IntPtr(data.Length * sizeof(T)), new IntPtr(pointer), StaticDraw);
        }
    }

    /// <summary>Bone lines (joint to parent) and joints of the shown bones, as position + colour per vertex: lines first, then points.</summary>
    private (float[] Data, int LineVertices, int PointVertices) BoneGeometry(Matrix4x4[] pose)
    {
        var bones = _scene?.Bones ?? [];
        var data = new List<float>();
        void Add(Vector3 position, Vector4 colour) => data.AddRange([position.X, position.Y, position.Z, colour.X, colour.Y, colour.Z, colour.W]);
        Vector4 ColourOf(ViewportBone bone) => bone.Name == _selectedBone ? SelectedColour : bone.Extra ? ExtraBoneColour : bone.Arms ? ArmBoneColour : BoneColour;

        var lines = 0;
        for (var index = 0; index < bones.Count; index++)
        {
            if (Shows(bones[index]) && bones[index].Parent >= 0 && bones[index].Parent < bones.Count)
            {
                Add(pose[bones[index].Parent].Translation, ColourOf(bones[index]));
                Add(pose[index].Translation, ColourOf(bones[index]));
                lines += 2;
            }
        }

        var points = 0;
        for (var index = 0; index < bones.Count; index++)
        {
            if (Shows(bones[index]))
            {
                Add(pose[index].Translation, ColourOf(bones[index]));
                points++;
            }
        }

        return ([.. data], lines, points);
    }

    private int _lineVertices, _pointVertices;

    // ---- Playback ----

    private ViewportClip? _clip;
    private double _frame;
    private bool _playing;
    private long _lastTick;
    private int _posedFrame = -1;

    private IReadOnlyList<PlaybackSegment>? _plan;
    private int _segment;

    /// <summary>Raised (on the UI thread) as playback moves, with the current frame.</summary>
    public event Action<double>? FrameChanged;

    /// <summary>Raised (on the UI thread) with the stretches of the clip playback just went through, in order, so events in them can fire.</summary>
    public event Action<IReadOnlyList<PlaybackSegment>>? Played;

    /// <summary>The clip shown; null shows the rest pose.</summary>
    public ViewportClip? Animation
    {
        get => _clip;
        set
        {
            _clip = value;
            _frame = 0;
            _segment = 0;
            _posedFrame = -1;
            _playing = _playing && value is not null;
            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// The stretches of the clip to play in order, then from the first again
    /// (a shell reload's intro, its loop N times, its outro); null plays the
    /// whole clip on a loop.
    /// </summary>
    public IReadOnlyList<PlaybackSegment>? Plan
    {
        get => _plan;
        set
        {
            _plan = value is { Count: > 0 } ? value : null;
            _segment = 0;
            if (_plan is not null)
            {
                _frame = _plan[0].From;
                RequestNextFrameRendering();
            }
        }
    }

    /// <summary>The frame shown (fractions are dropped when posing).</summary>
    public double CurrentFrame
    {
        get => _frame;
        set
        {
            _frame = _clip is null ? 0 : Math.Clamp(value, 0, Math.Max(0, _clip.FrameCount - 1));
            _segment = Segments().ToList().FindIndex(segment => _frame >= segment.From && _frame <= segment.To) is var found && found >= 0 ? found : 0;
            RequestNextFrameRendering();
        }
    }

    public bool Playing
    {
        get => _playing;
        set
        {
            _playing = value && _clip is not null;
            _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
            RequestNextFrameRendering();
        }
    }

    /// <summary>Playback speed: 1 is the clip's own frame rate.</summary>
    public double Speed { get; set; } = 1;

    /// <summary>Each bone's model-space transform now: the clip's frame, else the rest pose.</summary>
    private Matrix4x4[] Pose()
    {
        if (_clip is { FrameCount: > 0 } clip && _scene is { } scene && clip.Frames[0].Length == scene.Bones.Count)
        {
            return clip.Frames[(int)Math.Clamp(Math.Floor(_frame), 0, clip.FrameCount - 1)];
        }

        return _scene?.RestPose() ?? [];
    }

    private IEnumerable<PlaybackSegment> Segments() =>
        _plan ?? (_clip is { FrameCount: > 1 } clip ? [new PlaybackSegment(0, clip.FrameCount - 1)] : []);

    private void AdvancePlayback()
    {
        if (!_playing || _clip is not { FrameCount: > 1 } clip)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var seconds = (now - _lastTick) / (double)System.Diagnostics.Stopwatch.Frequency;
        _lastTick = now;

        // Walk the plan: to the end of the current stretch, then jump to the next one's start.
        var segments = Segments().ToList();
        var left = Math.Min(seconds * clip.FramesPerSecond * Speed, clip.FrameCount); // A long stall plays at most one clip's worth.
        var played = new List<PlaybackSegment>();
        _segment = Math.Clamp(_segment, 0, Math.Max(0, segments.Count - 1));
        for (var jumps = 0; left > 1e-9 && segments.Count > 0 && jumps < 64; jumps++) // Bounded: a plan of empty stretches plays nothing.
        {
            var segment = segments[_segment];
            var step = Math.Min(left, segment.To - _frame);
            if (step > 0)
            {
                played.Add(new PlaybackSegment(_frame, _frame + step));
                _frame += step;
                left -= step;
            }

            if (_frame >= segment.To - 1e-9)
            {
                _segment = (_segment + 1) % segments.Count;
                _frame = segments[_segment].From;
            }
        }

        var frame = _frame;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Played?.Invoke(played);
            FrameChanged?.Invoke(frame);
        });
    }

    /// <summary>A mesh on the GPU: its vertex array and buffers, and room for its skinned vertices.</summary>
    private sealed class GpuMesh(ViewportMesh source, int vao, int positions, int normals, int indices)
    {
        public ViewportMesh Source { get; } = source;
        public int Vao { get; } = vao;
        public int Positions { get; } = positions;
        public int Normals { get; } = normals;
        public int Indices { get; } = indices;
        public float[] SkinnedPositions { get; } = new float[source.Positions.Length];
        public float[] SkinnedNormals { get; } = new float[source.Normals.Length];
    }

    private void DeleteMeshes(GlInterface gl)
    {
        foreach (var mesh in _meshes)
        {
            gl.DeleteBuffer(mesh.Positions);
            gl.DeleteBuffer(mesh.Normals);
            gl.DeleteBuffer(mesh.Indices);
            gl.DeleteVertexArray(mesh.Vao);
        }

        _meshes.Clear();
    }

    /// <summary>
    /// Moves a mesh to a pose on the CPU: each vertex is the weighted sum of
    /// its joints' transforms (joint bind⁻¹ × bone now) applied to its rest position.
    /// </summary>
    private static void Skin(GpuMesh target, Matrix4x4[] pose)
    {
        var mesh = target.Source;
        var skin = mesh.SkinMatrices(pose);

        for (var vertex = 0; vertex < mesh.VertexCount; vertex++)
        {
            var position = new Vector3(mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 1], mesh.Positions[vertex * 3 + 2]);
            var normal = new Vector3(mesh.Normals[vertex * 3], mesh.Normals[vertex * 3 + 1], mesh.Normals[vertex * 3 + 2]);
            Vector3 placed = default, turned = default;
            for (var slot = 0; slot < 4; slot++)
            {
                var weight = mesh.Weights[vertex * 4 + slot];
                if (weight <= 0)
                {
                    continue;
                }

                var matrix = skin[mesh.Bones[vertex * 4 + slot]];
                placed += Vector3.Transform(position, matrix) * weight;
                turned += Vector3.TransformNormal(normal, matrix) * weight;
            }

            (target.SkinnedPositions[vertex * 3], target.SkinnedPositions[vertex * 3 + 1], target.SkinnedPositions[vertex * 3 + 2]) = (placed.X, placed.Y, placed.Z);
            (target.SkinnedNormals[vertex * 3], target.SkinnedNormals[vertex * 3 + 1], target.SkinnedNormals[vertex * 3 + 2]) = (turned.X, turned.Y, turned.Z);
        }
    }

    private unsafe void UploadScene(GlInterface gl)
    {
        var pose = Pose();
        var posed = pose.Length > 0; // Skinned at rest too: each joint snaps onto its bone, as in game.
        _sceneDirty = false;
        _posedFrame = (int)Math.Floor(_frame);
        if (_meshesStale)
        {
            // A new scene: fresh buffers per mesh, indices uploaded once.
            DeleteMeshes(gl);
            foreach (var source in SceneMeshes().Where(mesh => mesh.Indices.Length > 0))
            {
                var mesh = new GpuMesh(source, gl.GenVertexArray(), gl.GenBuffer(), gl.GenBuffer(), gl.GenBuffer());
                gl.BindVertexArray(mesh.Vao);
                Upload(gl, ElementArrayBuffer, mesh.Indices, source.Indices);
                _meshes.Add(mesh);
            }

            _meshesStale = false;
        }

        var skin = posed ? pose : null;
        foreach (var mesh in _meshes)
        {
            if (skin is not null)
            {
                Skin(mesh, skin);
            }

            gl.BindVertexArray(mesh.Vao);
            Upload(gl, ArrayBuffer, mesh.Positions, skin is not null ? mesh.SkinnedPositions : mesh.Source.Positions);
            gl.VertexAttribPointer(0, 3, Float, 0, 0, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);
            Upload(gl, ArrayBuffer, mesh.Normals, skin is not null ? mesh.SkinnedNormals : mesh.Source.Normals);
            gl.VertexAttribPointer(1, 3, Float, 0, 0, IntPtr.Zero);
            gl.EnableVertexAttribArray(1);
            gl.BindBuffer(ElementArrayBuffer, mesh.Indices);
        }

        var (data, lines, points) = BoneGeometry(pose);
        gl.BindVertexArray(_lineVao);
        Upload(gl, ArrayBuffer, _lineBuffer, data);
        gl.VertexAttribPointer(0, 3, Float, 0, 7 * sizeof(float), IntPtr.Zero);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 4, Float, 0, 7 * sizeof(float), new IntPtr(3 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        (_lineVertices, _pointVertices) = (lines, points);
        gl.BindVertexArray(0);
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        var scaling = Avalonia.Controls.TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var (width, height) = ((int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling));
        gl.BindFramebuffer(0x8D40, framebuffer);
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0.105f, 0.105f, 0.115f, 1);
        gl.ClearDepth(1);
        gl.Clear(ColorBufferBit | DepthBufferBit);
        AdvancePlayback();
        if (_sceneDirty || (int)Math.Floor(_frame) != _posedFrame)
        {
            UploadScene(gl);
        }

        var viewProjection = ViewProjection(Bounds.Width, Bounds.Height);
        var shown = _meshes.Where(mesh => Shows(mesh.Source)).ToList();
        if (shown.Count > 0)
        {
            gl.Enable(DepthTest);
            gl.DepthFunc(LessOrEqual);
            gl.UseProgram(_meshProgram);
            gl.UniformMatrix4fv(gl.GetUniformLocationString(_meshProgram, "uViewProjection"), 1, false, &viewProjection);
            var colour = gl.GetUniformLocationString(_meshProgram, "uColour");
            foreach (var mesh in shown)
            {
                // The weapon grey, the arms a warmer tone so they read apart.
                if (mesh.Source.Arms)
                {
                    _uniform4f(colour, 0.55f, 0.5f, 0.45f, 1f);
                }
                else
                {
                    _uniform4f(colour, 0.62f, 0.63f, 0.66f, 1f);
                }

                gl.BindVertexArray(mesh.Vao);
                gl.DrawElements(Triangles, mesh.Source.Indices.Length, UnsignedInt, IntPtr.Zero);
            }
        }

        if (ShowBones && _pointVertices > 0)
        {
            // Bones on top of the mesh, so they can always be seen and picked.
            gl.Disable(DepthTest);
            if (!_isEs)
            {
                gl.Enable(ProgramPointSize);
            }

            gl.UseProgram(_lineProgram);
            gl.UniformMatrix4fv(gl.GetUniformLocationString(_lineProgram, "uViewProjection"), 1, false, &viewProjection);
            gl.Uniform1f(gl.GetUniformLocationString(_lineProgram, "uPointSize"), (float)(6 * scaling));
            gl.BindVertexArray(_lineVao);
            gl.DrawArrays(Lines, 0, new IntPtr(_lineVertices));
            gl.DrawArrays(Points, _lineVertices, new IntPtr(_pointVertices));
        }

        gl.BindVertexArray(0);
        gl.UseProgram(0);

        // Where the selected bone is on screen, for its label.
        var index = _selectedBone is null || _scene is null ? -1 : _scene.Bones.ToList().FindIndex(bone => bone.Name == _selectedBone);
        if (index >= 0 && _selectedBone is { } selected)
        {
            var screen = ShowBones ? Project(Pose()[index].Translation, viewProjection) : null;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedBoneMoved?.Invoke(selected, screen));
        }

        if (_playing)
        {
            RequestNextFrameRendering();
        }
    }
}
