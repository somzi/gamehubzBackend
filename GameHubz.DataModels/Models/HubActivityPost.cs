using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Interfaces;
using System;

namespace GameHubz.DataModels.Models
{
    public class HubActivityPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public HubActivityType Type { get; set; }

        public Guid? HubId { get; set; }

        public Guid? TournamentId { get; set; }

        public TournamentPost? Tournament { get; set; }
    }
}