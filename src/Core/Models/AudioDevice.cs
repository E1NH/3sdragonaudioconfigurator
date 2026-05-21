namespace DragonOS.AudioConfigurator.Core.Models;

/// <summary>
/// An immutable data-transfer object representing a Windows audio endpoint device
/// as discovered by <c>IMMDeviceEnumerator</c>.
/// </summary>
/// <param name="Id">
/// The opaque Windows endpoint ID string (e.g.
/// <c>{0.0.0.00000000}.{guid}</c>). This is the value passed to
/// <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c> and must be
/// obtained from <c>IMMDevice::GetId</c> — never constructed by hand.
/// </param>
/// <param name="FriendlyName">
/// The human-readable device name as reported by Windows (e.g. "CABLE Input (VB-Audio Virtual Cable)").
/// </param>
/// <param name="IsDefault">
/// <see langword="true"/> if this device is currently the system-wide default render endpoint.
/// </param>
/// <remarks>
/// The record equality semantics mean two <see cref="AudioDevice"/> instances with
/// identical <paramref name="Id"/> values are considered equal, which is the correct
/// semantic for Windows audio endpoint identity.
/// </remarks>
public sealed record AudioDevice(string Id, string FriendlyName, bool IsDefault)
{
    /// <inheritdoc/>
    public override string ToString() => FriendlyName;
}
