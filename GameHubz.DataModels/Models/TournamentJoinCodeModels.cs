namespace GameHubz.DataModels.Models
{
    /// <summary>
    /// "Join with code" request. The code travels in the body rather than the URL so it never lands
    /// in access logs. Spaces and dashes are tolerated ("123 456", "123-456").
    /// </summary>
    public class JoinTournamentByCodeRequest
    {
        public string? Code { get; set; }
    }

    /// <summary>
    /// A private tournament's invite code as its organisers see it. <see cref="JoinCode"/> is null
    /// when the tournament is not private.
    /// </summary>
    public class TournamentJoinCodeDto
    {
        public bool IsPrivate { get; set; }

        public string? JoinCode { get; set; }
    }
}
