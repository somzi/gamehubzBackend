using System;
using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class MatchChatPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public string Content { get; set; } = "";

        public Guid? MatchId { get; set; }

        public Guid? UserId { get; set; }


    }
}