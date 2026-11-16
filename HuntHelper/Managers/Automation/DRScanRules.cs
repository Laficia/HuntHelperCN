namespace HuntHelper.Managers.Automation;

/// <summary>Unchanged DR scanner crystal exclusions; no Ruby Sea height overrides.</summary>
public static class DRScanRules
{
    // AutoMarksFinder.Scanner.cs, commit 292258453537f62e9cd45f4d5c94d33775cc1367.
    public static bool CrystalAllowed(uint id) => id is not (105 or 148 or 173 or 175 or 181 or 201);
}
