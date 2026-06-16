namespace GameHubz.Logic.Interfaces
{
    public interface IAppUnitOfWork : IDisposable, IUnitOfWork
    {
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

        // DO NOT DELETE - Generated Repository Tag
    }
}