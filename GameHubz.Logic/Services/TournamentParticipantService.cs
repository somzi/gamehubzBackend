using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentParticipantService : AppBaseServiceGeneric<TournamentParticipantEntity, TournamentParticipantDto, TournamentParticipantPost, TournamentParticipantEdit>
    {
        public TournamentParticipantService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentParticipantEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
        }

        protected override IRepository<TournamentParticipantEntity> GetRepository()
            => this.AppUnitOfWork.TournamentParticipantRepository;

        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            if (tournament.IsTeamTournament)
            {
                var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetByTournamentId(tournamentId);

                return teams.Select(team => new TournamentParticipantOverview
                {
                    Username = team.TeamName,
                    AvatarUrl = team.CaptainUser?.AvatarUrl,
                    UserId = team.CaptainUserId ?? Guid.Empty,
                    IsTeamTournament = true,
                    TeamId = team.Id,
                    TeamName = team.TeamName,
                    CaptainUserId = team.CaptainUserId,
                    MemberCount = team.Members.Count,
                    TeamSize = tournament.TeamSize,
                    Members = team.Members.Select(member => new TournamentParticipantMemberOverview
                    {
                        UserId = member.UserId ?? Guid.Empty,
                        Username = member.User?.Username ?? string.Empty,
                        AvatarUrl = member.User?.AvatarUrl
                    }).ToList()
                }).ToList();
            }

            var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTournamentId(tournamentId);

            return participants ?? new List<TournamentParticipantOverview>();
        }

        public async Task RemoveUser(Guid tournamentId, Guid userId)
        {
            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetUserByTournamentId(tournamentId, userId);

            var registration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetUserByTournamentId(tournamentId, userId);

            await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);
            await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);

            await this.SaveAsync();
        }
    }
}