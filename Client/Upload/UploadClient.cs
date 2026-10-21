namespace AionDPS.Upload;

/// <summary>
/// The upload to the community backend (aiondps.com, Skeeve's site) is switched off for good in this version (Stacy,
/// 2026-10-21): nothing leaves the PC. The methods stay so the code that builds an upload still compiles, but they send
/// nothing - there is no HttpClient and no address here any more.
/// </summary>
public static class UploadClient
{
    public const string Disabled = "upload disabled";

    public static Task<UploadResult> SendAsync(EncounterUploadRequest payload) => Task.FromResult(UploadResult.Failed(Disabled));

    public static Task<UploadResult> SendProfilesAsync(ProfilesUploadRequest payload) => Task.FromResult(UploadResult.Failed(Disabled));
}
