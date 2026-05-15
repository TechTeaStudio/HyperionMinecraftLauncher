using System;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.OpenGL;
using MinecraftSkinRender.OpenGL;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.Controls.SkinRender;

/// <summary>
/// Adapter that fulfils <see cref="OpenGLApi"/> by routing each call through Avalonia's
/// <see cref="GlInterface"/>. We mostly use the strongly-typed methods on
/// <c>GlInterface</c>; for OpenGL functions Avalonia doesn't expose directly we pull a
/// function pointer via <c>GetProcAddress</c> and call it through a delegate.
/// </summary>
internal sealed unsafe class AvaloniaGlApi : OpenGLApi
{
    private readonly GlInterface _gl;

    // --- delegate types for functions GlInterface doesn't expose ---
    private delegate void GlIntDel(int a);
    private delegate void GlIntIntDel(int a, int b);
    private delegate void GlIntFloatFloatDel(int a, float b, float c);
    private delegate void GlIntIntFloatFloatDel(int a, int b, float c, float d);
    private delegate void GlIntFloatDel(int a, float b);
    private delegate void GlBoolDel(byte b);
    private delegate void GlIntIntIntIntDel(int a, int b, int c, int d);
    private delegate void GlBlitFbDel(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, int mask, int filter);
    private delegate void GlRbStorageMsDel(int target, int samples, int internalformat, int width, int height);
    private delegate void GlTexStorageMsDel(int target, int samples, int internalformat, int width, int height, byte fixedSampleLocations);
    private delegate void GlFbTex2DDel(int target, int attachment, int textarget, int texture, int level);
    private delegate void GlUniformMat4Del(int loc, int count, byte transpose, float* data);
    private delegate void GlIntPtrDel(int a, int* p);
    private delegate void GlIntIntIntPtrDel(int a, int b, int* p);
    private delegate void GlInfoLogDel(int idx, int bufSize, int* length, byte* infoLog);

    // --- bound function pointers ---
    private readonly GlIntDel _cullFace;
    private readonly GlBoolDel _depthMask;
    private readonly GlIntIntDel _blendFunc;
    private readonly GlRbStorageMsDel _renderbufferStorageMultisample;
    private readonly GlIntIntDel _detachShader;
    private readonly GlIntDel _disable;
    private readonly GlIntDel _disableVertexAttribArray;
    private readonly GlIntIntDel _uniform1i;
    private readonly GlTexStorageMsDel _texStorage2DMultisample;
    private readonly GlFbTex2DDel _framebufferTexture2D;
    private readonly GlIntFloatFloatDel _uniform2f;
    private readonly GlIntFloatDel _uniform1f;
    private readonly GlUniformMat4Del _uniformMatrix4fv;
    private readonly GlBlitFbDel _blitFramebuffer;
    private readonly GlIntIntDel _bindRenderbuffer;
    private readonly GlIntIntDel _bindFramebuffer;
    private readonly GlIntIntIntIntDel _framebufferRenderbuffer;
    private readonly GlIntIntDel _stencilOpFallback;  // not used currently
    private readonly GlIntPtrDel _getIntegerv;
    private readonly GlIntIntIntPtrDel _getProgramiv;
    private readonly GlIntIntIntPtrDel _getShaderiv;
    private readonly GlInfoLogDel _getProgramInfoLog;
    private readonly GlInfoLogDel _getShaderInfoLog;

    public AvaloniaGlApi(GlInterface gl)
    {
        _gl = gl;
        _cullFace                       = Bind<GlIntDel>("glCullFace");
        _depthMask                      = Bind<GlBoolDel>("glDepthMask");
        _blendFunc                      = Bind<GlIntIntDel>("glBlendFunc");
        _renderbufferStorageMultisample = Bind<GlRbStorageMsDel>("glRenderbufferStorageMultisample");
        _detachShader                   = Bind<GlIntIntDel>("glDetachShader");
        _disable                        = Bind<GlIntDel>("glDisable");
        _disableVertexAttribArray       = Bind<GlIntDel>("glDisableVertexAttribArray");
        _uniform1i                      = Bind<GlIntIntDel>("glUniform1i");
        _texStorage2DMultisample        = Bind<GlTexStorageMsDel>("glTexStorage2DMultisample");
        _framebufferTexture2D           = Bind<GlFbTex2DDel>("glFramebufferTexture2D");
        _uniform2f                      = Bind<GlIntFloatFloatDel>("glUniform2f");
        _uniform1f                      = Bind<GlIntFloatDel>("glUniform1f");
        _uniformMatrix4fv               = Bind<GlUniformMat4Del>("glUniformMatrix4fv");
        _blitFramebuffer                = Bind<GlBlitFbDel>("glBlitFramebuffer");
        _bindRenderbuffer               = Bind<GlIntIntDel>("glBindRenderbuffer");
        _bindFramebuffer                = Bind<GlIntIntDel>("glBindFramebuffer");
        _framebufferRenderbuffer        = Bind<GlIntIntIntIntDel>("glFramebufferRenderbuffer");
        _stencilOpFallback              = Bind<GlIntIntDel>("glStencilOp");
        _getIntegerv                    = Bind<GlIntPtrDel>("glGetIntegerv");
        _getProgramiv                   = Bind<GlIntIntIntPtrDel>("glGetProgramiv");
        _getShaderiv                    = Bind<GlIntIntIntPtrDel>("glGetShaderiv");
        _getProgramInfoLog              = Bind<GlInfoLogDel>("glGetProgramInfoLog");
        _getShaderInfoLog               = Bind<GlInfoLogDel>("glGetShaderInfoLog");
    }

