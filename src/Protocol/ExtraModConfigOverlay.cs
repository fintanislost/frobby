namespace SdvTestFramework.Protocol;

public sealed record ExtraModConfigOverlay(
    string SourcePath,
    string TargetModUniqueId,
    string TargetRelativePath);
