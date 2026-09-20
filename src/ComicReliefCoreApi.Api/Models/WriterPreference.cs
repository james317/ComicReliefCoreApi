namespace ComicReliefCoreApi.Api.Models;

public enum WriterPreferenceType
{
    Favorite,
    Avoid,
}

/// <summary>
/// One tracked writer preference - a name is either Favorite or Avoid, never both at once
/// (NormalizedName is unique; re-adding an existing name with a different type flips it
/// rather than creating a second row). Matched against DcbsListingItem.CreatorsAndDescription
/// via CreatorCreditParser, which pulls out only the "(W)" writer credit(s) - artists/cover
/// artists are deliberately out of scope for this feature.
/// </summary>
public class WriterPreference
{
    public int Id { get; set; }

    /// <summary>Display name, as entered.</summary>
    public required string Name { get; set; }

    /// <summary>TitleNormalizer.Normalize(Name) - the same lowercase/punctuation-stripped form used for series titles, reused here since it already does exactly what a name-equality match needs (order/spacing/case-insensitive).</summary>
    public required string NormalizedName { get; set; }

    public WriterPreferenceType Type { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
