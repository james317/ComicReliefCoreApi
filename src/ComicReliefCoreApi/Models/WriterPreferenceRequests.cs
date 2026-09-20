using ComicReliefCoreApi.Api.Models;

namespace ComicReliefCoreApi.Models;

public record UpsertWriterPreferenceRequest(string Name, WriterPreferenceType Type);

public record WriterPreferenceResponse(int Id, string Name, WriterPreferenceType Type, DateTime CreatedAt)
{
    public static WriterPreferenceResponse FromEntity(WriterPreference entity) => new(
        entity.Id, entity.Name, entity.Type, entity.CreatedAt);
}
