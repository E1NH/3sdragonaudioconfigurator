using System.Runtime.InteropServices;

namespace DragonOS.AudioConfigurator.Core.Interop;

// ============================================================================
// P/Invoke declarations and Win32 helper utilities
// ============================================================================

/// <summary>
/// P/Invoke bindings for Win32 functions required by the audio routing pipeline.
/// </summary>
/// <remarks>
/// All methods in this class are <see langword="internal"/> — they are implementation
/// details of the service layer and must not be called from the UI project directly.
/// </remarks>
internal static class NativeInterop
{
    // -------------------------------------------------------------------------
    // Windows Runtime String (HSTRING) lifetime management
    //
    // IAudioPolicyConfigFactory uses HSTRING (Windows Runtime string handles) for
    // device IDs rather than raw LPWSTR. HSTRING is a reference-counted, immutable
    // string primitive from the Windows Runtime. Its lifetime must be managed
    // explicitly: create with WindowsCreateString, release with WindowsDeleteString.
    //
    // Reference: https://learn.microsoft.com/en-us/windows/win32/api/winstring/
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a Windows Runtime string (HSTRING) from a managed string.
    /// Must be paired with a <see cref="WindowsDeleteString"/> call to avoid a handle leak.
    /// </summary>
    /// <param name="sourceString">The source UTF-16 string to encapsulate.</param>
    /// <param name="length">The length of <paramref name="sourceString"/> in characters (not bytes).</param>
    /// <param name="hstring">
    /// Receives the opaque HSTRING handle on success.
    /// The caller owns this handle and must release it via <see cref="WindowsDeleteString"/>.
    /// </param>
    /// <returns>An HRESULT. S_OK (0) on success.</returns>
    [DllImport("combase.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
    internal static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        uint length,
        out IntPtr hstring);

    /// <summary>
    /// Decrements the reference count of an HSTRING handle and releases it when it reaches zero.
    /// Passing <see cref="IntPtr.Zero"/> is a no-op (safe to call unconditionally).
    /// </summary>
    /// <param name="hstring">The HSTRING handle to release.</param>
    /// <returns>An HRESULT. Always S_OK in practice for valid handles.</returns>
    [DllImport("combase.dll", PreserveSig = true)]
    internal static extern int WindowsDeleteString(IntPtr hstring);

    // -------------------------------------------------------------------------
    // HRESULT helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Throws a <see cref="COMException"/> if <paramref name="hresult"/> indicates failure
    /// (i.e. the value is negative in two's-complement signed interpretation).
    /// </summary>
    /// <param name="hresult">The HRESULT value returned by a COM method.</param>
    /// <param name="operationName">
    /// A descriptive label for the COM operation being called, included in the exception
    /// message to aid community debugging.
    /// </param>
    /// <exception cref="COMException">
    /// Thrown when <paramref name="hresult"/> is a failure code.
    /// </exception>
    internal static void ThrowIfFailed(int hresult, string operationName)
    {
        if (hresult < 0)
        {
            // Pass IntPtr(-1) to suppress IErrorInfo lookup — we don't implement
            // IErrorInfo, and attempting to marshal it can produce a secondary exception
            // that masks the original HRESULT.
            throw new COMException(
                $"COM operation '{operationName}' failed with HRESULT 0x{(uint)hresult:X8}. " +
                "Consult winerror.h or https://hresult.info for error details.",
                hresult);
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> if the HRESULT indicates success
    /// (S_OK = 0 or S_FALSE = 1 are both considered successful).
    /// </summary>
    internal static bool Succeeded(int hresult) => hresult >= 0;
}