    private T Bind<T>(string name) where T : Delegate
    {
        var addr = _gl.GetProcAddress(name);
        if (addr == IntPtr.Zero)
            throw new InvalidOperationException($"OpenGL function '{name}' is not available on this driver.");
        return Marshal.GetDelegateForFunctionPointer<T>(addr);
    }

    // ==================== abstract overrides ====================
    public override void ActiveTexture(int bit)                      => _gl.ActiveTexture(bit);
    public override void AttachShader(int a, int b)                  => _gl.AttachShader(a, b);
    public override void BindBuffer(int bit, int index)              => _gl.BindBuffer(bit, index);
    public override void BindFramebuffer(int type, int data)         => _bindFramebuffer(type, data);
    public override void BindRenderbuffer(int target, int rb)        => _bindRenderbuffer(target, rb);
    public override void BindTexture(int bit, int index)             => _gl.BindTexture(bit, index);
    public override void BindVertexArray(int vao)                    => _gl.BindVertexArray(vao);
    public override void BlendFunc(int a, int b)                     => _blendFunc(a, b);
    public override void BlitFramebuffer(int sx0, int sy0, int sx1, int sy1, int dx0, int dy0, int dx1, int dy1, int mask, int filter)
        => _blitFramebuffer(sx0, sy0, sx1, sy1, dx0, dy0, dx1, dy1, mask, filter);
    public override void BufferData(int type, int v1, IntPtr v2, int t1) => _gl.BufferData(type, (IntPtr)v1, v2, t1);
    public override int CheckFramebufferStatus(int target)           => _gl.CheckFramebufferStatus(target);
    public override void Clear(int bit)                              => _gl.Clear(bit);
    public override void ClearColor(float r, float g, float b, float a) => _gl.ClearColor(r, g, b, a);
    public override void ClearDepth(float v)                         => _gl.ClearStencil((int)v); // GlInterface lacks ClearDepthf; use proc fallback below

    // ClearDepth via fp because GlInterface only exposes ClearStencil; redirect to glClearDepthf.
    // We bind it lazily on first use via a static delegate cache.
    private GlIntFloatDel? _clearDepthFn;
    public void ClearDepthFloat(float v)
    {
        _clearDepthFn ??= Bind<GlIntFloatDel>("glClearDepthf");
        _clearDepthFn(0, v);
    }

