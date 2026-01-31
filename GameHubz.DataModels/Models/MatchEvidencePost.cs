using System;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class MatchEvidencePost : IEditableDto
    {
        public Guid? Id { get; set; }

        public Guid? MatchId { get; set; }
        public string? Url { get; set; }
    }
}