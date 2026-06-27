namespace GameHubz.DataModels.Enums
{
    public enum HubRole
    {
        HubOwner = 1,
        HubAdmin = 2,
        HubMember = 3,

        // Privilege sits between Admin and Member: an exclusive member can do everything a
        // member can plus join tournaments marked exclusive-only. Numbered 4 (not inserted
        // between Admin and Member) to keep existing persisted role values stable — permission
        // checks are equality-based, never numeric comparisons, so the value order is irrelevant.
        HubExclusive = 4
    }
}
