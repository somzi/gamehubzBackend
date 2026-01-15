using System;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentGroupPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public string Name { get; set; } = "";

        public Guid? TournamentStageId { get; set; }


    }
}