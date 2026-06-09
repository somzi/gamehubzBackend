using FluentValidation;

namespace GameHubz.Logic.Services
{
    public class TournamentParticipantService : AppBaseServiceGeneric<TournamentParticipantEntity, TournamentParticipantDto, TournamentParticipantPost, TournamentParticipantEdit>
    {
        private readonly ICacheService cacheService;

        public TournamentParticipantService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<TournamentParticipantEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            ICacheService cacheService) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.cacheService = cacheService;
        }

        protected override IRepository<TournamentParticipantEntity> GetRepository()
            => this.AppUnitOfWork.TournamentParticipantRepository;

        public async Task<List<TournamentParticipantOverview>> GetByTournament(Guid tournamentId)
        {
            string cacheKey = $"tournament_participants:{tournamentId}";
            var cached = await cacheService.GetAsync<List<TournamentParticipantOverview>>(cacheKey);
            if (cached != null) return cached;

            var tournament = await this.AppUnitOfWork.TournamentRepository.GetByIdOrThrowIfNull(tournamentId);

            List<TournamentParticipantOverview> result;
            if (tournament.IsTeamTournament)
            {
                var teams = await this.AppUnitOfWork.TournamentTeamRepository.GetByTournamentId(tournamentId);

                result = teams.Select(team => new TournamentParticipantOverview
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
            else
            {
                var participants = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTournamentId(tournamentId);
                result = participants ?? new List<TournamentParticipantOverview>();
            }

            await cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(2));

            return result;
        }

        public async Task RemoveUser(Guid tournamentId, Guid userId)
        {
            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetUserByTournamentId(tournamentId, userId);

            var registration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetUserByTournamentId(tournamentId, userId);

            await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);
            await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
        }

        public async Task RemoveTeam(Guid tournamentId, Guid teamId)
        {
            var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(teamId);
            if (team == null) throw new Exception("Team not found.");

            foreach (var member in team.Members)
            {
                await this.AppUnitOfWork.TournamentTeamMemberRepository.SoftDeleteEntity(member, this.UserContextReader);
            }

            await this.AppUnitOfWork.TournamentTeamRepository.SoftDeleteEntity(team, this.UserContextReader);

            var participant = await this.AppUnitOfWork.TournamentParticipantRepository.GetByTeamId(teamId);
            if (participant != null)
                await this.AppUnitOfWork.TournamentParticipantRepository.HardDeleteEntity(participant);

            var registration = await this.AppUnitOfWork.TournamentRegistrationRepository.GetByTeamId(teamId);
            if (registration != null)
                await this.AppUnitOfWork.TournamentRegistrationRepository.HardDeleteEntity(registration);

            await this.SaveAsync();

            await cacheService.RemoveAsync($"tournament_participants:{tournamentId}");
        }

        // SaveEntity is the only path that adds a new participant (called from registration approval
        // and team registration flow). Invalidate the participants cache so the next read sees the
        // new participant immediately instead of waiting for the 2-minute TTL.
        protected override async Task BeforeSave(TournamentParticipantEntity entity, TournamentParticipantPost inputDto, bool isNew)
        {
            if (entity.TournamentId.HasValue)
            {
                await cacheService.RemoveAsync($"tournament_participants:{entity.TournamentId.Value}");
            }
        }
    }
}