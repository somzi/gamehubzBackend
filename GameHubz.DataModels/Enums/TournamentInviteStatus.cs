namespace GameHubz.DataModels.Enums
{
    /// <summary>
    /// Where a tournament invite stands. Only <see cref="Pending"/> can still be answered; every other
    /// value is final. A pending entry invite whose tournament has stopped taking players is shown as
    /// <see cref="Expired"/> on read (see TournamentInviteRules.EffectiveStatus) without being rewritten.
    /// </summary>
    public enum TournamentInviteStatus
    {
        Pending = 1,
        Accepted = 2,
        Declined = 3,
        Cancelled = 4,
        Expired = 5,
    }
}
