using System;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class MatchPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid TournamentId { get; set; }

        public int? RoundNumber { get; set; }

        public Guid HomeUserId { get; set; }

        public Guid AwayUserId { get; set; }

        public int? HomeUserScore { get; set; }

        public int? AwayUserScore { get; set; }

        public DateTime? ScheduledStartTime { get; set; }

        public string? Status { get; set; }

        public Guid? WinnerUserId { get; set; }
    }
}