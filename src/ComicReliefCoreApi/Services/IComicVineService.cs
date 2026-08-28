using ComicReliefCoreApi.Models;

namespace ComicReliefCoreApi.Services;

public interface IComicVineService
{
    Task<UpcomingComicsResponse> GetIssuesShippingInAsync(
        DateOnly rangeStart,
        DateOnly rangeEnd,
        CancellationToken cancellationToken);
}
