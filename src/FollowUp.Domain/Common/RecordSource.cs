namespace FollowUp.Domain.Common;

/// <summary>
/// Origin of a record that can be mirrored from Oracle (SRS FR-17). Oracle-sourced rows are added/updated/
/// deactivated by the sync to match the upstream snapshot; Manual rows are entered in the app and are never
/// removed or overwritten by the sync.
/// </summary>
public enum RecordSource
{
    Manual = 0,
    Oracle = 1,
}
