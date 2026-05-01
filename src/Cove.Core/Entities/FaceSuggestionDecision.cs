namespace Cove.Core.Entities;

public static class FaceSuggestionDecisionValues
{
    public const string Accept = "accept";
    public const string Reject = "reject";
}

public class FaceSuggestionDecision : BaseEntity
{
    public int FaceId { get; set; }
    public int PerformerId { get; set; }
    public int UserId { get; set; }
    public string Decision { get; set; } = FaceSuggestionDecisionValues.Reject;

    public Face? Face { get; set; }
    public Performer? Performer { get; set; }
}