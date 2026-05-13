namespace Cove.Core.Entities;

public enum RatingHostType
{
    Scene = 1,
    Image = 2,
    Performer = 3,
    Segment = 4,
    Face = 5,
    Tag = 6,
    Studio = 7,
    Gallery = 8,
    Group = 9,
    Audio = 10,
    Text = 11,
}

public class Rating : BaseEntity
{
    public int UserId { get; set; }
    public RatingHostType HostType { get; set; }
    public int HostId { get; set; }
    public string Aspect { get; set; } = "overall";
    public int Value { get; set; }
}