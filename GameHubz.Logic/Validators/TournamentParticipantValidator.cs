using FluentValidation;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Validators
{
    public class TournamentParticipantValidator : AbstractValidator<TournamentParticipantEntity>
    {
        public TournamentParticipantValidator(ILocalizationService localizationService)
        {

        }
    }
}