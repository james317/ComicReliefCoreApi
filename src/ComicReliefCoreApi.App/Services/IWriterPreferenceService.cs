using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.App.Services;

public interface IWriterPreferenceService
{
    /// <summary>
    /// Adds a writer, or - if the normalized name already exists - flips its existing Type
    /// instead of creating a second row (a name is Favorite or Avoid, never both). This is
    /// the only way to change a writer's type; there's no separate edit endpoint.
    /// </summary>
    Task<WriterPreference> UpsertAsync(string name, WriterPreferenceType type, CancellationToken ct = default);

    Task<IReadOnlyList<WriterPreference>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns false if no entry with that id exists.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
