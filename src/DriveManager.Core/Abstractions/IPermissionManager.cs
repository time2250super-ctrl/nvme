namespace DriveManager.Core.Abstractions;

public sealed record PermissionItem(string Name, bool Granted, string Detail)
{
    public string Verdict => Granted ? "OK" : "MISSING";
}

/// <summary>
/// Ensures the process has the OS rights this app needs: Administrator elevation,
/// volume-management token privileges, storage WMI, BitLocker, and write access
/// to the portable app folder.
/// </summary>
public interface IPermissionManager
{
    bool IsElevated { get; }

    string Summary { get; }

    IReadOnlyList<PermissionItem> Items { get; }

    /// <summary>Enable privileges, fix ACLs, and re-check. Safe to call more than once.</summary>
    Task ApplyAsync(CancellationToken ct = default);
}
