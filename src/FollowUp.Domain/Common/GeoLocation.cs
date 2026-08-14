namespace FollowUp.Domain.Common;

/// <summary>A WGS-84 geographic coordinate. Optional on a laboratory (map pin).</summary>
public sealed class GeoLocation : ValueObject
{
    public double Latitude { get; }
    public double Longitude { get; }

    private GeoLocation(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static GeoLocation Create(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90)
            throw new DomainException("Latitude must be between -90 and 90.");
        if (longitude is < -180 or > 180)
            throw new DomainException("Longitude must be between -180 and 180.");
        return new GeoLocation(latitude, longitude);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }
}
