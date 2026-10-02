// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Runtime.InteropServices;
using System.Text;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// EXPERIMENTAL (rama experimental/d3dimage). Compila HLSL en tiempo de ejecucion con
/// d3dcompiler_47.dll, que Windows 10/11 trae de fabrica en System32: asi no hace falta ni una
/// dependencia NuGet mas ni guardar bytecode precompilado ilegible en el repositorio.
/// </summary>
internal static class D3DShaderCompiler
{
    /// <summary>Devuelve el bytecode o lanza con el mensaje del compilador.</summary>
    public static byte[] Compile(string source, string entryPoint, string profile)
    {
        var src = Encoding.ASCII.GetBytes(source);
        var hr = D3DCompile(src, src.Length, "inline", IntPtr.Zero, IntPtr.Zero, entryPoint, profile,
            D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, out var code, out var errors);

        try
        {
            if (hr < 0 || code == IntPtr.Zero)
            {
                var message = errors != IntPtr.Zero
                    ? Marshal.PtrToStringAnsi(BlobPointer(errors)) ?? ""
                    : "";
                throw new InvalidOperationException($"D3DCompile fallo (0x{hr:X8}): {message}");
            }

            var bytes = new byte[(int)BlobSize(code)];
            Marshal.Copy(BlobPointer(code), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (code != IntPtr.Zero)
                Marshal.Release(code);
            if (errors != IntPtr.Zero)
                Marshal.Release(errors);
        }
    }

    // ID3DBlob se lee por su vtable a mano (IUnknown ocupa los slots 0-2): evita depender del
    // marshalling COM integrado, y son solo dos metodos.
    private static unsafe IntPtr BlobPointer(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr>)(*(IntPtr**)blob)[3])(blob);

    private static unsafe nint BlobSize(IntPtr blob) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, nint>)(*(IntPtr**)blob)[4])(blob);

    private const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1 << 15;

    [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int D3DCompile(byte[] srcData, nint srcDataSize, string sourceName,
        IntPtr defines, IntPtr include, string entryPoint, string target, uint flags1, uint flags2,
        out IntPtr code, out IntPtr errorMsgs);
}
