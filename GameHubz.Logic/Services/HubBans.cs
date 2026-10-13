namespace GameHubz.Logic.Services
{
    /// <summary>
    /// A player banned from a hub plays in none of its tournaments. Shared by every door into one:
    /// a solo sign-up and its approval (TournamentRegistrationService), registering a team and
    /// approving that registration, creating, joining, asking to join or being approved into a team
    /// (TournamentTeamService), and the organizer's participant swap (TournamentParticipantService) —
    /// which places only hub members, but a manager can add a banned user back as a member.
    ///
    /// Players already in a tournament when they are banned stay in it — removing them is the
    /// organizer's call, not a side effect of the ban.
    /// </summary>
    public static class HubBans
    {
        /// <summary>The caller is entering on their own: "you are banned from this hub".</summary>
        public static async Task EnsureCallerNotBannedAsync(
            IAppUnitOfWork unitOfWork,
            ILocalizationService localization,
            Guid? hubId,
            Guid userId)
        {
            if (!hubId.HasValue) return;

            if (await unitOfWork.UserHubBanRepository.IsBanned(userId, hubId.Value))
            {
                throw new BusinessRuleException(localization["BusinessRule.YouAreBanned"]);
            }
        }

        /// <summary>
        /// Someone else is being let in — an approval, or a team's roster: names the banned player so
        /// the organizer or captain knows who stands in the way.
        /// </summary>
        public static async Task EnsureNoneBannedAsync(
            IAppUnitOfWork unitOfWork,
            ILocalizationService localization,
            Guid? hubId,
            IReadOnlyCollection<Guid> userIds)
        {
            if (!hubId.HasValue || userIds.Count == 0) return;

            var banned = await unitOfWork.UserHubBanRepository.GetBannedUserIds(hubId.Value, userIds);
            if (banned.Count == 0) return;

            var user = await unitOfWork.UserRepository.ShallowGetById(banned[0]);
            string name = string.IsNullOrWhiteSpace(user?.Username)
                ? localization["BusinessRule.ThatPlayerFallback"]
                : user!.Username;

            throw new BusinessRuleException(string.Format(localization["BusinessRule.UserBannedFromHub"], name));
        }

        /// <summary>Every member of the team, captain included.</summary>
        public static async Task EnsureTeamNotBannedAsync(
            IAppUnitOfWork unitOfWork,
            ILocalizationService localization,
            Guid? hubId,
            Guid teamId)
        {
            if (!hubId.HasValue) return;

            var members = await unitOfWork.TournamentTeamMemberRepository.GetByTeamId(teamId);
            var userIds = members
                .Where(m => m.UserId.HasValue)
                .Select(m => m.UserId!.Value)
                .ToList();

            await EnsureNoneBannedAsync(unitOfWork, localization, hubId, userIds);
        }
    }
}
