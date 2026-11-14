// The renderer consumes snapshots only; no game automation is linked into this native UI runner.
namespace HuntHelper.Managers.Automation;
public enum PatrolState { Stopped, PreparingWorld, ChangingWorld, PreparingMap, Teleporting, PreparingMove, Moving, Scanning, Completed }
