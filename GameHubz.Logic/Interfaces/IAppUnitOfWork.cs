namespace GameHubz.Logic.Interfaces
{
    public interface IAppUnitOfWork : IDisposable, IUnitOfWork
    {
        /// <summary>
        /// Runs related repository writes on this unit of work in one database transaction.
        /// The transaction is committed only after the complete operation succeeds.
        /// </summary>
        Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation);

        IUserRepository UserRepository { get; }

        IHubRepository HubRepository { get; }

        IUserRoleRepository UserRoleRepository { get; }

        IRefreshTokenRepository RefreshTokenRepository { get; }

        IAssetRepository AssetRepository { get; }

        IEmailQueueRepository EmailQueueRepository { get; }

        //***********************************************
        //********** GENERATED **************************
        //***********************************************

        IUserHubRepository UserHubRepository { get; }
        ITournamentRepository TournamentRepository { get; }
        ITournamentRegistrationRepository TournamentRegistrationRepository { get; }
        IMatchRepository MatchRepository { get; }

        IUserSocialRepository UserSocialRepository { get; }

        ITournamentStageRepository TournamentStageRepository { get; }
        ITournamentGroupRepository TournamentGroupRepository { get; }
        ITournamentParticipantRepository TournamentParticipantRepository { get; }

        IHubActivityRepository HubActivityRepository { get; }

        IMatchEvidenceRepository MatchEvidenceRepository { get; }

        IHubSocialRepository HubSocialRepository { get; }

        IMatchChatRepository MatchChatRepository { get; }

        IMatchChatReadRepository MatchChatReadRepository { get; }

        IMatchStreamRepository MatchStreamRepository { get; }

        ITournamentTeamRepository TournamentTeamRepository { get; }
        ITournamentTeamMemberRepository TournamentTeamMemberRepository { get; }
        ITeamJoinRequestRepository TeamJoinRequestRepository { get; }
        ITeamMatchRepository TeamMatchRepository { get; }
        IUserHubRequestRepository UserHubRequestRepository { get; }

        IUserHubBanRepository UserHubBanRepository { get; }

        IHubVerificationRequestRepository HubVerificationRequestRepository { get; }

        IFriendshipRepository FriendshipRepository { get; }

        IFriendRequestRepository FriendRequestRepository { get; }

        IDirectChatRepository DirectChatRepository { get; }

        IDirectMessageRepository DirectMessageRepository { get; }

        IUserBlockRepository UserBlockRepository { get; }

        INotificationRepository NotificationRepository { get; }

        // DO NOT DELETE - Generated Repository Tag
    }
}
