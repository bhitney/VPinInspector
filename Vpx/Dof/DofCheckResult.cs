namespace VPX_Inspector.Vpx.Dof;

/// <summary>
/// The DOF outcome for a single table: whether the check ran, the ROM
/// (cGameName) that was looked up, and whether a matching DOF entry was found.
/// </summary>
public sealed record DofCheckResult(bool Evaluated, string Rom, bool HasDofEntry)
{
    /// <summary>The check ran but found no DOF entry for the table's ROM.</summary>
    public bool IsMissing => Evaluated && !HasDofEntry;

    /// <summary>A skipped result (check disabled or no DOF config available).</summary>
    public static readonly DofCheckResult NotEvaluated =
        new(Evaluated: false, Rom: string.Empty, HasDofEntry: false);
}