    public override void CompileShader(int index)                    => _gl.CompileShader(index);
    public override int CreateProgram()                              => _gl.CreateProgram();
    public override int CreateShader(int type)                       => _gl.CreateShader(type);
    public override void CullFace(int mode)                          => _cullFace(mode);
    public override void DeleteBuffer(int buffers)                   => _gl.DeleteBuffer(buffers);
    public override void DeleteFramebuffer(int fb)                   => _gl.DeleteFramebuffer(fb);
    public override void DeleteProgram(int index)                    => _gl.DeleteProgram(index);
    public override void DeleteRenderbuffer(int rb)                  => _gl.DeleteRenderbuffer(rb);
    public override void DeleteShader(int index)                     => _gl.DeleteShader(index);
    public override void DeleteTexture(int data)                     => _gl.DeleteTexture(data);
    public override void DeleteVertexArray(int arrays)               => _gl.DeleteVertexArray(arrays);
    public override void DepthMask(bool flag)                        => _depthMask((byte)(flag ? 1 : 0));
    public override void DetachShader(int index, int data)           => _detachShader(index, data);
    public override void Disable(int bit)                            => _disable(bit);
    public override void DisableVertexAttribArray(int index)         => _disableVertexAttribArray(index);
    public override void DrawArrays(int type, int v1, int v2)        => _gl.DrawArrays(type, v1, v2);
    public override void DrawElements(int type, int count, int t1, IntPtr arr) => _gl.DrawElements(type, count, t1, arr);
    public override void Enable(int bit)                             => _gl.Enable(bit);
    public override void EnableVertexAttribArray(int index)          => _gl.EnableVertexAttribArray(index);
    public override void FramebufferRenderbuffer(int target, int att, int rbTgt, int rb) => _framebufferRenderbuffer(target, att, rbTgt, rb);
    public override void FramebufferTexture2D(int target, int att, int texTgt, int tex, int level) => _framebufferTexture2D(target, att, texTgt, tex, level);
    public override int GenBuffer()                                  => _gl.GenBuffer();
    public override int GenFramebuffer()                             => _gl.GenFramebuffer();
    public override int GenRenderbuffer()                            => _gl.GenRenderbuffer();
    public override int GenTexture()                                 => _gl.GenTexture();
    public override int GenVertexArray()                             => _gl.GenVertexArray();
    public override int GetAttribLocation(int index, string attr)    => _gl.GetAttribLocationString(index, attr);
    public override int GetError()                                   => _gl.GetError();
    public override void GetIntegerv(int bit, out int data)
    {
        int local = 0;
        _getIntegerv(bit, &local);
        data = local;
    }
    public override void GetProgramInfoLog(int index, out string log)
    {
        // Pull info-log length, allocate, copy, then UTF-8-decode.
        int len = 0;
        _getProgramiv(index, 0x8B84 /* GL_INFO_LOG_LENGTH */, &len);
        if (len <= 0) { log = string.Empty; return; }
        var buf = new byte[len];
        fixed (byte* p = buf)
        {
            int written = 0;
            _getProgramInfoLog(index, len, &written, p);
            log = Encoding.UTF8.GetString(p, written);
        }
    }
    public override void GetProgramiv(int index, int type, out int length)
    {
        int local = 0;
        _getProgramiv(index, type, &local);
        length = local;
    }
    public override void GetShaderInfoLog(int index, out string log)
    {
        int len = 0;
        _getShaderiv(index, 0x8B84 /* GL_INFO_LOG_LENGTH */, &len);
        if (len <= 0) { log = string.Empty; return; }
        var buf = new byte[len];
        fixed (byte* p = buf)
        {
            int written = 0;
            _getShaderInfoLog(index, len, &written, p);
            log = Encoding.UTF8.GetString(p, written);
        }
    }
    public override void GetShaderiv(int index, int type, out int data)
    {
        int local = 0;
        _getShaderiv(index, type, &local);
        data = local;
    }
    public override string GetString(int name)                       => _gl.GetString(name) ?? string.Empty;
    public override int GetUniformLocation(int index, string uni)    => _gl.GetUniformLocationString(index, uni);
    public override void LinkProgram(int index)                      => _gl.LinkProgram(index);
    public override void RenderbufferStorage(int target, int ifmt, int w, int h) => _gl.RenderbufferStorage(target, ifmt, w, h);
    public override void RenderbufferStorageMultisample(int target, int samples, int ifmt, int w, int h)
        => _renderbufferStorageMultisample(target, samples, ifmt, w, h);
    public override void ShaderSource(int a, string source)          => _gl.ShaderSourceString(a, source);
    public override void TexImage2D(int type, int a, int t1, int w, int h, int size, int t2, int t3, IntPtr data)
        => _gl.TexImage2D(type, a, t1, w, h, size, t2, t3, data);
    public override void TexParameteri(int a, int b, int c)          => _gl.TexParameteri(a, b, c);
    public override void Uniform1f(int loc, float v)                 => _uniform1f(loc, v);
    public override void Uniform1i(int index, int data)              => _uniform1i(index, data);
    public override void Uniform2f(int v, float w, float h)          => _uniform2f(v, w, h);
    public override void UniformMatrix4fv(int index, int length, bool b, float* data)
        => _uniformMatrix4fv(index, length, (byte)(b ? 1 : 0), data);
    public override void UseProgram(int index)                       => _gl.UseProgram(index);
    public override void VertexAttribPointer(int index, int length, int type, bool b, int size, IntPtr arr)
        => _gl.VertexAttribPointer(index, length, type, (byte)(b ? 1 : 0), size, arr);
    public override void Viewport(int x, int y, int w, int h)        => _gl.Viewport(x, y, w, h);
}
