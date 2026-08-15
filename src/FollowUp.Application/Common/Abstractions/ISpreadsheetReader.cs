namespace FollowUp.Application.Common.Abstractions;

/// <summary>
/// Parses an uploaded <c>.xlsx</c> workbook into rows of string cells keyed by header (SRS FR-13/FR-14 imports).
/// The hand-written reader lives in Infrastructure; the application maps rows to domain upserts. Format
/// parsing is transport, not business logic, so it stays out of the handler beyond mapping.
/// </summary>
public interface ISpreadsheetReader
{
    IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(byte[] content);
}
